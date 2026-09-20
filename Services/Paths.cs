using System.IO;

namespace SocAiChat.Services;

/// <summary>
/// Donde vive todo: <c>%LOCALAPPDATA%\sOCAIChat</c>. Los modelos y el motor pesan gigas, asi que
/// van en Local (no en Roaming) y nunca en la carpeta del programa.
/// </summary>
public static class Paths
{
    public static string Root { get; } = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "sOCAIChat");
    public static string Models => Path.Combine(Root, "models");
    public static string Engine => Path.Combine(Root, "engine");
    public static string Threads => Path.Combine(Root, "threads");
    public static string Logs => Path.Combine(Root, "logs");
    public static string SettingsFile => Path.Combine(Root, "settings.json");
    public static string EngineLog => Path.Combine(Logs, "engine.log");

    public static void Ensure()
    {
        foreach (var dir in new[] { Root, Models, Engine, Threads, Logs })
            Directory.CreateDirectory(dir);
    }
}
