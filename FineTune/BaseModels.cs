using System.IO;
using System.Text.Json;
using SocLucia.Engine;

namespace SocLucia.FineTune;

/// <summary>La IA original (safetensors de Hugging Face) de la que sale un GGUF: es la que se entrena.</summary>
public sealed record BaseModel(string Repo, long Parameters, bool Gated, string License, long Bytes, IReadOnlyList<string> Files)
{
    public double Billions => Parameters / 1e9;
}

/// <summary>Si se puede entrenar esta IA en este PC.</summary>
public enum TrainFit { Ok, Tight, TooBig, CpuOnly }

/// <summary>
/// Lo que se le pide al entrenamiento: pasadas por los ejemplos (como minimo <see cref="Epochs"/> y las
/// que hagan falta para llegar a <see cref="MinSteps"/> pasos, hasta <see cref="MaxEpochs"/>), cuanto se
/// aprende en cada paso y el tamaño del adaptador.
/// </summary>
/// <remarks>
/// Medido el 2026-10-22 con Qwen3.5 0,8B y 12 ejemplos de datos: con 6 pasos aprende el tono pero se
/// inventa los datos; con 96 pasos los repite al pie de la letra. Con pocos ejemplos hacen falta muchas
/// pasadas; con muchos, pocas.
/// </remarks>
public sealed record TrainingPreset(string Key, int Epochs, int MinSteps, int MaxEpochs, double LearningRate, int Rank, int MaxTokens)
{
    public int Alpha => Rank * 2;

    /// <summary>Ejemplos por paso: de uno en uno con pocos ejemplos (mas pasos), de cuatro en cuatro con muchos.</summary>
    public static int Accumulation(int examples) => examples < 100 ? 1 : 4;

    /// <summary>Pasadas, ejemplos por paso y pasos para tantos ejemplos.</summary>
    public (int Epochs, int Accumulation, int Steps) Plan(int examples)
    {
        var accumulation = Accumulation(examples);
        var perEpoch = Math.Max(1, (int)Math.Ceiling(examples / (double)accumulation));
        var epochs = Math.Clamp((int)Math.Ceiling(MinSteps / (double)perEpoch), Epochs, MaxEpochs);
        return (epochs, accumulation, perEpoch * epochs);
    }
}

/// <summary>
/// De que IA original sale cada GGUF, cuanto pesa, si cabe para entrenar y su descarga a
/// <c>finetune\bases\</c>. Los GGUF publicados en Hugging Face dicen su origen en la ficha
/// (<c>base_model</c>); con eso se baja el modelo en safetensors, que es lo que entrena PyTorch.
/// </summary>
public static class BaseModels
{
    public static string Folder => Path.Combine(PythonEnv.Folder, "bases");

    public static readonly TrainingPreset Quick = new("FtPresetQuick", 2, 40, 10, 2e-4, 8, 512);
    public static readonly TrainingPreset Balanced = new("FtPresetBalanced", 3, 100, 15, 2e-4, 16, 1024);
    public static readonly TrainingPreset Deep = new("FtPresetDeep", 5, 200, 25, 1.5e-4, 32, 1024);
    public static readonly TrainingPreset[] Presets = [Quick, Balanced, Deep];

    /// <summary>
    /// El repositorio original de un repositorio de GGUF (su ficha: <c>cardData.base_model</c>, texto o
    /// lista). Si la ficha no lo dice, el propio repositorio cuando no es de GGUF; si no, null.
    /// </summary>
    public static string? BaseRepoFrom(string modelInfoJson, string repo)
    {
        using var doc = JsonDocument.Parse(modelInfoJson);
        var root = doc.RootElement;
        if (root.TryGetProperty("cardData", out var card) && card.ValueKind == JsonValueKind.Object && card.TryGetProperty("base_model", out var b))
        {
            var value = b.ValueKind switch
            {
                JsonValueKind.String => b.GetString(),
                JsonValueKind.Array => b.EnumerateArray().Select(x => x.GetString()).FirstOrDefault(x => !string.IsNullOrEmpty(x)),
                _ => null,
            };
            if (!string.IsNullOrEmpty(value) && value.Contains('/')) return value;
        }
        var gguf = root.TryGetProperty("tags", out var tags) && tags.ValueKind == JsonValueKind.Array && tags.EnumerateArray().Any(t => t.GetString() == "gguf");
        return gguf || repo.EndsWith("-GGUF", StringComparison.OrdinalIgnoreCase) ? null : repo;
    }

    /// <summary>La ficha de un repositorio de safetensors: parametros, si esta cerrado y su licencia.</summary>
    public static BaseModel ParseBase(string repo, string modelInfoJson, string treeJson)
    {
        using var info = JsonDocument.Parse(modelInfoJson);
        var root = info.RootElement;
        long parameters = root.TryGetProperty("safetensors", out var st) && st.ValueKind == JsonValueKind.Object && st.TryGetProperty("total", out var total) && total.ValueKind == JsonValueKind.Number ? total.GetInt64() : 0;
        var gated = root.TryGetProperty("gated", out var g) && g.ValueKind is not (JsonValueKind.False or JsonValueKind.Null or JsonValueKind.Undefined);
        var license = root.TryGetProperty("cardData", out var card) && card.ValueKind == JsonValueKind.Object && card.TryGetProperty("license", out var l) && l.ValueKind == JsonValueKind.String ? l.GetString() ?? "?" : "?";
        var files = new List<string>();
        long bytes = 0;
        using var tree = JsonDocument.Parse(treeJson);
        foreach (var entry in tree.RootElement.EnumerateArray())
        {
            if (entry.TryGetProperty("type", out var type) && type.GetString() != "file") continue;
            var path = entry.GetProperty("path").GetString() ?? string.Empty;
            if (!Needed(path)) continue;
            files.Add(path);
            if (entry.TryGetProperty("lfs", out var lfs) && lfs.TryGetProperty("size", out var ls) && ls.ValueKind == JsonValueKind.Number) bytes += ls.GetInt64();
            else if (entry.TryGetProperty("size", out var s) && s.ValueKind == JsonValueKind.Number) bytes += s.GetInt64();
        }
        if (parameters == 0) parameters = bytes / 2;   // sin el dato, los pesos en 16 bits: 2 bytes por parametro
        return new BaseModel(repo, parameters, gated, license, bytes, files);
    }

    /// <summary>Los ficheros que hacen falta para entrenar y convertir: pesos, configuracion y tokenizador (no los .bin viejos ni los GGUF/ONNX).</summary>
    public static bool Needed(string path)
    {
        if (path.Contains('/')) return false;   // subcarpetas (onnx/, original/…): no
        var name = path.ToLowerInvariant();
        return name.EndsWith(".safetensors") || name.EndsWith(".json") || name.EndsWith(".jinja") || name.EndsWith(".model")
               || name.EndsWith(".tiktoken") || name == "merges.txt" || name == "vocab.txt" || name.EndsWith(".py");
    }

    /// <summary>Del GGUF instalado (o de un repositorio cualquiera) a la ficha de su IA original.</summary>
    public static async Task<BaseModel?> ResolveAsync(string repo, CancellationToken cancel)
    {
        var info = await GetAsync($"https://huggingface.co/api/models/{repo}", cancel);
        if (info is null) return null;
        var baseRepo = BaseRepoFrom(info, repo);
        if (baseRepo is null) return null;
        var baseInfo = baseRepo == repo ? info : await GetAsync($"https://huggingface.co/api/models/{baseRepo}", cancel);
        var tree = await GetAsync($"https://huggingface.co/api/models/{baseRepo}/tree/main", cancel);
        if (baseInfo is null || tree is null) return null;
        var model = ParseBase(baseRepo, baseInfo, tree);
        return model.Files.Any(f => f.EndsWith(".safetensors", StringComparison.OrdinalIgnoreCase)) ? model : null;
    }

    private static async Task<string?> GetAsync(string url, CancellationToken cancel)
    {
        using var response = await Downloader.Http.GetAsync(url, cancel);
        return response.IsSuccessStatusCode ? await response.Content.ReadAsStringAsync(cancel) : null;
    }

    /// <summary>
    /// Memoria de grafica para entrenar en 4 bits (QLoRA): ~0,6 bytes por parametro con la
    /// cuantizacion y sus escalas, mas ~1,2 GB de activaciones, adaptador y optimizador con 1024 tokens.
    /// </summary>
    public static long VramNeeded(long parameters) => (long)(parameters * 0.6) + 1200L * 1024 * 1024;

    public static TrainFit FitOf(BaseModel model, PcProfile pc, bool cuda)
    {
        if (!cuda || !pc.HasUsableGpu)
            return model.Parameters <= 2_500_000_000 ? TrainFit.CpuOnly : TrainFit.TooBig;
        var need = VramNeeded(model.Parameters);
        if (need <= pc.VramBytes * 0.9) return TrainFit.Ok;
        return need <= pc.VramBytes * 1.15 ? TrainFit.Tight : TrainFit.TooBig;
    }

    /// <summary>Carpeta local de una IA original («Qwen/Qwen3.5-4B» → <c>bases\Qwen--Qwen3.5-4B</c>).</summary>
    public static string LocalFolder(string repo) => Path.Combine(Folder, string.Concat(repo.Replace("/", "--").Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c)));

    public static bool IsDownloaded(string repo) => File.Exists(Path.Combine(LocalFolder(repo), ".completa"));

    /// <summary>Baja los ficheros de la IA original (con progreso total y retomando lo que quedo a medias).</summary>
    public static async Task<string> DownloadAsync(BaseModel model, IProgress<Downloader.Progress>? progress, CancellationToken cancel)
    {
        var folder = LocalFolder(model.Repo);
        if (IsDownloaded(model.Repo)) return folder;
        Directory.CreateDirectory(folder);
        long done = 0;
        foreach (var file in model.Files)
        {
            var target = Path.Combine(folder, file);
            if (!File.Exists(target))
            {
                var before = done;
                var each = new Progress<Downloader.Progress>(p => progress?.Report(new Downloader.Progress(before + p.Received, model.Bytes)));
                await Downloader.DownloadAsync($"https://huggingface.co/{model.Repo}/resolve/main/{file}?download=true", target, null, each, cancel);
            }
            done += new FileInfo(target).Length;
            progress?.Report(new Downloader.Progress(done, model.Bytes));
        }
        File.WriteAllText(Path.Combine(folder, ".completa"), DateTime.Now.ToString("s"));
        return folder;
    }

    public static void Delete(string repo)
    {
        var folder = LocalFolder(repo);
        if (Directory.Exists(folder)) Directory.Delete(folder, recursive: true);
    }
}
