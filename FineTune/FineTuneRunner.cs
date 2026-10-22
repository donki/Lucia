using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using SocLucia.Engine;
using SocLucia.Services;

namespace SocLucia.FineTune;

/// <summary>Lo que cuenta el script mientras trabaja (las lineas «@lucia {json}»).</summary>
public sealed record TrainEvent(string Kind, string? Stage = null, int Step = 0, int Total = 0, double? Loss = null, double Epoch = 0, int Elapsed = 0, string? Path = null, string? Message = null, string? ErrorKind = null, int Examples = 0);

/// <summary>
/// Lanza el entrenamiento y la conversion de modelos con el Python del entorno. La grafica no da
/// para la IA del chat y el entrenamiento a la vez: antes de empezar se para llama-server (la
/// siguiente pregunta lo vuelve a arrancar). Parar es dejar un fichero «stop»: el script acaba el
/// paso en curso y sale sin guardar.
/// </summary>
public static class FineTuneRunner
{
    public static string WorkFolder => Path.Combine(PythonEnv.Folder, "work");

    /// <summary>Una linea de la salida del script, o null si no es de las suyas.</summary>
    public static TrainEvent? Parse(string line)
    {
        if (!line.StartsWith("@lucia ", StringComparison.Ordinal)) return null;
        try
        {
            if (JsonNode.Parse(line[7..]) is not JsonObject o) return null;
            string? S(string k) => o[k] is JsonValue v && v.TryGetValue<string>(out var s) ? s : null;
            double D(string k) => o[k] is JsonValue v && v.TryGetValue<double>(out var d) ? d : 0;
            return new TrainEvent(S("event") ?? string.Empty, S("stage"), (int)D("step"), (int)D("total"), o["loss"] is null ? null : D("loss"),
                D("epoch"), (int)D("elapsed"), S("adapter") ?? S("outfile"), S("message"), S("kind"), (int)D("examples"));
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>Segundos que faltan, por el ritmo de los pasos hechos (null hasta tener dos pasos).</summary>
    public static int? Remaining(TrainEvent e) => e.Step >= 2 && e.Total > e.Step && e.Elapsed > 0 ? (int)Math.Ceiling((double)e.Elapsed / e.Step * (e.Total - e.Step)) : null;

    /// <summary>
    /// Entrena un adaptador LoRA y devuelve la ruta del GGUF resultante (en <c>finetune\adapters\</c>),
    /// o null si se paro. Los ejemplos ya revisados van en <paramref name="dataset"/> (JSONL).
    /// </summary>
    public static async Task<string?> TrainAsync(string baseFolder, string dataset, int examples, TrainingPreset preset, string name, IProgress<TrainEvent> events, CancellationToken cancel)
    {
        var plan = preset.Plan(examples);
        PythonEnv.WriteScript();
        var output = Path.Combine(WorkFolder, DateTime.Now.ToString("yyyyMMdd-HHmmss"));
        Directory.CreateDirectory(output);
        var stop = Path.Combine(output, "stop");
        var config = new JsonObject
        {
            ["base_dir"] = baseFolder,
            ["dataset"] = dataset,
            ["output_dir"] = output,
            ["epochs"] = plan.Epochs,
            ["learning_rate"] = preset.LearningRate,
            ["rank"] = preset.Rank,
            ["alpha"] = preset.Alpha,
            ["max_len"] = preset.MaxTokens,
            ["batch"] = 1,
            ["grad_accum"] = plan.Accumulation,
            ["use_4bit"] = true,
            ["stop_file"] = stop,
            ["llamacpp_dir"] = PythonEnv.LlamaCppFolder,
        };
        var result = await RunScriptAsync(config, output, events, stop, cancel);
        if (result is null) return null;
        var adapters = Adapters.Folder;
        Directory.CreateDirectory(adapters);
        var target = Path.Combine(adapters, Adapters.FileNameFor(name));
        File.Move(result, target, overwrite: true);
        try { Directory.Delete(output, recursive: true); } catch (Exception) { }
        return target;
    }

    /// <summary>
    /// Un modelo de Hugging Face en safetensors a GGUF que Lucia ejecuta: se convierte a F16 con
    /// convert_hf_to_gguf.py y se cuantiza a Q4_K_M con el llama-quantize del motor. Devuelve la ruta
    /// del GGUF en la carpeta de modelos.
    /// </summary>
    public static async Task<string> ConvertAsync(string modelFolder, string displayName, IProgress<TrainEvent> events, CancellationToken cancel)
    {
        PythonEnv.WriteScript();
        Directory.CreateDirectory(WorkFolder);
        var f16 = Path.Combine(WorkFolder, Adapters.SafeName(displayName) + "-F16.gguf");
        var config = new JsonObject
        {
            ["mode"] = "convert",
            ["base_dir"] = modelFolder,
            ["outfile"] = f16,
            ["outtype"] = "f16",
            ["llamacpp_dir"] = PythonEnv.LlamaCppFolder,
        };
        try
        {
            await RunScriptAsync(config, WorkFolder, events, null, cancel);
            events.Report(new TrainEvent("stage", "quantizing"));
            var quantize = Path.Combine(await App.Engine.EnsureBinariesForToolsAsync(cancel), "llama-quantize.exe");
            var target = Path.Combine(Paths.Models, Adapters.SafeName(displayName) + "-Q4_K_M.gguf");
            var info = new ProcessStartInfo(quantize) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
            foreach (var a in new[] { f16, target, "Q4_K_M" }) info.ArgumentList.Add(a);
            var (code, tail) = await PythonEnv.RunAsync(info, null, cancel);
            if (code != 0 || !File.Exists(target))
                throw new InvalidOperationException(Localization.Loc.Format("FtConvertFailed", tail));
            return target;
        }
        finally
        {
            try { if (File.Exists(f16)) File.Delete(f16); } catch (Exception) { }
        }
    }

    private static async Task<string?> RunScriptAsync(JsonObject config, string folder, IProgress<TrainEvent> events, string? stop, CancellationToken cancel)
    {
        var configPath = Path.Combine(folder, "config.json");
        File.WriteAllText(configPath, config.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
        App.Engine.Stop();   // la grafica, para el entrenamiento
        var info = new ProcessStartInfo(PythonEnv.PythonExe) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true, WorkingDirectory = folder };
        info.ArgumentList.Add(PythonEnv.ScriptPath);
        info.ArgumentList.Add(configPath);
        PythonEnv.Isolate(info);
        info.Environment["PYTORCH_CUDA_ALLOC_CONF"] = "expandable_segments:True";
        PythonEnv.Log("entrenamiento/conversion: " + config.ToJsonString());
        TrainEvent? last = null;
        var stopped = false;
        // Parar: primero con el fichero (el script sale ordenado); si en 20 s no ha salido, se mata.
        using var graceful = new CancellationTokenSource();
        using var link = cancel.Register(() =>
        {
            if (stop is not null) { try { File.WriteAllText(stop, "stop"); } catch (Exception) { } graceful.CancelAfter(TimeSpan.FromSeconds(20)); }
            else graceful.Cancel();
        });
        var (code, tail) = await PythonEnv.RunAsync(info, line =>
        {
            if (Parse(line) is not { } e) return;
            if (e.Kind == "stopped") stopped = true;
            last = e;
            events.Report(e);
        }, graceful.Token);
        if (stopped || cancel.IsCancellationRequested)
        {
            cancel.ThrowIfCancellationRequested();
            return null;
        }
        if (last is { Kind: "error" } error)
            throw new InvalidOperationException(error.ErrorKind == "oom" ? Localization.Loc.Get("FtOutOfMemory") : Localization.Loc.Format("FtFailed", error.Message ?? tail));
        if (code != 0 || last is not { Kind: "done", Path: { } path })
            throw new InvalidOperationException(Localization.Loc.Format("FtFailed", tail));
        return path;
    }
}
