using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json.Nodes;
using SocAiChat.Services;

namespace SocAiChat.Agent;

/// <summary>
/// La herramienta que la IA puede usar en «modo trabajo»: ejecutar una linea de ordenes de
/// PowerShell en este PC y leer su salida. La ejecucion pasa por la aplicacion, que pide
/// confirmacion al usuario (salvo que la haya quitado para esa conversacion) y deja registro de
/// cada orden en <c>logs\commands.log</c> (constitucion de herramientas, 4).
/// </summary>
public static class CommandTool
{
    public const string Name = "run_command";
    private const int MaxOutputChars = 12_000;

    /// <summary>La definicion que se manda al modelo (formato de herramientas de OpenAI).</summary>
    public static JsonArray Definitions() =>
    [
        new JsonObject
        {
            ["type"] = "function",
            ["function"] = new JsonObject
            {
                ["name"] = Name,
                ["description"] = "Run one PowerShell command line on the user's Windows PC and return its output (stdout and stderr). Use it to inspect files, run programs, git, dotnet, python, etc. The user sees and approves each command before it runs.",
                ["parameters"] = new JsonObject
                {
                    ["type"] = "object",
                    ["properties"] = new JsonObject
                    {
                        ["command"] = new JsonObject { ["type"] = "string", ["description"] = "The PowerShell command line to run." },
                        ["reason"] = new JsonObject { ["type"] = "string", ["description"] = "One short sentence, in the user's language, saying why." },
                    },
                    ["required"] = new JsonArray("command"),
                },
            },
        },
    ];

    /// <summary>Instrucciones que acompañan al modelo cuando el modo trabajo esta activo.</summary>
    public static string SystemPrompt(string workingFolder) =>
        $"You can run PowerShell commands on the user's Windows PC with the tool `{Name}`. " +
        $"The working folder is `{workingFolder}`. Prefer one command per call and read its output before the next. " +
        "Never run destructive commands (delete, format, overwrite, force push) unless the user asked for exactly that. " +
        "When you are done, answer the user in their language and summarise what you did.";

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
        var result = new Result(timedOut ? -1 : process.ExitCode, text, timedOut);
        Log(command, result);
        return result;
    }

    private static void Log(string command, Result result)
    {
        try
        {
            Directory.CreateDirectory(Paths.Logs);
            File.AppendAllText(Path.Combine(Paths.Logs, "commands.log"),
                $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} [{(result.TimedOut ? "timeout" : result.ExitCode.ToString())}] {command}{Environment.NewLine}");
        }
        catch (Exception) { }
    }
}
