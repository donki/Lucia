using System.IO;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Text.Json;
using SocLucia.Services;

namespace SocLucia.Engine;

/// <summary>Un modelo del catalogo: repositorio GGUF de Hugging Face, tamaño aproximado del cuantizado recomendado y memoria que pide.</summary>
public sealed record CatalogModel(string Name, string Repo, long ApproxBytes, string License, string Blurb)
{
    /// <summary>RAM (o VRAM) que hace falta con holgura: el fichero y el contexto.</summary>
    public long MinRamBytes => (long)(ApproxBytes * 1.6) + 1024L * 1024 * 1024;
}

/// <summary>
/// Catalogo corto y curado (todos con licencia abierta y GGUF publicados por el propio autor o
/// por unsloth): del mas capaz al mas pequeño. La aplicacion recomienda el mayor que quepa en la
/// memoria del equipo y esconde los que no.
/// </summary>
public static class ModelCatalog
{
    private const long MiB = 1024L * 1024;

    public static readonly CatalogModel[] All =
    [
        new("Qwen3.8 27B", "unsloth/Qwen3.8-27B-GGUF", 16314 * MiB, "Apache-2.0", "CatalogBig"),
        new("Gemma 4 12B", "unsloth/gemma-4-12b-it-GGUF", 7300 * MiB, "Apache-2.0", "CatalogMid"),
        new("Ministral 3 8B", "unsloth/Ministral-3-8B-Instruct-2512-GGUF", 4900 * MiB, "Apache-2.0", "CatalogMid"),
        new("Qwen3.5 4B", "unsloth/Qwen3.5-4B-GGUF", 2614 * MiB, "Apache-2.0", "CatalogSmall"),
        new("Phi-4 Mini", "unsloth/Phi-4-mini-instruct-GGUF", 2500 * MiB, "MIT", "CatalogSmall"),
        new("Qwen3.5 2B", "unsloth/Qwen3.5-2B-GGUF", 1400 * MiB, "Apache-2.0", "CatalogTiny"),
        new("Gemma 3 1B", "unsloth/gemma-3-1b-it-GGUF", 800 * MiB, "Gemma", "CatalogTiny"),
    ];

    /// <summary>Memoria total del equipo (RAM). Con GPU dedicada el motor reparte, pero la RAM manda para elegir.</summary>
    public static long TotalRamBytes()
    {
        var status = new MemoryStatusEx { dwLength = (uint)Marshal.SizeOf<MemoryStatusEx>() };
        return GlobalMemoryStatusEx(ref status) ? (long)status.ullTotalPhys : 8L * 1024 * MiB;
    }

    public static IEnumerable<CatalogModel> Fitting(long ramBytes) => All.Where(m => m.MinRamBytes <= ramBytes);

    /// <summary>
    /// Como ira cada IA en este PC. Con grafica: optima si cabe entera en su memoria (pesos, contexto
    /// y un margen); si sobresale poco, va; si sobresale mucho, lenta. Sin grafica manda el ancho de
    /// banda de la RAM: optimas las ligeras (hasta ~3 GB), lentas las que pasan de 6 GB.
    /// </summary>
    public static ModelFit FitOf(CatalogModel model, PcProfile pc)
    {
        if (model.MinRamBytes > pc.RamBytes)
            return ModelFit.TooBig;
        if (pc.HasUsableGpu)
        {
            var needed = (long)(model.ApproxBytes * 1.25) + 512 * MiB;
            if (needed <= pc.VramBytes) return ModelFit.Optimal;
            return model.ApproxBytes <= pc.VramBytes * 3 / 2 ? ModelFit.Ok : ModelFit.Slow;
        }
        if (model.ApproxBytes <= 3200 * MiB) return ModelFit.Optimal;
        return model.ApproxBytes <= 6144 * MiB ? ModelFit.Ok : ModelFit.Slow;
    }

    /// <summary>La mejor para este PC: la mayor de las optimas; si ninguna lo es, la mayor que cabe.</summary>
    public static CatalogModel? Recommended(PcProfile pc)
        => All.FirstOrDefault(m => FitOf(m, pc) == ModelFit.Optimal) ?? Fitting(pc.RamBytes).FirstOrDefault();

    /// <summary>Cuantizados en orden de preferencia: Q4_K_M es el equilibrio habitual entre tamaño y calidad.</summary>
    private static readonly string[] QuantPreference = ["Q4_K_M", "Q4_K_XL", "Q4_K_S", "IQ4_XS", "Q5_K_M", "Q4_0", "Q5_0", "Q6_K", "Q8_0"];

    public sealed record RepoFile(string Name, long? Size);

    /// <summary>Lista los GGUF del repositorio y elige el cuantizado preferido que sea un solo fichero.</summary>
    public static async Task<RepoFile> PickFileAsync(CatalogModel model, CancellationToken cancel)
    {
        using var response = await Downloader.Http.GetAsync($"https://huggingface.co/api/models/{model.Repo}/tree/main", cancel);
        response.EnsureSuccessStatusCode();
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancel));
        var files = new List<RepoFile>();
        foreach (var entry in doc.RootElement.EnumerateArray())
        {
            var path = entry.GetProperty("path").GetString() ?? string.Empty;
            if (!path.EndsWith(".gguf", StringComparison.OrdinalIgnoreCase) || path.Contains("-of-", StringComparison.OrdinalIgnoreCase) || path.Contains("mmproj", StringComparison.OrdinalIgnoreCase))
                continue;
            long? size = entry.TryGetProperty("size", out var s) && s.ValueKind == JsonValueKind.Number ? s.GetInt64() : null;
            if (entry.TryGetProperty("lfs", out var lfs) && lfs.TryGetProperty("size", out var ls) && ls.ValueKind == JsonValueKind.Number)
                size = ls.GetInt64();
            files.Add(new RepoFile(path, size));
        }
        foreach (var quant in QuantPreference)
        {
            var hit = files.FirstOrDefault(f => f.Name.Contains(quant, StringComparison.OrdinalIgnoreCase));
            if (hit is not null)
                return hit;
        }
        return files.OrderBy(f => f.Size ?? long.MaxValue).FirstOrDefault() ?? throw new FileNotFoundException("El repositorio no tiene ningún GGUF");
    }

    /// <summary>
    /// La parte de vision del modelo (el «mmproj»), si el repositorio la publica: con ella llama.cpp
    /// entiende imagenes. Se guarda al lado del modelo como <c>&lt;modelo&gt;.mmproj.gguf</c>.
    /// </summary>
    public static async Task<RepoFile?> PickMmprojAsync(string repo, CancellationToken cancel)
    {
        using var response = await Downloader.Http.GetAsync($"https://huggingface.co/api/models/{repo}/tree/main", cancel);
        if (!response.IsSuccessStatusCode)
            return null;
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancel));
        var files = new List<RepoFile>();
        foreach (var entry in doc.RootElement.EnumerateArray())
        {
            var path = entry.GetProperty("path").GetString() ?? string.Empty;
            if (!path.EndsWith(".gguf", StringComparison.OrdinalIgnoreCase) || !path.Contains("mmproj", StringComparison.OrdinalIgnoreCase))
                continue;
            long? size = entry.TryGetProperty("lfs", out var lfs) && lfs.TryGetProperty("size", out var ls) && ls.ValueKind == JsonValueKind.Number ? ls.GetInt64() : null;
            files.Add(new RepoFile(path, size));
        }
        return files.FirstOrDefault(f => f.Name.Contains("F16", StringComparison.OrdinalIgnoreCase))
            ?? files.FirstOrDefault(f => f.Name.Contains("BF16", StringComparison.OrdinalIgnoreCase))
            ?? files.OrderBy(f => f.Size ?? long.MaxValue).FirstOrDefault();
    }

    /// <summary>Ruta del mmproj que acompaña a un modelo (exista o no).</summary>
    public static string MmprojPathFor(string modelPath) => modelPath + ".mmproj.gguf";

    /// <summary>Descarga la parte de vision de un modelo instalado (si su repositorio la tiene). Devuelve la ruta, o null si no hay.</summary>
    public static async Task<string?> DownloadMmprojAsync(InstalledModel model, IProgress<Downloader.Progress> progress, CancellationToken cancel)
    {
        var repo = model.Repo ?? All.FirstOrDefault(m => m.Name == model.Name)?.Repo;
        if (repo is null)
            return null;
        var file = await PickMmprojAsync(repo, cancel);
        if (file is null)
            return null;
        var destination = MmprojPathFor(model.Path);
        if (File.Exists(destination) && (file.Size is null || new FileInfo(destination).Length == file.Size))
            return destination;
        await Downloader.DownloadAsync($"https://huggingface.co/{repo}/resolve/main/{file.Name}?download=true", destination, null, progress, cancel);
        return destination;
    }

    /// <summary>Descarga el fichero elegido a la carpeta de modelos y devuelve su ruta.</summary>
    public static async Task<string> DownloadAsync(CatalogModel model, RepoFile file, IProgress<Downloader.Progress> progress, CancellationToken cancel)
    {
        var destination = Path.Combine(Paths.Models, Path.GetFileName(file.Name));
        if (File.Exists(destination) && (file.Size is null || new FileInfo(destination).Length == file.Size))
            return destination;
        var url = $"https://huggingface.co/{model.Repo}/resolve/main/{file.Name}?download=true";
        await Downloader.DownloadAsync(url, destination, null, progress, cancel);
        return destination;
    }

    /// <summary>Los GGUF que hay en la carpeta de modelos, con el nombre de su ficha (o uno sacado del fichero).</summary>
    public static List<InstalledModel> InstalledFiles()
    {
        var settings = AppSettings.Current;
        var result = new List<InstalledModel>();
        if (!Directory.Exists(Paths.Models))
            return result;
        foreach (var file in Directory.GetFiles(Paths.Models, "*.gguf").OrderBy(f => f, StringComparer.OrdinalIgnoreCase))
        {
            var known = settings.Installed.FirstOrDefault(i => string.Equals(i.Path, file, StringComparison.OrdinalIgnoreCase));
            if (known is null)
            {
                // Sin ficha (copiado a mano): si el nombre del fichero es el de un repositorio del catalogo, se toma su ficha.
                var name = Path.GetFileName(file);
                var fromCatalog = All.FirstOrDefault(m => name.StartsWith(m.Repo[(m.Repo.IndexOf('/') + 1)..].Replace("-GGUF", string.Empty), StringComparison.OrdinalIgnoreCase));
                known = fromCatalog is null ? new InstalledModel { Name = NameFromFile(file), Path = file } : new InstalledModel { Name = fromCatalog.Name, Path = file, License = fromCatalog.License };
            }
            result.Add(known);
        }
        return result;
    }

    /// <summary>Borra un GGUF descargado. Si era el activo, se para el motor y la aplicacion se queda sin IA hasta elegir otra.</summary>
    public static void Delete(InstalledModel model)
    {
        var settings = AppSettings.Current;
        if (string.Equals(settings.ModelPath, model.Path, StringComparison.OrdinalIgnoreCase))
        {
            App.Engine.Stop();
            settings.ModelPath = null;
            settings.ModelName = null;
            settings.ModelLicense = null;
        }
        File.Delete(model.Path);
        settings.Forget(model.Path);
        settings.Save();
    }

    /// <summary>Nombre visible a partir de un fichero GGUF importado a mano: «Qwen3.5-4B-Q4_K_M.gguf» → «Qwen3.5 4B».</summary>
    public static string NameFromFile(string path)
    {
        var name = Path.GetFileNameWithoutExtension(path);
        foreach (var quant in QuantPreference.Concat(["F16", "BF16", "IQ", "Q3", "Q2"]))
        {
            var i = name.IndexOf("-" + quant, StringComparison.OrdinalIgnoreCase);
            if (i > 0) { name = name[..i]; break; }
            i = name.IndexOf("." + quant, StringComparison.OrdinalIgnoreCase);
            if (i > 0) { name = name[..i]; break; }
        }
        return name.Replace('-', ' ').Replace('_', ' ').Trim();
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MemoryStatusEx
    {
        public uint dwLength, dwMemoryLoad;
        public ulong ullTotalPhys, ullAvailPhys, ullTotalPageFile, ullAvailPageFile, ullTotalVirtual, ullAvailVirtual, ullAvailExtendedVirtual;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GlobalMemoryStatusEx(ref MemoryStatusEx buffer);
}
