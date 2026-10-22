using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Net.Sockets;
using System.Net;
using SocLucia.Services;

namespace SocLucia.Engine;

public enum EngineState { NoModel, DownloadingEngine, Starting, Ready, Stopped, Error }

/// <summary>
/// Arranca y vigila <c>llama-server</c> como proceso hijo: un solo modelo, en 127.0.0.1 y en un
/// puerto libre. La interfaz solo ve un estado («Preparando la IA…», lista, error); el puerto y el
/// PID son cosa de esta clase. Al cerrar la aplicacion se mata el proceso.
/// </summary>
public sealed class EngineHost : IDisposable
{
    private readonly SemaphoreSlim _lock = new(1, 1);
    private Process? _process;
    private int _port;
    private string? _modelPath;
    private int _context;
    private string? _lora;
    private EnginePin.Archive? _archive;
    private bool _cudaFailed;

    public EngineState State { get; private set; } = EngineState.NoModel;
    public string? Detail { get; private set; }
    public event Action? StateChanged;

    /// <summary>La URL base del servidor si esta listo; si no, la arranca (descargando el motor si hace falta).</summary>
    public async Task<string> EnsureReadyAsync(IProgress<Downloader.Progress>? engineDownload, CancellationToken cancel)
    {
        var settings = AppSettings.Current;
        if (!settings.HasModel)
        {
            Set(EngineState.NoModel, null);
            throw new InvalidOperationException(Localization.Loc.Get("NoModelYet"));
        }
        await _lock.WaitAsync(cancel);
        try
        {
            // Otro modelo u otro contexto (Ajustes) piden reiniciar el motor; lo demas, reutilizarlo.
            var context = TokenLimits.Context(settings);
            var lora = FineTune.Adapters.ActiveFor(settings, settings.ModelPath)?.Path;
            if (_process is { HasExited: false } && _modelPath == settings.ModelPath && _context == context && _lora == lora && await HealthyAsync(_port, cancel))
            {
                Set(EngineState.Ready, null);
                return BaseUrl;
            }
            if (_process is not null)
                DiagLog.Write("motor", _context != context && _modelPath == settings.ModelPath ? $"se reinicia: contexto {_context} -> {context}"
                    : _lora != lora && _modelPath == settings.ModelPath ? "se reinicia: otro adaptador" : "se reinicia (otro modelo o no responde)");
            Stop();
            var archive = _archive ?? EnginePin.ForThisMachine(allowCuda: !_cudaFailed);
            var folder = await EnsureBinariesAsync(archive, engineDownload, cancel);
            try
            {
                await StartAsync(folder, archive, settings, cancel);
            }
            catch (Exception ex) when (archive.Accelerator == "cuda" && !cancel.IsCancellationRequested && ex is not EngineLoadException { ModelProblem: true })
            {
                // CUDA que no arranca (driver viejo, DLL que falta): se sigue con Vulkan y no se insiste en esta sesion.
                Log($"CUDA no arranca; se prueba con Vulkan");
                _cudaFailed = true;
                archive = EnginePin.WinVulkanX64;
                folder = await EnsureBinariesAsync(archive, engineDownload, cancel);
                await StartAsync(folder, archive, settings, cancel);
            }
            _archive = archive;
            if (settings.BrokenModels.Remove(settings.ModelPath!)) settings.Save();
            _modelPath = settings.ModelPath;
            _context = context;
            _lora = lora;
            Set(EngineState.Ready, null);
            return BaseUrl;
        }
        catch (OperationCanceledException)
        {
            DiagLog.Write("motor", "arranque cancelado");
            Stop();
            Set(EngineState.Stopped, null);
            throw;
        }
        catch (Exception ex)
        {
            DiagLog.Write("motor", "no arranca: " + DiagLog.Describe(ex));
            if (ex is EngineLoadException { ModelProblem: true } && settings.ModelPath is { } broken)
            {
                // Se recuerda para avisarlo en la lista de IA instaladas (hasta que arranque bien).
                settings.BrokenModels[broken] = ex.Message;
                try { settings.Save(); } catch (Exception) { }
            }
            Stop();
            Set(EngineState.Error, ex.Message);
            throw;
        }
        finally
        {
            _lock.Release();
        }
    }

    public string BaseUrl => $"http://127.0.0.1:{_port}";

    /// <summary>El modelo cargado entiende imagenes (arranco con su mmproj).</summary>
    public bool Vision { get; private set; }

    /// <summary>Si el modelo activo tiene su parte de vision descargada (sin arrancar nada).</summary>
    public static bool VisionAvailable(AppSettings settings) => settings.ModelPath is { Length: > 0 } p && File.Exists(ModelCatalog.MmprojPathFor(p));
    public string Accelerator => _archive?.Accelerator ?? "-";

    /// <summary>El adaptador (lo aprendido) con el que arranco el motor, si hay.</summary>
    public FineTune.LoraAdapter? Adapter { get; private set; }

    /// <summary>La carpeta del motor (bajandolo si falta) para usar sus otras herramientas, como llama-quantize.</summary>
    public async Task<string> EnsureBinariesForToolsAsync(CancellationToken cancel)
    {
        var archive = _archive ?? EnginePin.ForThisMachine(allowCuda: !_cudaFailed);
        return await EnsureBinariesAsync(archive, null, cancel);
    }

    private async Task<string> EnsureBinariesAsync(EnginePin.Archive archive, IProgress<Downloader.Progress>? progress, CancellationToken cancel)
    {
        var folder = Path.Combine(Paths.Engine, $"{EnginePin.Build}-{archive.Accelerator}");
        var exe = Path.Combine(folder, "llama-server.exe");
        var complete = Path.Combine(folder, ".completa");
        if (File.Exists(exe) && File.Exists(complete))
            return folder;
        Set(EngineState.DownloadingEngine, null);
        DiagLog.Write("motor", $"descargando el motor {EnginePin.Build}-{archive.Accelerator}");
        if (Directory.Exists(folder))
            Directory.Delete(folder, recursive: true);
        Directory.CreateDirectory(folder);
        foreach (var part in new[] { archive, archive.Companion }.Where(a => a is not null))
        {
            var zip = Path.Combine(Paths.Engine, part!.FileName);
            if (!File.Exists(zip))
                await Downloader.DownloadAsync(part.Url, zip, part.Sha256, progress, cancel);
            // Los zips de llama.cpp llevan los ficheros en la raiz (o en una carpeta unica): se aplanan.
            using (var archiveZip = ZipFile.OpenRead(zip))
            {
                foreach (var entry in archiveZip.Entries)
                {
                    if (string.IsNullOrEmpty(entry.Name))
                        continue;
                    entry.ExtractToFile(Path.Combine(folder, entry.Name), overwrite: true);
                }
            }
            File.Delete(zip);
        }
        if (!File.Exists(exe))
            throw new FileNotFoundException("El zip del motor no trae llama-server.exe");
        File.WriteAllText(complete, DateTime.Now.ToString("s"));
        return folder;
    }

    private async Task StartAsync(string folder, EnginePin.Archive archive, AppSettings settings, CancellationToken cancel)
    {
        Set(EngineState.Starting, null);
        _port = FreePort();
        var gpuLayers = EnginePin.GpuLayers(archive);
        var flash = archive.Accelerator == "cpu" ? "auto" : "on";
        var info = new ProcessStartInfo(Path.Combine(folder, "llama-server.exe"))
        {
            WorkingDirectory = folder,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        foreach (var arg in new[]
        {
            "-m", settings.ModelPath!, "--host", "127.0.0.1", "--port", _port.ToString(),
            "-c", TokenLimits.Context(settings).ToString(), "-np", "1",
            "-ngl", gpuLayers.ToString(), "-fa", flash, "--jinja", "--reasoning-format", "auto", "--no-webui",
        })
            info.ArgumentList.Add(arg);
        // Con la parte de vision al lado, el modelo entiende las imagenes que se adjuntan.
        var mmproj = ModelCatalog.MmprojPathFor(settings.ModelPath!);
        if (File.Exists(mmproj))
        {
            info.ArgumentList.Add("--mmproj");
            info.ArgumentList.Add(mmproj);
            Vision = true;
        }
        else
            Vision = false;
        // Lo aprendido con los ejemplos del usuario (Enseñar a Lucia): un adaptador LoRA encima de la IA.
        Adapter = FineTune.Adapters.ActiveFor(settings, settings.ModelPath);
        if (Adapter is not null)
        {
            info.ArgumentList.Add("--lora");
            info.ArgumentList.Add(Adapter.Path);
        }
        Log($"arrancando llama-server ({archive.Accelerator}) con {Path.GetFileName(settings.ModelPath)} en :{_port}");
        DiagLog.Write("motor", $"arrancando llama-server {EnginePin.Build}-{archive.Accelerator}, modelo {Path.GetFileName(settings.ModelPath)}, contexto {TokenLimits.Context(settings)}, capas en GPU {gpuLayers}, vision {(Vision ? "si" : "no")}, adaptador {Adapter?.Name ?? "ninguno"}");
        var loading = Stopwatch.StartNew();
        var process = Process.Start(info) ?? throw new InvalidOperationException("No se ha podido arrancar llama-server");
        ChildJob.Add(process);   // muere con la aplicacion, pase lo que pase
        _process = process;
        // Las lineas de error se guardan para explicar en claro por que no carga (EngineErrors).
        var errors = new List<string>();
        void Take(string? line)
        {
            if (line is null) return;
            Log(line);
            if (EngineErrors.IsErrorLine(line)) lock (errors) if (errors.Count < 40) errors.Add(line);
        }
        process.OutputDataReceived += (_, e) => Take(e.Data);
        process.ErrorDataReceived += (_, e) => Take(e.Data);
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        // Cargar pesos puede llevar un minuto largo en discos lentos o modelos grandes.
        var deadline = DateTime.UtcNow.AddMinutes(4);
        while (DateTime.UtcNow < deadline)
        {
            cancel.ThrowIfCancellationRequested();
            if (process.HasExited)
            {
                process.WaitForExit();   // que lleguen las ultimas lineas de su salida
                string why;
                bool modelProblem;
                lock (errors)
                {
                    why = EngineErrors.Explain(errors.ToList(), process.ExitCode, settings.ModelName ?? Path.GetFileName(settings.ModelPath!));
                    modelProblem = EngineErrors.IsModelProblem(errors.ToList());
                }
                DiagLog.Write("motor", $"llama-server se ha cerrado al cargar (codigo {process.ExitCode}) a los {loading.Elapsed.TotalSeconds:0.0} s: {why}");
                throw new EngineLoadException(why, modelProblem);
            }
            if (await HealthyAsync(_port, cancel))
            {
                DiagLog.Write("motor", $"modelo cargado en {loading.Elapsed.TotalSeconds:0.0} s (puerto {_port})");
                return;
            }
            await Task.Delay(500, cancel);
        }
        DiagLog.Write("motor", $"el modelo no ha cargado en {loading.Elapsed.TotalMinutes:0} min");
        throw new TimeoutException(Localization.Loc.Get("EngineTimeout"));
    }

    private static async Task<bool> HealthyAsync(int port, CancellationToken cancel)
    {
        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancel);
            cts.CancelAfter(2000);
            using var response = await Downloader.Http.GetAsync($"http://127.0.0.1:{port}/health", cts.Token);
            return response.IsSuccessStatusCode;
        }
        catch (Exception) when (!cancel.IsCancellationRequested)
        {
            return false;
        }
    }

    public void Stop()
    {
        try
        {
            if (_process is { HasExited: false })
            {
                DiagLog.Write("motor", "parado");
                _process.Kill(entireProcessTree: true);
            }
        }
        catch (Exception) { }
        _process?.Dispose();
        _process = null;
        _modelPath = null;
    }

    private static int FreePort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    private void Set(EngineState state, string? detail)
    {
        State = state;
        Detail = detail;
        StateChanged?.Invoke();
    }

    private static readonly object LogLock = new();

    public static void Log(string line)
    {
        try
        {
            lock (LogLock)
            {
                Directory.CreateDirectory(Paths.Logs);
                var log = new FileInfo(Paths.EngineLog);
                if (log.Exists && log.Length > 5_000_000)
                    log.Delete();
                File.AppendAllText(Paths.EngineLog, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} {line}{Environment.NewLine}");   // con fecha: sin ella, lineas de dias distintos parecian de la misma sesion
            }
        }
        catch (Exception) { }
    }

    public void Dispose()
    {
        Stop();
        _lock.Dispose();
    }
}
