using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Hosting;
using System;
using System.IO;
using System.Text;
using System.CodeDom.Compiler;
using Microsoft.CSharp;
using System.Threading.Tasks;

var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();

// POST /upload 엔드포인트를 통해 HTML 파일을 업로드하고, 생성된 EXE 파일을 다운로드합니다.
app.MapPost("/upload", async (HttpRequest request) =>
{
    if (!request.HasFormContentType)
        return Results.BadRequest("폼 형식의 요청이 아닙니다.");

    var form = await request.ReadFormAsync();
    var file = form.Files["file"]; // 클라이언트의 form 데이터에서 입력 이름이 "file"인 요소 사용

    if (file == null || file.Length == 0)
        return Results.BadRequest("업로드된 파일이 없습니다.");

    string htmlContent;
    // 업로드된 파일의 HTML 내용을 읽어옵니다.
    using (var reader = new StreamReader(file.OpenReadStream()))
    {
        htmlContent = await reader.ReadToEndAsync();
    }

    try
    {
        // HTML 내용을 포함하는 EXE 파일을 동적으로 생성합니다.
        byte[] exeBytes = CompileHtmlToExe(htmlContent);
        // 생성된 EXE 파일을 바이트 배열로 클라이언트에 전송하며, 파일명은 GeneratedHtmlViewer.exe 로 지정합니다.
        return Results.File(exeBytes, "application/octet-stream", "GeneratedHtmlViewer.exe");
    }
    catch (Exception ex)
    {
        return Results.BadRequest("컴파일 에러: " + ex.Message);
    }
});

app.Run();

/// <summary>
/// 업로드된 HTML 문자열을 포함하는 Windows Forms EXE 파일을 동적으로 생성하여 byte 배열로 반환합니다.
/// </summary>
/// <param name="htmlContent">업로드된 HTML 콘텐트</param>
/// <returns>생성된 EXE 파일의 바이트 배열</returns>
byte[] CompileHtmlToExe(string htmlContent)
{
    // HTML 내용 내 특수 문자나 줄바꿈 등에 안전하게 포함하기 위해 Base64 인코딩
    string htmlContentBase64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(htmlContent));

    // 생성할 Windows Forms 응용프로그램 코드 
    // 이 코드는 WebBrowser 컨트롤을 사용하여 Base64로 인코딩된 HTML 내용을 복원 후 화면에 출력합니다.
    string code = $@"
using System;
using System.Windows.Forms;
using System.Text;

namespace GeneratedHtmlViewer
{{
    static class Program
    {{
        [STAThread]
        static void Main()
        {{
            // Base64로 인코딩된 HTML 내용을 복원합니다.
            var htmlBase64 = ""{htmlContentBase64}"";
            string htmlContent = Encoding.UTF8.GetString(Convert.FromBase64String(htmlBase64));
            
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            Form form = new Form();
            form.Text = ""HTML Viewer"";

            WebBrowser webBrowser = new WebBrowser();
            webBrowser.Dock = DockStyle.Fill;
            form.Controls.Add(webBrowser);

            // HTML 내용을 WebBrowser 컨트롤에 로드
            webBrowser.DocumentText = htmlContent;

            Application.Run(form);
        }}
    }}
}}
";

    // CSharpCodeProvider를 사용하여 위 코드를 컴파일
    using (var provider = new CSharpCodeProvider())
    {
        CompilerParameters parameters = new CompilerParameters
        {
            GenerateExecutable = true,
            // 임시 파일로 EXE를 출력 (GUID를 사용해 유일한 파일명 생성)
            OutputAssembly = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString() + ".exe")
        };

        // Windows Forms 응용 프로그램에 필요한 어셈블리 참조 추가
        parameters.ReferencedAssemblies.Add("System.dll");
        parameters.ReferencedAssemblies.Add("System.Windows.Forms.dll");
        parameters.ReferencedAssemblies.Add("System.Drawing.dll");

        CompilerResults results = provider.CompileAssemblyFromSource(parameters, code);
        if (results.Errors.HasErrors)
        {
            StringBuilder errors = new StringBuilder();
            foreach (CompilerError error in results.Errors)
            {
                errors.AppendLine(error.ErrorText);
            }
            throw new Exception("컴파일에 실패했습니다: " + errors.ToString());
        }

        // 컴파일된 EXE 파일을 읽어 byte 배열로 반환한 후, 임시 파일은 삭제합니다.
        byte[] exeBytes = File.ReadAllBytes(parameters.OutputAssembly);
        File.Delete(parameters.OutputAssembly);
        return exeBytes;
    }
}
