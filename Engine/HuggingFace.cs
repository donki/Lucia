using System.Text.Json;

namespace SocLucia.Engine;

/// <summary>
/// Buscador de IA en Hugging Face: repositorios con GGUF, ordenados por descargas. De cada uno se
/// mira su lista de ficheros para elegir el cuantizado habitual y saber lo que pesa: con eso se
/// dice si es optimo para este PC, igual que con el catalogo.
/// </summary>
public static class HuggingFace
{
    public sealed record Hit(CatalogModel Model, string FileName, long Downloads, int Likes, bool Gated);

    public static async Task<List<Hit>> SearchAsync(string query, PcProfile pc, CancellationToken cancel)
    {
        var url = $"https://huggingface.co/api/models?search={Uri.EscapeDataString(query)}&filter=gguf&sort=downloads&direction=-1&limit=24";
        using var response = await Downloader.Http.GetAsync(url, cancel);
        response.EnsureSuccessStatusCode();
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancel));
        var candidates = new List<(string Id, long Downloads, int Likes, string License, bool Gated)>();
        foreach (var m in doc.RootElement.EnumerateArray())
        {
            var id = m.TryGetProperty("id", out var idNode) ? idNode.GetString() ?? string.Empty : string.Empty;
            if (id.Length == 0) continue;
            var license = "?";
            if (m.TryGetProperty("tags", out var tags) && tags.ValueKind == JsonValueKind.Array)
                foreach (var t in tags.EnumerateArray())
                    if (t.GetString() is { } tag && tag.StartsWith("license:", StringComparison.Ordinal)) { license = tag[8..]; break; }
            var gated = m.TryGetProperty("gated", out var g) && g.ValueKind is not (JsonValueKind.False or JsonValueKind.Null);
            candidates.Add((id,
                m.TryGetProperty("downloads", out var d) && d.ValueKind == JsonValueKind.Number ? d.GetInt64() : 0,
                m.TryGetProperty("likes", out var l) && l.ValueKind == JsonValueKind.Number ? l.GetInt32() : 0,
                license, gated));
        }
        // La lista de ficheros de cada uno, a la vez; los que no tengan un GGUF de un solo fichero se descartan.
        var tasks = candidates.Take(16).Select(async c =>
        {
            try
            {
                var model = new CatalogModel(NameFor(c.Id), c.Id, 0, c.License, "CatalogHf");
                var file = await ModelCatalog.PickFileAsync(model, cancel);
                if (file.Size is not { } size || size <= 0) return null;
                return new Hit(model with { ApproxBytes = size }, System.IO.Path.GetFileName(file.Name), c.Downloads, c.Likes, c.Gated);
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception) { return null; }
        });
        var hits = (await Task.WhenAll(tasks)).OfType<Hit>().ToList();
        // Primero las que van bien en este PC; dentro de cada grupo, las mas descargadas.
        return hits.OrderBy(h => ModelCatalog.FitOf(h.Model, pc)).ThenByDescending(h => h.Downloads).ToList();
    }

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
