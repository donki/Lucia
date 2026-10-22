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
    /// <summary>
    /// Un resultado. Runnable = GGUF de un fichero que Lucia puede bajar y ejecutar; si no, Kind dice
    /// que es («convertible» = modelo de texto en safetensors que Lucia puede pasar a GGUF).
    /// Parameters, si Hugging Face lo sabe (0 si no).
    /// </summary>
    public sealed record Hit(CatalogModel Model, string FileName, long Downloads, int Likes, bool Gated, bool Runnable, string Kind, long Parameters = 0)
    {
        public string Url => "https://huggingface.co/" + Model.Repo;
        public bool Convertible => Kind == "convertible";
    }

    /// <summary>Un repositorio de la lista de resultados, antes de mirar sus ficheros.</summary>
    public sealed record Candidate(string Id, long Downloads, int Likes, string License, bool Gated, bool Gguf, string Kind, long Parameters = 0, bool Safetensors = false, bool Prequantized = false);

    /// <summary>
    /// Marcas de pesos ya cuantizados en otro formato (MLX, GPTQ, AWQ, FP8/NVFP4, bitsandbytes…): los
    /// convertidores de llama.cpp esperan los pesos originales, asi que esos no se ofrecen para convertir.
    /// </summary>
    private static readonly string[] PrequantizedMarks = ["mlx", "gptq", "awq", "exl2", "exl3", "fp8", "nvfp4", "mxfp4", "int4", "int8", "bnb", "4bit", "8bit", "4-bit", "8-bit", "bitsandbytes", "compressed-tensors", "quantized"];

    public static bool LooksPrequantized(string id, IEnumerable<string> tags)
        => tags.Concat([id]).Any(t => PrequantizedMarks.Any(m => t.Contains(m, StringComparison.OrdinalIgnoreCase)));

    /// <summary>Que formatos enseñar: solo GGUF, tambien los que se pueden convertir, o todo.</summary>
    public enum Formats { Gguf, Convertible, All }

    /// <summary>Filtros del buscador. Kind es el pipeline de Hugging Face (null = cualquiera).</summary>
    public sealed record Filters(string? Kind = null, double? MaxBillions = null, Formats Format = Formats.Gguf, bool CommercialOnly = false, string Sort = "downloads");

    /// <summary>Licencias que permiten uso comercial (con o sin condiciones de uso aceptable).</summary>
    public static readonly HashSet<string> CommercialLicenses = new(StringComparer.OrdinalIgnoreCase)
    {
        "apache-2.0", "mit", "bsd-2-clause", "bsd-3-clause", "bsd", "cc-by-4.0", "cc-by-sa-4.0", "cc0-1.0", "unlicense", "isc", "zlib", "mpl-2.0",
        "openrail", "openrail++", "creativeml-openrail-m", "bigscience-openrail-m", "bigcode-openrail-m",
        "llama2", "llama3", "llama3.1", "llama3.2", "llama3.3", "llama4", "gemma",
    };

    public static Task<List<Hit>> SearchAsync(string query, PcProfile pc, bool onlyGguf, CancellationToken cancel)
        => SearchAsync(query, pc, new Filters(Format: onlyGguf ? Formats.Gguf : Formats.All), cancel);

    public static async Task<List<Hit>> SearchAsync(string query, PcProfile pc, Filters filters, CancellationToken cancel)
    {
        using var response = await Downloader.Http.GetAsync(SearchUrl(query, filters), cancel);
        response.EnsureSuccessStatusCode();
        var candidates = Filter(Candidates(await response.Content.ReadAsStringAsync(cancel)), filters);
        // De los GGUF, la lista de ficheros a la vez; el resto se enseña tal cual (no se ejecuta aqui).
        var tasks = candidates.Take(24).Select(async c =>
        {
            var model = new CatalogModel(NameFor(c.Id), c.Id, 0, c.License, "CatalogHf");
            if (!c.Gguf || !TextModel(c.Kind))
            {
                var convertible = IsConvertible(c);
                var kind = convertible ? "convertible" : c.Kind.Length > 0 ? c.Kind : (c.Gguf ? "gguf" : string.Empty);
                // De un convertible, lo que ocupara en GGUF Q4_K_M (~0,6 bytes por parametro), para valorar si cabe.
                return new Hit(model with { ApproxBytes = convertible ? (long)(c.Parameters * 0.6) : 0 }, string.Empty, c.Downloads, c.Likes, c.Gated, false, kind, c.Parameters);
            }
            try
            {
                var file = await ModelCatalog.PickFileAsync(model, cancel);
                if (file.Size is not { } size || size <= 0)
                    return new Hit(model, System.IO.Path.GetFileName(file.Name), c.Downloads, c.Likes, c.Gated, false, "gguf", c.Parameters);
                return new Hit(model with { ApproxBytes = size }, System.IO.Path.GetFileName(file.Name), c.Downloads, c.Likes, c.Gated, true, "gguf", c.Parameters);
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception)
            {
                return new Hit(model, string.Empty, c.Downloads, c.Likes, c.Gated, false, "gguf-sharded", c.Parameters);
            }
        });
        return Rank(await Task.WhenAll(tasks), pc);
    }

    /// <summary>
    /// Un modelo de texto en safetensors (sin GGUF), abierto y del que se sabe el tamaño: Lucia lo puede
    /// convertir a GGUF con los convertidores de llama.cpp (si su arquitectura esta soportada).
    /// </summary>
    public static bool IsConvertible(Candidate c) => !c.Gguf && c.Safetensors && !c.Prequantized && !c.Gated && c.Parameters > 0 && c.Kind.Length > 0 && TextModel(c.Kind);

    /// <summary>Lo que la API no filtra por si sola: formato y licencia comercial.</summary>
    public static List<Candidate> Filter(IEnumerable<Candidate> candidates, Filters filters)
        => candidates.Where(c => (!filters.CommercialOnly || CommercialLicenses.Contains(c.License))
                                 && filters.Format switch
                                 {
                                     Formats.Gguf => true,   // ya lo filtra la API (filter=gguf)
                                     Formats.Convertible => (c.Gguf && TextModel(c.Kind)) || IsConvertible(c),
                                     _ => true,
                                 }).ToList();

    /// <summary>Primero las que Lucia ejecuta y van bien en este PC; luego las demas por descargas.</summary>
    public static List<Hit> Rank(IEnumerable<Hit> hits, PcProfile pc)
        => hits.OrderBy(h => h.Runnable ? (int)ModelCatalog.FitOf(h.Model, pc) : h.Convertible ? 10 + (int)ModelCatalog.FitOf(h.Model, pc) : 20).ThenByDescending(h => h.Downloads).ToList();

    /// <summary>La direccion de busqueda en la API de Hugging Face (sin filtros: solo GGUF o todo).</summary>
    public static string SearchUrl(string query, bool onlyGguf) => SearchUrl(query, new Filters(Format: onlyGguf ? Formats.Gguf : Formats.All), expand: false);

    /// <summary>Ordenes que admite la API (descargas, me gusta, tendencia, recientes).</summary>
    public static readonly string[] Sorts = ["downloads", "likes", "trendingScore", "lastModified"];

    /// <summary>
    /// La direccion de busqueda con filtros. Las palabras clave de la frase (imagenes, voz…) se
    /// convierten en tipo si el filtro de tipo no dice otro; el maximo de parametros va a la API
    /// (<c>num_parameters=min:0,max:3B</c>) y se piden los campos de tamaño (safetensors y gguf).
    /// </summary>
    public static string SearchUrl(string query, Filters filters, bool expand = true)
    {
        // Palabras clave (imagenes, video, voz…) → tipo de modelo de Hugging Face; el resto de la frase sigue siendo texto de busqueda.
        var words = query.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
        var pipeline = words.Select(w => Kinds.TryGetValue(w, out var k) ? k : null).FirstOrDefault(k => k is not null);
        if (pipeline is not null) words.RemoveAll(w => Kinds.TryGetValue(w, out var k) && k == pipeline);
        pipeline = filters.Kind ?? pipeline;
        var text = string.Join(' ', words);
        var sort = Sorts.Contains(filters.Sort) ? filters.Sort : "downloads";
        // Lo que se filtra aqui (licencia, convertibles) se lleva parte de la lista: se piden mas.
        var limit = filters.CommercialOnly || filters.Format == Formats.Convertible ? 60 : 30;
        var url = $"https://huggingface.co/api/models?sort={sort}&direction=-1&limit={limit}"
               + (text.Length > 0 ? "&search=" + Uri.EscapeDataString(text) : string.Empty)
               + (pipeline is not null ? "&pipeline_tag=" + pipeline : string.Empty)
               + (filters.Format == Formats.Gguf ? "&filter=gguf" : string.Empty)
               + (filters.MaxBillions is { } max ? "&num_parameters=min:0,max:" + Billions(max) : string.Empty);
        if (expand)
            foreach (var field in new[] { "downloads", "likes", "tags", "pipeline_tag", "gated", "safetensors", "gguf" })
                url += "&expand[]=" + field;
        return url;
    }

    /// <summary>3 → «3B»; 0,5 → «500M» (el formato de num_parameters de la API).</summary>
    public static string Billions(double value) => value >= 1
        ? value.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture) + "B"
        : (value * 1000).ToString("0", System.Globalization.CultureInfo.InvariantCulture) + "M";

    /// <summary>Los repositorios de la respuesta de la API: id, descargas, licencia, si es GGUF y su tipo.</summary>
    public static List<Candidate> Candidates(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var candidates = new List<Candidate>();
        foreach (var m in doc.RootElement.EnumerateArray())
        {
            var id = m.TryGetProperty("id", out var idNode) ? idNode.GetString() ?? string.Empty : string.Empty;
            if (id.Length == 0) continue;
            var license = "?";
            var gguf = false;
            var hasSafetensorsTag = false;
            var allTags = new List<string>();
            var library = string.Empty;
            if (m.TryGetProperty("tags", out var tags) && tags.ValueKind == JsonValueKind.Array)
                foreach (var t in tags.EnumerateArray())
                {
                    var tag = t.GetString() ?? string.Empty;
                    if (tag.StartsWith("license:", StringComparison.Ordinal)) license = tag[8..];
                    else if (tag == "gguf") gguf = true;
                    else if (tag is "diffusers" or "transformers" or "safetensors" or "onnx" or "mlx") library = library.Length == 0 ? tag : library;
                    if (tag == "safetensors") hasSafetensorsTag = true;
                    allTags.Add(tag);
                }
            var kind = m.TryGetProperty("pipeline_tag", out var p) && p.ValueKind == JsonValueKind.String ? p.GetString() ?? string.Empty : string.Empty;
            if (kind.Length == 0) kind = library;
            var gated = m.TryGetProperty("gated", out var g) && g.ValueKind is not (JsonValueKind.False or JsonValueKind.Null);
            // Parametros: los de safetensors o, si es GGUF, los que dice su cabecera.
            long parameters = 0;
            var safetensors = hasSafetensorsTag;
            if (m.TryGetProperty("safetensors", out var st) && st.ValueKind == JsonValueKind.Object)
            {
                safetensors = true;
                if (st.TryGetProperty("total", out var t) && t.ValueKind == JsonValueKind.Number) parameters = t.GetInt64();
            }
            if (parameters == 0 && m.TryGetProperty("gguf", out var gg) && gg.ValueKind == JsonValueKind.Object && gg.TryGetProperty("total", out var gt) && gt.ValueKind == JsonValueKind.Number)
                parameters = gt.GetInt64();
            candidates.Add(new Candidate(id,
                m.TryGetProperty("downloads", out var d) && d.ValueKind == JsonValueKind.Number ? d.GetInt64() : 0,
                m.TryGetProperty("likes", out var l) && l.ValueKind == JsonValueKind.Number ? l.GetInt32() : 0,
                license, gated, gguf, kind, parameters, safetensors, LooksPrequantized(id, allTags)));
        }
        return candidates;
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
    public static bool TextModel(string kind) => kind is "" or "text-generation" or "text2text-generation" or "image-text-to-text" or "conversational";

    /// <summary>«unsloth/Qwen3.5-4B-GGUF» → «Qwen3.5 4B (unsloth)».</summary>
    public static string NameFor(string repo)
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
