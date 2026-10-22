using System.Text.RegularExpressions;
using SocLucia.Localization;

namespace SocLucia.Engine;

/// <summary>El motor no ha podido cargar la IA; el mensaje ya viene explicado para el usuario.</summary>
public sealed class EngineLoadException(string message, bool modelProblem) : InvalidOperationException(message)
{
    /// <summary>El fallo es del fichero (arquitectura, tensores, datos que faltan, roto): otro acelerador no lo arregla.</summary>
    public bool ModelProblem { get; } = modelProblem;
}

/// <summary>
/// Por que no arranca llama-server, en claro. Antes solo se decia «el motor ha terminado con el codigo 1,
/// mira el registro»; el motivo estaba en las lineas « E » de su salida (arquitectura desconocida, tensores
/// que no encajan, sin memoria, fichero roto…) y nadie lo veia.
/// </summary>
public static class EngineErrors
{
    /// <summary>Las lineas de error de llama.cpp («0.00.936.778 E llama_model_load: …»).</summary>
    public static bool IsErrorLine(string line) => Regex.IsMatch(line, @"^\s*(\d+\.\d+\.\d+\.\d+\s+)?E\s") || line.Contains("error loading model", StringComparison.OrdinalIgnoreCase);

    /// <summary>Si el motivo es del fichero de la IA (no de la grafica ni de la memoria).</summary>
    public static bool IsModelProblem(IReadOnlyList<string> errors)
        => Regex.IsMatch(string.Join("\n", errors), @"unknown model architecture|has wrong shape|missing tensor|wrong number of tensors|key not found in model|invalid magic|not a gguf|not within the file bounds|corrupted");

    public static string Explain(IReadOnlyList<string> errors, int exitCode, string modelName)
    {
        var all = string.Join("\n", errors);
        var arch = Regex.Match(all, @"unknown model architecture: '([^']*)'");
        if (arch.Success)
            return arch.Groups[1].Value.Length == 0
                ? Loc.Format("EngineErrNoArch", modelName)
                : Loc.Format("EngineErrArch", modelName, arch.Groups[1].Value, EnginePin.Build);
        var shape = Regex.Match(all, @"tensor '([^']+)' has wrong shape|missing tensor '([^']+)'|wrong number of tensors");
        if (shape.Success)
            return Loc.Format("EngineErrTensors", modelName, Clean(shape.Value));
        var key = Regex.Match(all, @"key not found in model: ([\w.]+)");
        if (key.Success)
            return Loc.Format(LooksLikeAdapter(modelName) ? "EngineErrAdapter" : "EngineErrMissingKey", modelName, key.Groups[1].Value);
        if (Regex.IsMatch(all, @"out of memory|failed to allocate|ErrorOutOfDeviceMemory|cudaMalloc failed|unable to allocate|not enough memory", RegexOptions.IgnoreCase))
            return Loc.Format("EngineErrMemory", modelName);
        if (Regex.IsMatch(all, @"invalid magic|failed to read|not a gguf|tensor data is not within|unexpectedly reached end|file is truncated|corrupted", RegexOptions.IgnoreCase))
            return Loc.Format("EngineErrCorrupt", modelName);
        // Lo que no se reconoce: la primera linea que de verdad dice algo (sin el reloj ni el prefijo).
        var first = errors.Select(Clean).FirstOrDefault(l => l.Length > 0 && !l.StartsWith("failed to load model", StringComparison.OrdinalIgnoreCase));
        return first is null ? Loc.Format("EngineExited", exitCode) : Loc.Format("EngineErrOther", modelName, first);
    }

    /// <summary>El nombre de un adaptador LoRA (lo que le falta a un adaptador son justo los datos del modelo).</summary>
    internal static bool LooksLikeAdapter(string name) => Regex.IsMatch(name, @"(^|[-_. ])(q?lora|adapter)([-_. ]|\.gguf$|$)", RegexOptions.IgnoreCase);

    /// <summary>Sin el reloj, la «E» ni el nombre de la funcion de llama.cpp.</summary>
    internal static string Clean(string line)
    {
        var l = Regex.Replace(line, @"^\s*(\d+\.\d+\.\d+\.\d+\s+)?E\s+", string.Empty);
        l = Regex.Replace(l, @"^(\w+\s+)?\w+:\s+(error loading model:\s+)?", string.Empty);
        l = Regex.Replace(l, @"\s{2,}", " ").Trim();
        return l.Length > 300 ? l[..300] + "…" : l;
    }
}
