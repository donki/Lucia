using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json.Nodes;
using SocLucia.Services;

namespace SocLucia.Agent;

/// <summary>
/// Ejecutar una linea de ordenes de PowerShell en este PC y leer su salida. Es una de las
/// herramientas de <see cref="ToolBox"/>: el permiso y el registro los pone la aplicacion.
/// </summary>
public static class CommandTool
{
    private const int MaxOutputChars = 12_000;

    public sealed record Result(int ExitCode, string Output, bool TimedOut);

    public static async Task<Result> RunAsync(string command, string workingFolder, CancellationToken cancel)
    {
        var shell = File.Exists(@"C:\Program Files\PowerShell\7\pwsh.exe") ? @"C:\Program Files\PowerShell\7\pwsh.exe" : "powershell.exe";
        var info = new ProcessStartInfo(shell)
        {
            WorkingDirectory = Directory.Exists(workingFolder) ? workingFolder : Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };
        foreach (var arg in new[] { "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-OutputFormat", "Text", "-Command", "[Console]::OutputEncoding=[Text.Encoding]::UTF8; " + command })
            info.ArgumentList.Add(arg);
        var output = new StringBuilder();
        using var process = Process.Start(info) ?? throw new InvalidOperationException("No se ha podido arrancar PowerShell");
        process.OutputDataReceived += (_, e) => { if (e.Data is not null) lock (output) output.AppendLine(e.Data); };
        process.ErrorDataReceived += (_, e) => { if (e.Data is not null) lock (output) output.AppendLine(e.Data); };
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        var timedOut = false;
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancel);
            timeout.CancelAfter(TimeSpan.FromMinutes(3));
            await process.WaitForExitAsync(timeout.Token);
        }
        catch (OperationCanceledException)
        {
            timedOut = !cancel.IsCancellationRequested;
            try { process.Kill(entireProcessTree: true); } catch (Exception) { }
            if (cancel.IsCancellationRequested) throw;
        }
        string text;
        lock (output) text = output.ToString();
        if (text.Length > MaxOutputChars)
            text = text[..(MaxOutputChars / 2)] + "\n…[salida recortada]…\n" + text[^(MaxOutputChars / 2)..];
        return new Result(timedOut ? -1 : process.ExitCode, text, timedOut);
    }
}
