using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Text.Json;
using SocLucia.Services;

namespace SocLucia.Engine;

/// <summary>
/// Imagenes generadas en el PC con <c>stable-diffusion.cpp</c> (MIT), fijado como llama.cpp: se
/// descarga de sus releases de GitHub la primera vez, se comprueba su SHA-256 y se desempaqueta
/// en <c>engine\sd-&lt;build&gt;-&lt;acelerador&gt;</c>. El modelo es Stable Diffusion 1.5 en GGUF
/// (Q8_0, ~1,7 GB; licencia CreativeML OpenRAIL-M, de uso comercial con restricciones de uso).
/// </summary>
/// <remarks>
/// La grafica no da para las dos IA a la vez (la de texto ocupa casi toda la memoria de video):
/// antes de generar se para llama-server, y la siguiente pregunta lo vuelve a arrancar. Si la
/// grafica no puede (poca memoria, sin Vulkan) se genera en la CPU, mas despacio.
/// </remarks>
public static class ImageEngine
{
    public const string Build = "master-890-74988b2";
    private const string Base = "https://github.com/leejet/stable-diffusion.cpp/releases/download/" + Build + "/";

    public sealed record Archive(string Accelerator, string Url, string Sha256, string FileName);

    /// <summary>Vulkan: cualquier grafica (NVIDIA, AMD, Intel). 32 MB.</summary>
    public static readonly Archive WinVulkanX64 = new("vulkan", Base + "sd-master-74988b2-bin-win-vulkan-x64.zip",
        "744c8f817c66ecfd02fbb9dc8b122e1f29f7240db1f6086dfde2669403c5d896", "sd-master-74988b2-bin-win-vulkan-x64.zip");

    /// <summary>Solo CPU (sin grafica util). 17 MB.</summary>
    public static readonly Archive WinCpuX64 = new("cpu", Base + "sd-master-74988b2-bin-win-cpu-x64.zip",
        "24fcac70fadc41eb3524837893f1cb7ea54895fe4d807f88ef7afdeee8bd484b", "sd-master-74988b2-bin-win-cpu-x64.zip");

    /// <summary>El modelo de imagenes: repositorio GGUF y cuantizado preferido.</summary>
    public sealed record ImageModel(string Name, string Repo, string Quant, long ApproxBytes, string License);

    public static readonly ImageModel Default = new("Stable Diffusion 1.5", "second-state/stable-diffusion-v1-5-GGUF", "Q8_0", 1764L * 1024 * 1024, "CreativeML OpenRAIL-M");

    public static Archive ForThisMachine() => PcProfile.Detect("vulkan").HasUsableGpu ? WinVulkanX64 : WinCpuX64;

    public static string ModelPath => Path.Combine(Paths.Models, "stable-diffusion-v1-5-pruned-emaonly-Q8_0.gguf");
    public static bool ModelInstalled => File.Exists(ModelPath);
    public static string ImagesFolder => Path.Combine(Paths.Root, "images");

    private static string Folder(Archive a) => Path.Combine(Paths.Engine, $"sd-{Build}-{a.Accelerator}");
    public static bool EngineInstalled => File.Exists(Path.Combine(Folder(ForThisMachine()), "sd-cli.exe")) && File.Exists(Path.Combine(Folder(ForThisMachine()), ".completa"));

    public static event Action<string?>? Status;

    /// <summary>Motor y modelo, descargados si faltan (con progreso).</summary>
    public static async Task EnsureInstalledAsync(IProgress<Downloader.Progress>? progress, CancellationToken cancel)
    {
        var archive = ForThisMachine();
        var folder = Folder(archive);
        var exe = Path.Combine(folder, "sd-cli.exe");
        var complete = Path.Combine(folder, ".completa");
        if (!File.Exists(exe) || !File.Exists(complete))
        {
            Status?.Invoke(Localization.Loc.Get("ImageEngineDownloading"));
            if (Directory.Exists(folder))
                Directory.Delete(folder, recursive: true);
            Directory.CreateDirectory(folder);
            var zip = Path.Combine(Paths.Engine, archive.FileName);
            if (!File.Exists(zip))
                await Downloader.DownloadAsync(archive.Url, zip, archive.Sha256, progress, cancel);
            using (var archiveZip = ZipFile.OpenRead(zip))
                foreach (var entry in archiveZip.Entries)
                    if (!string.IsNullOrEmpty(entry.Name))
                        entry.ExtractToFile(Path.Combine(folder, entry.Name), overwrite: true);
            File.Delete(zip);
            if (!File.Exists(exe))
                throw new FileNotFoundException("El zip de stable-diffusion.cpp no trae sd-cli.exe");
            File.WriteAllText(complete, DateTime.Now.ToString("s"));
        }
        if (!ModelInstalled)
        {
            Status?.Invoke(Localization.Loc.Format("ImageModelDownloading", Default.Name));
            Directory.CreateDirectory(Paths.Models);
            var url = $"https://huggingface.co/{Default.Repo}/resolve/main/{Path.GetFileName(ModelPath)}?download=true";
            await Downloader.DownloadAsync(url, ModelPath, null, progress, cancel);
        }
        Status?.Invoke(null);
    }

    public sealed record Request(string Prompt, string Negative = "", int Width = 512, int Height = 512, int Steps = 20, long Seed = -1);

    /// <summary>Genera una imagen y devuelve su ruta (PNG en <c>images\</c>). Para la IA de texto antes: no caben las dos en la grafica.</summary>
    public static async Task<string> GenerateAsync(Request request, IProgress<Downloader.Progress>? progress, CancellationToken cancel)
    {
        await EnsureInstalledAsync(progress, cancel);
        var archive = ForThisMachine();
        var exe = Path.Combine(Folder(archive), "sd-cli.exe");
        Directory.CreateDirectory(ImagesFolder);
        var output = Path.Combine(ImagesFolder, $"{DateTime.Now:yyyyMMdd-HHmmss}.png");
        App.Engine.Stop();
        Status?.Invoke(Localization.Loc.Get("ImageGenerating"));
        try
        {
            var seed = request.Seed >= 0 ? request.Seed : Random.Shared.NextInt64(1, int.MaxValue);
            var width = Math.Clamp(request.Width / 64 * 64, 256, 768);
            var height = Math.Clamp(request.Height / 64 * 64, 256, 768);
            if (archive.Accelerator != "cpu")
            {
                // Primero en la grafica; si no puede (memoria), en la CPU.
                if (await RunAsync(exe, request, output, width, height, seed, cpu: false, cancel))
                    return output;
                EngineHost.Log("sd-cli en la grafica ha fallado; se prueba en la CPU");
                Status?.Invoke(Localization.Loc.Get("ImageGeneratingCpu"));
            }
            if (await RunAsync(exe, request, output, width, height, seed, cpu: true, cancel))
                return output;
            throw new InvalidOperationException(Localization.Loc.Get("ImageFailed"));
        }
        finally
        {
            Status?.Invoke(null);
        }
    }

    private static async Task<bool> RunAsync(string exe, Request request, string output, int width, int height, long seed, bool cpu, CancellationToken cancel)
    {
        var info = new ProcessStartInfo(exe)
        {
            WorkingDirectory = Path.GetDirectoryName(exe)!,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        foreach (var arg in new[]
        {
            "-m", ModelPath, "-p", request.Prompt, "-n", request.Negative, "-o", output,
            "--steps", request.Steps.ToString(), "-W", width.ToString(), "-H", height.ToString(), "--cfg-scale", "7", "-s", seed.ToString(),
            "--vae-tiling",
        })
            info.ArgumentList.Add(arg);
        if (cpu) { info.ArgumentList.Add("--backend"); info.ArgumentList.Add("cpu"); }
        EngineHost.Log($"sd-cli {(cpu ? "cpu" : "gpu")} {width}x{height} steps={request.Steps} seed={seed}: {request.Prompt}");
        using var process = Process.Start(info) ?? throw new InvalidOperationException("No se ha podido arrancar sd-cli");
        ChildJob.Add(process);
        process.OutputDataReceived += (_, e) => { if (e.Data is not null && !e.Data.Contains('|')) EngineHost.Log(e.Data); };
        process.ErrorDataReceived += (_, e) => { if (e.Data is not null && !e.Data.Contains('|')) EngineHost.Log(e.Data); };
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        try
        {
            await process.WaitForExitAsync(cancel);
        }
        catch (OperationCanceledException)
        {
            try { process.Kill(entireProcessTree: true); } catch (Exception) { }
            throw;
        }
        return process.ExitCode == 0 && File.Exists(output);
    }

    /// <summary>Espacio que ocupan el motor y el modelo de imagenes, para Ajustes.</summary>
    public static long InstalledBytes()
    {
        long total = 0;
        try { if (ModelInstalled) total += new FileInfo(ModelPath).Length; } catch (Exception) { }
        try
        {
            foreach (var dir in Directory.Exists(Paths.Engine) ? Directory.GetDirectories(Paths.Engine, "sd-*") : [])
                total += new DirectoryInfo(dir).EnumerateFiles("*", SearchOption.AllDirectories).Sum(f => f.Length);
        }
        catch (Exception) { }
        return total;
    }

    /// <summary>Borra el modelo y el motor de imagenes (se vuelven a bajar si se piden).</summary>
    public static void Uninstall()
    {
        try { if (ModelInstalled) File.Delete(ModelPath); } catch (Exception) { }
        try
        {
            foreach (var dir in Directory.Exists(Paths.Engine) ? Directory.GetDirectories(Paths.Engine, "sd-*") : [])
                Directory.Delete(dir, recursive: true);
        }
        catch (Exception) { }
    }
}
