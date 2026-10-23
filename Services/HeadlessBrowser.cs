using System.Diagnostics;
using System.IO;
using System.Text;
using SocLucia.Engine;

namespace SocLucia.Services;

/// <summary>
/// Lee una página con el navegador ya instalado (Edge o Chrome) en modo headless, como hace Playwright:
/// la abre, deja que el JavaScript la monte y devuelve el HTML final. Es el repliegue de <c>fetch_url</c>
/// cuando una web bloquea al cliente (403) o solo trae un esqueleto que el navegador rellena con scripts.
/// No descarga nada: usa el Chromium que el usuario ya tiene; si no hay ninguno, no hace nada.
/// </summary>
public static class HeadlessBrowser
{
    private static readonly string[] Candidates =
    [
        @"C:\Program Files\Microsoft\Edge\Application\msedge.exe",
        @"C:\Program Files (x86)\Microsoft\Edge\Application\msedge.exe",
        @"C:\Program Files\Google\Chrome\Application\chrome.exe",
        @"C:\Program Files (x86)\Google\Chrome\Application\chrome.exe",
    ];

    /// <summary>El primer navegador Chromium instalado, o null.</summary>
    public static string? FindBrowser() => Candidates.FirstOrDefault(File.Exists);

    public static bool Available => FindBrowser() is not null;

    /// <summary>
    /// Los argumentos del volcado del DOM. <c>--proxy-server=direct://</c> es clave: sin él, en PCs con
    /// una política de proxy el navegador headless no carga nada (pasó en el PC de Josep el 2026-10-23).
    /// </summary>
    public static List<string> BuildArgs(string url, string userDataDir) =>
    [
        "--headless=new", "--disable-gpu", "--no-sandbox", "--disable-dev-shm-usage", "--disable-extensions",
        "--no-first-run", "--no-default-browser-check", "--disable-background-networking",
        "--proxy-server=direct://", "--proxy-bypass-list=*",
        "--user-data-dir=" + userDataDir, "--virtual-time-budget=8000", "--dump-dom", url,
    ];

    /// <summary>El HTML final de la página (tras el JavaScript), o null si no hay navegador o no se pudo.</summary>
    public static async Task<string?> RenderAsync(string url, CancellationToken cancel)
    {
        var exe = FindBrowser();
        if (exe is null) return null;
        var profile = Path.Combine(Path.GetTempPath(), "sOCLucia-headless", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(profile);
        var info = new ProcessStartInfo(exe)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
        };
        foreach (var a in BuildArgs(url, profile)) info.ArgumentList.Add(a);
        try
        {
            using var process = Process.Start(info);
            if (process is null) return null;
            ChildJob.Add(process);
            var stdout = process.StandardOutput.ReadToEndAsync(cancel);
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancel);
            timeout.CancelAfter(TimeSpan.FromSeconds(30));
            try { await process.WaitForExitAsync(timeout.Token); }
            catch (OperationCanceledException) { try { process.Kill(entireProcessTree: true); } catch (Exception) { } cancel.ThrowIfCancellationRequested(); }
            var html = await stdout;
            return html.Length > 0 ? html : null;
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            DiagLog.Write("web", "el navegador headless no pudo leer la página: " + ex.Message);
            return null;
        }
        finally
        {
            try { Directory.Delete(profile, recursive: true); } catch (Exception) { }
        }
    }
}
