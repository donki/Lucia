using System.Diagnostics;
using System.Formats.Tar;
using System.IO;
using System.IO.Compression;
using SocLucia.Engine;
using SocLucia.Services;

namespace SocLucia.FineTune;

/// <summary>
/// El entorno de Python con el que Lucia entrena y convierte modelos, en <c>finetune\</c>: uv, un
/// Python propio (no se usa ni se toca el del sistema), las bibliotecas fijadas en
/// <see cref="FineTunePin"/> y los convertidores de llama.cpp. Pesa unos 3-4 GB con CUDA, por eso se
/// instala solo cuando el usuario lo pide y se puede quitar desde Ajustes.
/// </summary>
public static class PythonEnv
{
    public static string Folder => Path.Combine(Paths.Root, "finetune");
    public static string UvExe => Path.Combine(Folder, "uv", "uv.exe");
    public static string EnvFolder => Path.Combine(Folder, "env");
    public static string PythonExe => Path.Combine(EnvFolder, "Scripts", "python.exe");
    public static string LlamaCppFolder => Path.Combine(Folder, "llamacpp");
    public static string ScriptPath => Path.Combine(Folder, "lucia_train.py");
    private static string Marker => Path.Combine(Folder, ".completa");

    /// <summary>Instalado y de la version fijada (si cambia la fijacion, se rehace).</summary>
    public static bool Installed => File.Exists(PythonExe) && File.Exists(Marker) && File.ReadAllText(Marker).StartsWith(FineTunePin.EnvVersion + "|", StringComparison.Ordinal);

    /// <summary>Con la grafica NVIDIA se instala PyTorch con CUDA; si no, el de CPU (entrenar ahi es muy lento).</summary>
    public static bool UseCuda => EnginePin.HasNvidiaDriver();

    /// <summary>Lo que ocupa en disco ahora mismo.</summary>
    public static long SizeOnDisk() => FolderSize(Folder, skip: "work");

    internal static long FolderSize(string folder, string? skip = null)
    {
        if (!Directory.Exists(folder)) return 0;
        long total = 0;
        foreach (var file in Directory.EnumerateFiles(folder, "*", new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true }))
        {
            if (skip is not null && file.StartsWith(Path.Combine(folder, skip) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) continue;
            try { total += new FileInfo(file).Length; } catch (Exception) { }
        }
        return total;
    }

    /// <summary>Monta todo lo que falte. Cada paso se cuenta por <paramref name="status"/> (texto ya traducido) y las descargas por <paramref name="progress"/>.</summary>
    public static async Task EnsureAsync(IProgress<string> status, IProgress<Downloader.Progress>? progress, CancellationToken cancel)
    {
        WriteScript();
        if (Installed) return;
        Directory.CreateDirectory(Folder);
        if (File.Exists(Marker)) File.Delete(Marker);

        if (!File.Exists(UvExe))
        {
            status.Report(Localization.Loc.Get("FtEnvUv"));
            var zip = Path.Combine(Folder, "uv.zip");
            await Downloader.DownloadAsync(FineTunePin.UvUrl, zip, FineTunePin.UvSha256, progress, cancel);
            Directory.CreateDirectory(Path.GetDirectoryName(UvExe)!);
            using (var archive = ZipFile.OpenRead(zip))
                foreach (var entry in archive.Entries.Where(e => e.Name.Length > 0))
                    entry.ExtractToFile(Path.Combine(Path.GetDirectoryName(UvExe)!, entry.Name), overwrite: true);
            File.Delete(zip);
        }

        status.Report(Localization.Loc.Get("FtEnvPython"));
        if (Directory.Exists(EnvFolder)) Directory.Delete(EnvFolder, recursive: true);
        await RunUvAsync(["venv", "--python", FineTunePin.PythonVersion, EnvFolder], cancel);

        status.Report(Localization.Loc.Get(UseCuda ? "FtEnvTorchCuda" : "FtEnvTorchCpu"));
        await RunUvAsync(["pip", "install", "--python", PythonExe, FineTunePin.Torch, "--index-url", UseCuda ? FineTunePin.TorchCudaIndex : FineTunePin.TorchCpuIndex], cancel);

        status.Report(Localization.Loc.Get("FtEnvLibraries"));
        await RunUvAsync(["pip", "install", "--python", PythonExe, .. FineTunePin.Packages], cancel);

        status.Report(Localization.Loc.Get("FtEnvConverters"));
        await EnsureLlamaCppAsync(progress, cancel);

        File.WriteAllText(Marker, $"{FineTunePin.EnvVersion}|{(UseCuda ? "cuda" : "cpu")}|{DateTime.Now:s}");
        Log("entorno instalado");
    }

    /// <summary>Solo lo que usan los convertidores del codigo de llama.cpp (sin el resto del repositorio).</summary>
    internal static bool IsConverterFile(string path)
    {
        var p = path.Replace('\\', '/');
        var slash = p.IndexOf('/');
        if (slash < 0) return false;
        var rel = p[(slash + 1)..];
        return rel is "convert_hf_to_gguf.py" or "convert_lora_to_gguf.py" or "LICENSE"
               || rel.StartsWith("gguf-py/", StringComparison.Ordinal)
               || rel.StartsWith("conversion/", StringComparison.Ordinal);
    }

    private static async Task EnsureLlamaCppAsync(IProgress<Downloader.Progress>? progress, CancellationToken cancel)
    {
        if (File.Exists(Path.Combine(LlamaCppFolder, "convert_lora_to_gguf.py"))) return;
        var tar = Path.Combine(Folder, "llamacpp.tar.gz");
        await Downloader.DownloadAsync(FineTunePin.LlamaCppUrl, tar, FineTunePin.LlamaCppSha256, progress, cancel);
        if (Directory.Exists(LlamaCppFolder)) Directory.Delete(LlamaCppFolder, recursive: true);
        await using (var file = File.OpenRead(tar))
        await using (var gzip = new GZipStream(file, CompressionMode.Decompress))
        {
            using var reader = new TarReader(gzip);
            while (reader.GetNextEntry() is { } entry)
            {
                if (entry.EntryType is not (TarEntryType.RegularFile or TarEntryType.V7RegularFile) || !IsConverterFile(entry.Name))
                    continue;
                var rel = entry.Name.Replace('\\', '/');
                rel = rel[(rel.IndexOf('/') + 1)..];
                var target = Path.GetFullPath(Path.Combine(LlamaCppFolder, rel));
                if (!target.StartsWith(Path.GetFullPath(LlamaCppFolder), StringComparison.OrdinalIgnoreCase)) continue;
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                entry.ExtractToFile(target, overwrite: true);
            }
        }
        File.Delete(tar);
    }

    /// <summary>El script de entrenamiento va dentro del exe; se escribe en cada uso (asi siempre es el de esta version).</summary>
    public static void WriteScript()
    {
        Directory.CreateDirectory(Folder);
        using var stream = typeof(PythonEnv).Assembly.GetManifestResourceStream("SocLucia.FineTune.lucia_train.py")
            ?? throw new FileNotFoundException("lucia_train.py no esta dentro de la aplicacion");
        using var reader = new StreamReader(stream);
        File.WriteAllText(ScriptPath, reader.ReadToEnd());
    }

    /// <summary>Quita el entorno (Python, bibliotecas, convertidores). Los adaptadores y los modelos base no se tocan.</summary>
    public static void Remove()
    {
        foreach (var dir in new[] { EnvFolder, Path.GetDirectoryName(UvExe)!, LlamaCppFolder, Path.Combine(Folder, "python"), Path.Combine(Folder, "uv-cache") })
            if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        if (File.Exists(Marker)) File.Delete(Marker);
    }

    /// <summary>Variables para que uv y Python se queden dentro de la carpeta de Lucia (ni %APPDATA% ni la cache global).</summary>
    internal static void Isolate(ProcessStartInfo info)
    {
        info.Environment["UV_PYTHON_INSTALL_DIR"] = Path.Combine(Folder, "python");
        info.Environment["UV_CACHE_DIR"] = Path.Combine(Folder, "uv-cache");
        info.Environment["UV_NO_CACHE"] = "1";
        info.Environment["UV_PYTHON_PREFERENCE"] = "only-managed";
        info.Environment["HF_HOME"] = Path.Combine(Folder, "hf");
        info.Environment["HF_HUB_DISABLE_TELEMETRY"] = "1";
        info.Environment["PYTHONIOENCODING"] = "utf-8";
        info.Environment["PYTHONUTF8"] = "1";
        info.Environment["PYTHONNOUSERSITE"] = "1";
        info.Environment.Remove("PYTHONPATH");
        info.Environment.Remove("PYTHONHOME");
        info.Environment.Remove("VIRTUAL_ENV");
    }

    private static async Task RunUvAsync(string[] args, CancellationToken cancel)
    {
        var info = new ProcessStartInfo(UvExe) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true, WorkingDirectory = Folder };
        foreach (var a in args) info.ArgumentList.Add(a);
        Isolate(info);
        Log("uv " + string.Join(' ', args));
        var (code, tail) = await RunAsync(info, null, cancel);
        if (code != 0)
            throw new InvalidOperationException(Localization.Loc.Format("FtEnvFailed", tail));
    }

    /// <summary>
    /// Lanza un proceso hijo (muere con la aplicacion), apunta su salida en finetune.log, pasa cada
    /// linea a <paramref name="line"/> y devuelve el codigo de salida y las ultimas lineas de error.
    /// </summary>
    internal static async Task<(int Code, string Tail)> RunAsync(ProcessStartInfo info, Action<string>? line, CancellationToken cancel)
    {
        info.StandardOutputEncoding = info.StandardErrorEncoding = System.Text.Encoding.UTF8;
        using var process = Process.Start(info) ?? throw new InvalidOperationException("No se ha podido arrancar " + info.FileName);
        ChildJob.Add(process);
        var tail = new Queue<string>();
        void Take(string? data)
        {
            if (data is null) return;
            Log(data);
            lock (tail) { tail.Enqueue(data); if (tail.Count > 6) tail.Dequeue(); }
            line?.Invoke(data);
        }
        process.OutputDataReceived += (_, e) => Take(e.Data);
        process.ErrorDataReceived += (_, e) => Take(e.Data);
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        using (cancel.Register(() => { try { if (!process.HasExited) process.Kill(entireProcessTree: true); } catch (Exception) { } }))
            await process.WaitForExitAsync(CancellationToken.None);
        cancel.ThrowIfCancellationRequested();
        lock (tail)
            return (process.ExitCode, string.Join(Environment.NewLine, tail.Where(l => !l.StartsWith("@lucia ", StringComparison.Ordinal))));
    }

    public static string LogPath => Path.Combine(Paths.Logs, "finetune.log");
    private static readonly object LogLock = new();

    public static void Log(string line)
    {
        try
        {
            lock (LogLock)
            {
                Directory.CreateDirectory(Paths.Logs);
                var log = new FileInfo(LogPath);
                if (log.Exists && log.Length > 5_000_000) log.Delete();
                File.AppendAllText(LogPath, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} {line}{Environment.NewLine}");
            }
        }
        catch (Exception) { }
    }
}
