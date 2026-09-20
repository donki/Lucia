using System.IO;

namespace SocLucia.Services;

/// <summary>
/// Donde vive todo: <c>%LOCALAPPDATA%\sOCLucia</c>. Los modelos y el motor pesan gigas, asi que
/// van en Local (no en Roaming) y nunca en la carpeta del programa.
/// </summary>
public static class Paths
{
    public static string Root { get; } = DefaultRoot();

    private static string DefaultRoot()
    {
#if DEBUG
        // Para probar sin tocar los datos reales: SOC_LUCIA_ROOT=carpeta (solo en Debug).
        if (Environment.GetEnvironmentVariable("SOC_LUCIA_ROOT") is { Length: > 0 } sandbox)
            return sandbox;
#endif
        return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "sOCLucia");
    }
    /// <summary>Carpeta por defecto de los modelos; la efectiva la decide el ajuste ModelsFolder (pueden estar en otro disco).</summary>
    public static string DefaultModels => Path.Combine(Root, "models");
    public static string Models => AppSettings.Current.ModelsFolder is { Length: > 0 } f ? f : DefaultModels;
    public static string Engine => Path.Combine(Root, "engine");
    public static string Threads => Path.Combine(Root, "threads");
    public static string Logs => Path.Combine(Root, "logs");
    public static string SettingsFile => Path.Combine(Root, "settings.json");
    public static string EngineLog => Path.Combine(Logs, "engine.log");

    public static void Ensure()
    {
        MigrateOldRoot();
        foreach (var dir in new[] { Root, DefaultModels, Engine, Threads, Logs })
            Directory.CreateDirectory(dir);
        try { Directory.CreateDirectory(Models); } catch (Exception) { }
    }

    /// <summary>
    /// La aplicacion se llamo sOC AI Chat hasta 2026.9.20.3 y guardaba en <c>sOCAIChat</c>. Si esa
    /// carpeta existe y la nueva no, se renombra: conversaciones, ajustes, motor y modelos siguen donde estaban.
    /// </summary>
    private static void MigrateOldRoot()
    {
        var old = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "sOCAIChat");
        if (!Directory.Exists(old) || !string.Equals(Root, Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "sOCLucia"), StringComparison.OrdinalIgnoreCase))
            return;
        // Se funde carpeta a carpeta sin pisar nada de la nueva; lo que este en uso por la version
        // vieja (motor, registro) se queda y se reintenta en el siguiente arranque.
        try { Merge(old, Root); Directory.Delete(old); } catch (Exception) { }
    }

    private static void Merge(string from, string to)
    {
        if (!Directory.Exists(to)) { Directory.Move(from, to); return; }
        foreach (var dir in Directory.GetDirectories(from))
            try { Merge(dir, Path.Combine(to, Path.GetFileName(dir))); } catch (Exception) { }
        foreach (var file in Directory.GetFiles(from))
        {
            var target = Path.Combine(to, Path.GetFileName(file));
            try { if (!File.Exists(target)) File.Move(file, target); else File.Delete(file); } catch (Exception) { }
        }
        try { Directory.Delete(from); } catch (Exception) { }
    }
}
