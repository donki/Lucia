using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Text;

namespace SocLucia.Services;

/// <summary>
/// Registro de diagnostico (<c>logs\diagnostics.log</c>): lo que hace la aplicacion y cuanto tarda
/// (enviar, arrancar y parar el motor, cargar el modelo, primer trozo y fin de cada respuesta,
/// cancelaciones, errores, congelaciones de la ventana), para poder pasarlo cuando algo va mal.
/// Nunca lleva el texto de las conversaciones ni datos personales (constitucion, seccion 10): solo
/// tiempos, tamaños, nombres de herramientas y tipos de error, con la carpeta del usuario tapada.
/// Rota por tamaño: al pasar de <see cref="MaxBytes"/> pasa a <c>diagnostics.1.log</c> y se guardan
/// <see cref="Keep"/> anteriores.
/// </summary>
public static class DiagLog
{
    public const string FileName = "diagnostics.log";

    /// <summary>Tamaño de cada fichero antes de rotar.</summary>
    public static long MaxBytes { get; internal set; } = 1024 * 1024;

    /// <summary>Ficheros anteriores que se guardan (diagnostics.1.log … diagnostics.N.log).</summary>
    public static int Keep { get; internal set; } = 4;

    public static string Current => Path.Combine(Paths.Logs, FileName);

    private static readonly object Gate = new();
    private static string _phase = "inactiva";
    private static readonly Stopwatch PhaseClock = Stopwatch.StartNew();

    /// <summary>Lo que esta haciendo la aplicacion ahora (para el vigilante de congelaciones).</summary>
    public static string Phase
    {
        get { lock (Gate) return $"{_phase} (desde hace {PhaseClock.Elapsed.TotalSeconds:0.0} s)"; }
    }

    public static void SetPhase(string phase)
    {
        lock (Gate)
        {
            _phase = phase;
            PhaseClock.Restart();
        }
    }

    /// <summary>Una linea con fecha y hora (milisegundos), la zona y el hilo. Nunca lanza.</summary>
    public static void Write(string area, string message)
    {
        try
        {
            var line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [{area}] (hilo {Environment.CurrentManagedThreadId}) {Scrub(message)}{Environment.NewLine}";
            lock (Gate)
            {
                Directory.CreateDirectory(Paths.Logs);
                var file = new FileInfo(Current);
                if (file.Exists && file.Length + line.Length > MaxBytes)
                    Rotate();
                File.AppendAllText(Current, line, Encoding.UTF8);
            }
        }
        catch (Exception) { }
    }

    private static void Rotate()
    {
        var folder = Paths.Logs;
        string Old(int n) => Path.Combine(folder, $"diagnostics.{n}.log");
        if (Keep <= 0)
        {
            File.Delete(Current);
            return;
        }
        File.Delete(Old(Keep));
        for (var n = Keep - 1; n >= 1; n--)
            if (File.Exists(Old(n)))
                File.Move(Old(n), Old(n + 1));
        File.Move(Current, Old(1));
    }

    /// <summary>Un error sin datos personales: su tipo y su mensaje con la carpeta del usuario tapada.</summary>
    public static string Describe(Exception ex) => $"{ex.GetType().Name}: {Scrub(ex.Message)}";

    /// <summary>La carpeta del usuario y su nombre se sustituyen (las rutas de los modelos y de los datos los llevan).</summary>
    public static string Scrub(string text)
    {
        var profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (profile.Length > 3)
            text = text.Replace(profile, "%USERPROFILE%", StringComparison.OrdinalIgnoreCase);
        var user = Environment.UserName;
        if (user.Length > 2)
            text = System.Text.RegularExpressions.Regex.Replace(text, $@"(?<![\p{{L}}\p{{N}}]){System.Text.RegularExpressions.Regex.Escape(user)}(?![\p{{L}}\p{{N}}])", "<usuario>", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        return text;
    }

    /// <summary>Los ficheros de registro que se pasan: el de diagnostico (con sus anteriores) y el del motor.</summary>
    public static IReadOnlyList<string> Files()
    {
        if (!Directory.Exists(Paths.Logs)) return [];
        return Directory.GetFiles(Paths.Logs, "diagnostics*.log")
            .Append(Paths.EngineLog)
            .Where(File.Exists)
            .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>
    /// Un zip con los registros (el del motor, con la carpeta del usuario tapada) y un resumen del PC
    /// y de la configuracion, en la carpeta de registros. Se guardan los tres ultimos. Devuelve su ruta.
    /// Los registros de ordenes y acciones del modo agente no van: llevan lo que se pidio hacer.
    /// </summary>
    public static string Bundle(string summary)
    {
        Directory.CreateDirectory(Paths.Logs);
        var stamp = $"sOCLucia-registros-{DateTime.Now:yyyyMMdd-HHmmss}";
        var zip = Path.Combine(Paths.Logs, stamp + ".zip");
        for (var n = 2; File.Exists(zip); n++)   // dos copias en el mismo segundo
            zip = Path.Combine(Paths.Logs, $"{stamp}-{n}.zip");
        var files = Files();
        using (var archive = ZipFile.Open(zip, ZipArchiveMode.Create))
        {
            foreach (var file in files)
            {
                string text;
                lock (Gate)
                    using (var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                    using (var reader = new StreamReader(stream, Encoding.UTF8))
                        text = reader.ReadToEnd();
                var entry = archive.CreateEntry(Path.GetFileName(file), CompressionLevel.Optimal);
                using var writer = new StreamWriter(entry.Open(), new UTF8Encoding(false));
                writer.Write(Scrub(text));
            }
            var info = archive.CreateEntry("resumen.txt");
            using (var writer = new StreamWriter(info.Open(), new UTF8Encoding(false)))
                writer.Write(Scrub(summary));
        }
        foreach (var old in Directory.GetFiles(Paths.Logs, "sOCLucia-registros-*.zip").OrderByDescending(f => f, StringComparer.Ordinal).Skip(3))
            try { File.Delete(old); } catch (Exception) { }
        return zip;
    }
}
