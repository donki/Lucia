using System.IO;
using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;

namespace SocLucia.Services;

/// <summary>
/// La carpeta de documentos del usuario: lo que deje ahi (texto, Markdown, CSV, JSON, codigo,
/// HTML, .docx) la IA lo tiene en cuenta. Sin base vectorial: los ficheros se trocean y, con cada
/// pregunta, se le pasan los trozos que comparten mas palabras con ella; ademas la IA puede
/// listarlos y leerlos enteros con dos herramientas. Todo en este PC.
/// </summary>
public static class DocumentIndex
{
    private static readonly string[] Extensions = [".txt", ".md", ".markdown", ".csv", ".tsv", ".json", ".xml", ".yaml", ".yml", ".log", ".ini", ".cfg", ".toml", ".cs", ".py", ".js", ".ts", ".ps1", ".sql", ".html", ".htm", ".docx", ".rtf"];
    private const int MaxFiles = 300;
    private const long MaxBytes = 4L * 1024 * 1024;
    private const int ChunkChars = 900;
    private const int MaxChunksOut = 4;

    private sealed record Chunk(string File, string Text, HashSet<string> Words);
    private sealed record Indexed(DateTime Written, long Length, List<Chunk> Chunks);
    private static readonly Dictionary<string, Indexed> Cache = new(StringComparer.OrdinalIgnoreCase);

    public static string Folder => AppSettings.Current.DocumentsFolder is { Length: > 0 } f ? f : DefaultFolder;
    public static string DefaultFolder => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Lucia");

    /// <summary>Los documentos que se tienen en cuenta (por extension, con tope de tamaño y numero).</summary>
    public static List<FileInfo> Files()
    {
        if (!Directory.Exists(Folder)) return [];
        var options = new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true };
        return Directory.EnumerateFiles(Folder, "*", options)
            .Select(p => new FileInfo(p))
            .Where(f => Extensions.Contains(f.Extension.ToLowerInvariant()) && f.Length <= MaxBytes)
            .OrderBy(f => f.FullName, StringComparer.OrdinalIgnoreCase)
            .Take(MaxFiles)
            .ToList();
    }

    public static string Relative(string path) => Path.GetRelativePath(Folder, path);

    /// <summary>El texto de un documento (docx desempaquetado, HTML sin etiquetas).</summary>
    public static string ReadText(string path)
    {
        var ext = Path.GetExtension(path).ToLowerInvariant();
        if (ext == ".docx")
        {
            using var zip = ZipFile.OpenRead(path);
            var entry = zip.GetEntry("word/document.xml") ?? throw new InvalidDataException("docx sin word/document.xml");
            using var reader = new StreamReader(entry.Open(), Encoding.UTF8);
            var xml = reader.ReadToEnd();
            xml = Regex.Replace(xml, @"</w:p>", "\n");
            xml = Regex.Replace(xml, @"<w:tab/>", "\t");
            return System.Net.WebUtility.HtmlDecode(Regex.Replace(xml, "<[^>]+>", string.Empty)).Trim();
        }
        var text = File.ReadAllText(path);
        if (ext is ".html" or ".htm")
        {
            text = Regex.Replace(text, @"<(script|style)[^>]*>.*?</\1>", string.Empty, RegexOptions.Singleline | RegexOptions.IgnoreCase);
            text = Regex.Replace(text, @"<br\s*/?>|</(p|div|li|h\d|tr)>", "\n", RegexOptions.IgnoreCase);
            text = System.Net.WebUtility.HtmlDecode(Regex.Replace(text, "<[^>]+>", string.Empty));
        }
        if (ext == ".rtf")
            text = Regex.Replace(Regex.Replace(text, @"\\par[d]?", "\n"), @"\\[a-z]+-?\d* ?|[{}]", string.Empty);
        return text;
    }

    /// <summary>Los trozos de documentos que mas tienen que ver con la pregunta, listos para el system prompt; vacio si nada encaja.</summary>
    public static string Prompt(string question)
    {
        var files = Files();
        if (files.Count == 0) return string.Empty;
        var words = Words(question);
        var header = $"The user keeps documents in `{Folder}` ({files.Count} file(s)). Use `list_documents` and `read_document` when a question may refer to them.";
        if (words.Count == 0) return header;
        var scored = new List<(Chunk Chunk, double Score)>();
        foreach (var file in files)
            foreach (var chunk in ChunksOf(file))
            {
                var hits = words.Count(w => chunk.Words.Contains(w));
                if (hits >= Math.Min(2, words.Count))
                    scored.Add((chunk, hits + (double)hits / words.Count));
            }
        if (scored.Count == 0) return header;
        var sb = new StringBuilder(header);
        sb.AppendLine().AppendLine("Excerpts from the user's documents that may be relevant to the question (cite the file when you use them):");
        foreach (var (chunk, _) in scored.OrderByDescending(s => s.Score).Take(MaxChunksOut))
            sb.AppendLine($"[{chunk.File}]\n{chunk.Text.Trim()}\n");
        return sb.ToString();
    }

    private static List<Chunk> ChunksOf(FileInfo file)
    {
        if (Cache.TryGetValue(file.FullName, out var cached) && cached.Written == file.LastWriteTimeUtc && cached.Length == file.Length)
            return cached.Chunks;
        var chunks = new List<Chunk>();
        try
        {
            var text = ReadText(file.FullName);
            var name = Relative(file.FullName);
            for (var i = 0; i < text.Length; i += ChunkChars - 150)
            {
                var piece = text.Substring(i, Math.Min(ChunkChars, text.Length - i));
                chunks.Add(new Chunk(name, piece, Words(piece)));
                if (i + ChunkChars >= text.Length) break;
            }
        }
        catch (Exception) { }
        Cache[file.FullName] = new Indexed(file.LastWriteTimeUtc, file.Length, chunks);
        return chunks;
    }

    private static readonly HashSet<string> Stop = new(StringComparer.OrdinalIgnoreCase)
    {
        "que", "como", "para", "con", "los", "las", "del", "una", "uno", "por", "sobre", "the", "and", "for", "with", "this", "that", "what", "which", "from", "have", "has", "are", "was", "cual", "cuál", "qué", "cómo", "donde", "dónde", "hay", "esta", "está", "tiene", "tienen", "puede", "pueden", "dime", "quiero", "me", "mi", "mis", "tu", "tus", "sus", "sin", "más", "mas", "pero", "también",
    };

    private static HashSet<string> Words(string text)
        => Regex.Matches(text.ToLowerInvariant(), @"[\p{L}\p{N}][\p{L}\p{N}_\-]{2,}")
            .Select(m => m.Value)
            .Where(w => !Stop.Contains(w))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
}
