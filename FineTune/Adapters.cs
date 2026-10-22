using System.IO;
using SocLucia.Services;

namespace SocLucia.FineTune;

/// <summary>
/// Un adaptador entrenado: lo que la IA ha aprendido con los ejemplos del usuario, en un GGUF
/// pequeño (decenas de MB) que llama-server pone encima de la IA original con <c>--lora</c>.
/// Solo sirve para la IA con la que se entreno.
/// </summary>
public sealed class LoraAdapter
{
    public string Name { get; set; } = string.Empty;
    public string Path { get; set; } = string.Empty;
    /// <summary>El GGUF de la IA sobre la que va (el de la carpeta de modelos).</summary>
    public string ModelPath { get; set; } = string.Empty;
    /// <summary>La IA original (safetensors) con la que se entreno.</summary>
    public string BaseRepo { get; set; } = string.Empty;
    public int Examples { get; set; }
    public string Preset { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.Now;
    /// <summary>En uso: cuando esta IA esta activa, responde con lo aprendido.</summary>
    public bool Active { get; set; }
}

/// <summary>Los adaptadores guardados (<c>finetune\adapters\</c>) y cual esta en uso con cada IA.</summary>
public static class Adapters
{
    public static string Folder => System.IO.Path.Combine(PythonEnv.Folder, "adapters");

    /// <summary>El adaptador en uso con la IA de esa ruta, si lo hay y el fichero sigue ahi.</summary>
    public static LoraAdapter? ActiveFor(AppSettings settings, string? modelPath)
        => modelPath is null ? null : settings.Adapters.FirstOrDefault(a => a.Active && string.Equals(a.ModelPath, modelPath, StringComparison.OrdinalIgnoreCase) && File.Exists(a.Path));

    public static IEnumerable<LoraAdapter> For(AppSettings settings, string? modelPath)
        => settings.Adapters.Where(a => string.Equals(a.ModelPath, modelPath, StringComparison.OrdinalIgnoreCase));

    /// <summary>Pone en uso un adaptador (o ninguno) para su IA: solo uno a la vez por IA.</summary>
    public static void SetActive(AppSettings settings, LoraAdapter? adapter, string modelPath)
    {
        foreach (var a in For(settings, modelPath))
            a.Active = adapter is not null && ReferenceEquals(a, adapter);
        settings.Save();
    }

    public static void Add(AppSettings settings, LoraAdapter adapter, bool activate)
    {
        settings.Adapters.RemoveAll(a => string.Equals(a.Path, adapter.Path, StringComparison.OrdinalIgnoreCase));
        settings.Adapters.Add(adapter);
        if (activate) SetActive(settings, adapter, adapter.ModelPath);
        else settings.Save();
    }

    public static void Delete(AppSettings settings, LoraAdapter adapter)
    {
        settings.Adapters.Remove(adapter);
        settings.Save();
        try { if (File.Exists(adapter.Path)) File.Delete(adapter.Path); } catch (Exception) { }
    }

    /// <summary>Nombre de fichero a partir del nombre que le da el usuario.</summary>
    public static string FileNameFor(string name) => $"{SafeName(name)}-{DateTime.Now:yyyyMMdd-HHmmss}.lora.gguf";

    public static string SafeName(string name)
    {
        var safe = new string(name.Trim().Select(c => System.IO.Path.GetInvalidFileNameChars().Contains(c) || char.IsWhiteSpace(c) ? '-' : c).ToArray());
        safe = System.Text.RegularExpressions.Regex.Replace(safe, "-{2,}", "-").Trim('-', '.');
        return safe.Length == 0 ? "lucia" : safe.Length > 60 ? safe[..60] : safe;
    }
}
