using System.Text.Json;

namespace SocLucia.Engine;

/// <summary>
/// Buscador de modelos en Hugging Face. Con el filtro puesto, solo repositorios GGUF (los que
/// Lucia ejecuta); sin el, cualquier modelo (imagen, video, voz…) para curiosear, con su tipo y un
/// enlace. De los GGUF se mira la lista de ficheros para elegir el cuantizado habitual y saber lo
/// que pesa: con eso se dice si es optimo para este PC, igual que con el catalogo.
/// </summary>
public static class HuggingFace
{
    /// <summary>Un resultado. Runnable = GGUF de un fichero que Lucia puede bajar y ejecutar; si no, Kind dice que es.</summary>
    public sealed record Hit(CatalogModel Model, string FileName, long Downloads, int Likes, bool Gated, bool Runnable, string Kind)
    {
        public string Url => "https://huggingface.co/" + Model.Repo;
    }

    public static async Task<List<Hit>> SearchAsync(string query, PcProfile pc, bool onlyGguf, CancellationToken cancel)
    {
        // Palabras clave (imagenes, video, voz…) → tipo de modelo de Hugging Face; el resto de la frase sigue siendo texto de busqueda.
        var words = query.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
        var pipeline = words.Select(w => Kinds.TryGetValue(w, out var k) ? k : null).FirstOrDefault(k => k is not null);
        if (pipeline is not null) words.RemoveAll(w => Kinds.TryGetValue(w, out var k) && k == pipeline);
        var text = string.Join(' ', words);
        var url = "https://huggingface.co/api/models?sort=downloads&direction=-1&limit=30"
                  + (text.Length > 0 ? "&search=" + Uri.EscapeDataString(text) : string.Empty)
                  + (pipeline is not null ? "&pipeline_tag=" + pipeline : string.Empty)
                  + (onlyGguf ? "&filter=gguf" : string.Empty);
        using var response = await Downloader.Http.GetAsync(url, cancel);
        response.EnsureSuccessStatusCode();
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancel));
        var candidates = new List<(string Id, long Downloads, int Likes, string License, bool Gated, bool Gguf, string Kind)>();
        foreach (var m in doc.RootElement.EnumerateArray())
        {
            var id = m.TryGetProperty("id", out var idNode) ? idNode.GetString() ?? string.Empty : string.Empty;
            if (id.Length == 0) continue;
            var license = "?";
            var gguf = false;
            var library = string.Empty;
            if (m.TryGetProperty("tags", out var tags) && tags.ValueKind == JsonValueKind.Array)
                foreach (var t in tags.EnumerateArray())
                {
                    var tag = t.GetString() ?? string.Empty;
                    if (tag.StartsWith("license:", StringComparison.Ordinal)) license = tag[8..];
                    else if (tag == "gguf") gguf = true;
                    else if (tag is "diffusers" or "transformers" or "safetensors" or "onnx" or "mlx") library = library.Length == 0 ? tag : library;
                }
            var kind = m.TryGetProperty("pipeline_tag", out var p) && p.ValueKind == JsonValueKind.String ? p.GetString() ?? string.Empty : string.Empty;
            if (kind.Length == 0) kind = library;
            var gated = m.TryGetProperty("gated", out var g) && g.ValueKind is not (JsonValueKind.False or JsonValueKind.Null);
            candidates.Add((id,
                m.TryGetProperty("downloads", out var d) && d.ValueKind == JsonValueKind.Number ? d.GetInt64() : 0,
                m.TryGetProperty("likes", out var l) && l.ValueKind == JsonValueKind.Number ? l.GetInt32() : 0,
                license, gated, gguf, kind));
        }
        // De los GGUF, la lista de ficheros a la vez; el resto se enseña tal cual (no se ejecuta aqui).
        var tasks = candidates.Take(24).Select(async c =>
        {
            var model = new CatalogModel(NameFor(c.Id), c.Id, 0, c.License, "CatalogHf");
            if (!c.Gguf || !TextModel(c.Kind))
                return new Hit(model, string.Empty, c.Downloads, c.Likes, c.Gated, false, c.Kind.Length > 0 ? c.Kind : (c.Gguf ? "gguf" : string.Empty));
            try
            {
                var file = await ModelCatalog.PickFileAsync(model, cancel);
                if (file.Size is not { } size || size <= 0)
                    return new Hit(model, System.IO.Path.GetFileName(file.Name), c.Downloads, c.Likes, c.Gated, false, "gguf");
                return new Hit(model with { ApproxBytes = size }, System.IO.Path.GetFileName(file.Name), c.Downloads, c.Likes, c.Gated, true, "gguf");
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception)
            {
                return new Hit(model, string.Empty, c.Downloads, c.Likes, c.Gated, false, "gguf-sharded");
            }
        });
        var hits = (await Task.WhenAll(tasks)).ToList();
        // Primero las que Lucia ejecuta y van bien en este PC; luego las demas por descargas.
        return hits.OrderBy(h => h.Runnable ? (int)ModelCatalog.FitOf(h.Model, pc) : 9).ThenByDescending(h => h.Downloads).ToList();
    }

    /// <summary>Tipos de modelo por palabra clave, en español e ingles (los nombres de pipeline de Hugging Face).</summary>
    private static readonly Dictionary<string, string> Kinds = new(StringComparer.OrdinalIgnoreCase)
    {
        ["imagen"] = "text-to-image", ["imagenes"] = "text-to-image", ["imágenes"] = "text-to-image", ["image"] = "text-to-image", ["images"] = "text-to-image", ["dibujo"] = "text-to-image", ["dibujos"] = "text-to-image", ["foto"] = "text-to-image", ["fotos"] = "text-to-image",
        ["video"] = "text-to-video", ["vídeo"] = "text-to-video", ["videos"] = "text-to-video", ["vídeos"] = "text-to-video",
        ["voz"] = "text-to-speech", ["habla"] = "text-to-speech", ["tts"] = "text-to-speech", ["speech"] = "text-to-speech", ["locucion"] = "text-to-speech", ["locución"] = "text-to-speech",
        ["transcribir"] = "automatic-speech-recognition", ["transcripcion"] = "automatic-speech-recognition", ["transcripción"] = "automatic-speech-recognition", ["whisper"] = "automatic-speech-recognition", ["dictado"] = "automatic-speech-recognition",
        ["traducir"] = "translation", ["traduccion"] = "translation", ["traducción"] = "translation", ["translation"] = "translation",
        ["vision"] = "image-text-to-text", ["visión"] = "image-text-to-text", ["multimodal"] = "image-text-to-text",
        ["embeddings"] = "feature-extraction", ["embedding"] = "feature-extraction",
        ["musica"] = "text-to-audio", ["música"] = "text-to-audio", ["audio"] = "text-to-audio", ["sonido"] = "text-to-audio",
        ["texto"] = "text-generation", ["chat"] = "text-generation", ["llm"] = "text-generation",
    };

    /// <summary>Lo que llama-server ejecuta: modelos de texto (y de texto+imagen con mmproj).</summary>
    private static bool TextModel(string kind) => kind is "" or "text-generation" or "text2text-generation" or "image-text-to-text" or "conversational";

    /// <summary>«unsloth/Qwen3.5-4B-GGUF» → «Qwen3.5 4B (unsloth)».</summary>
    private static string NameFor(string repo)
    {
        var slash = repo.IndexOf('/');
        var owner = slash > 0 ? repo[..slash] : string.Empty;
        var name = slash > 0 ? repo[(slash + 1)..] : repo;
        foreach (var suffix in new[] { "-GGUF", "_GGUF", "-gguf", ".gguf" })
            if (name.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)) { name = name[..^suffix.Length]; break; }
        name = name.Replace('-', ' ').Replace('_', ' ').Trim();
        return owner.Length > 0 ? $"{name} ({owner})" : name;
    }
}
