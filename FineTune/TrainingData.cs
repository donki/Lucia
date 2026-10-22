using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using SocLucia.Chat;

namespace SocLucia.FineTune;

/// <summary>Un mensaje de un ejemplo: «system», «user» o «assistant».</summary>
public sealed record TrainingMessage(string Role, string Content);

/// <summary>
/// Un ejemplo de entrenamiento: una conversacion que acaba en la respuesta que la IA tiene que
/// aprender. Solo se entrena esa ultima respuesta; lo de antes es el contexto.
/// </summary>
public sealed class TrainingExample
{
    public List<TrainingMessage> Messages { get; init; } = [];
    /// <summary>De donde salio (conversacion, fichero, a mano): para enseñarlo en la lista.</summary>
    public string Source { get; init; } = string.Empty;

    public string Question => Messages.LastOrDefault(m => m.Role == "user")?.Content ?? string.Empty;
    public string Answer => Messages.Count > 0 && Messages[^1].Role == "assistant" ? Messages[^1].Content : string.Empty;

    public static TrainingExample Pair(string question, string answer, string source = "", string? system = null)
    {
        var messages = new List<TrainingMessage>();
        if (!string.IsNullOrWhiteSpace(system)) messages.Add(new("system", system.Trim()));
        messages.Add(new("user", question.Trim()));
        messages.Add(new("assistant", answer.Trim()));
        return new TrainingExample { Messages = messages, Source = source };
    }

    /// <summary>Clave para encontrar repetidos: pregunta y respuesta sin mayusculas ni espacios de mas.</summary>
    internal string Key => Normalize(Question) + "\u0001" + Normalize(Answer);

    private static string Normalize(string text) => string.Join(' ', text.ToLowerInvariant().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
}

/// <summary>El resultado de revisar los ejemplos antes de entrenar.</summary>
public sealed record TrainingReport(int Kept, int Empty, int Duplicates, int TooLong, long ApproxTokens, TrainingVerdict Verdict);

public enum TrainingVerdict { TooFew, Few, Good }

/// <summary>
/// Los ejemplos de entrenamiento: se leen de las conversaciones guardadas, de un fichero (JSONL en
/// los formatos habituales o CSV de dos columnas) o se escriben a mano; se revisan (vacios,
/// repetidos, demasiado largos) y se guardan en JSONL con mensajes, que es lo que lee el script.
/// </summary>
public static class TrainingData
{
    /// <summary>Menos que esto no da para que la IA aprenda nada estable.</summary>
    public const int Minimum = 10;
    /// <summary>A partir de aqui, el resultado suele ser bueno.</summary>
    public const int Recommended = 50;
    /// <summary>Caracteres por ejemplo como mucho (≈ 2000 tokens): mas largo, se recorta por delante al entrenar.</summary>
    public const int MaxChars = 8000;

    /// <summary>
    /// Ejemplos a partir de conversaciones: uno por cada respuesta de texto de la IA, con lo que se
    /// hablo antes como contexto (hasta <paramref name="historyTurns"/> mensajes). Las herramientas, el
    /// razonamiento y los adjuntos no se usan; las respuestas vacias o de error, tampoco.
    /// </summary>
    public static List<TrainingExample> FromThreads(IEnumerable<ChatThread> threads, string? system = null, int historyTurns = 4)
    {
        var result = new List<TrainingExample>();
        foreach (var thread in threads)
        {
            var history = new List<TrainingMessage>();
            foreach (var message in thread.Messages)
            {
                if (message.Role == "tool" || !string.IsNullOrEmpty(message.ToolCalls))
                {
                    // Un turno con herramientas no es una respuesta de texto: se corta la historia para no enseñar media accion.
                    history.Clear();
                    continue;
                }
                if (message.Role is not ("user" or "assistant") || string.IsNullOrWhiteSpace(message.Content))
                    continue;
                if (message.Role == "assistant" && history.Count > 0 && history[^1].Role == "user")
                {
                    var messages = new List<TrainingMessage>();
                    if (!string.IsNullOrWhiteSpace(system)) messages.Add(new("system", system.Trim()));
                    messages.AddRange(history.Skip(Math.Max(0, history.Count - historyTurns)).SkipWhile(m => m.Role != "user"));
                    messages.Add(new("assistant", message.Content.Trim()));
                    result.Add(new TrainingExample { Messages = messages, Source = thread.Title });
                }
                history.Add(new(message.Role, message.Content.Trim()));
            }
        }
        return result;
    }

    /// <summary>Lee un fichero: .jsonl/.json (varios formatos) o .csv/.tsv/.txt (pregunta y respuesta en dos columnas).</summary>
    public static List<TrainingExample> FromFile(string path)
    {
        var text = File.ReadAllText(path);
        var ext = Path.GetExtension(path).ToLowerInvariant();
        var source = Path.GetFileName(path);
        return ext is ".csv" or ".tsv" or ".txt" ? ParseCsv(text, source, ext == ".tsv" ? '\t' : null) : ParseJson(text, source);
    }

    /// <summary>
    /// JSONL (un ejemplo por linea) o un JSON con una lista. Formatos: <c>messages</c> (OpenAI/ChatML),
    /// <c>conversations</c> (ShareGPT, from/value), <c>prompt</c>/<c>completion</c>,
    /// <c>instruction</c>/<c>input</c>/<c>output</c> (Alpaca) y <c>question</c>/<c>answer</c> (o
    /// <c>pregunta</c>/<c>respuesta</c>). Lo que no se entiende se salta.
    /// </summary>
    public static List<TrainingExample> ParseJson(string text, string source = "")
    {
        var nodes = new List<JsonNode?>();
        var trimmed = text.TrimStart('﻿', ' ', '\r', '\n', '\t');
        if (trimmed.StartsWith('['))
        {
            if (JsonNode.Parse(trimmed) is JsonArray array) nodes.AddRange(array);
        }
        else
        {
            foreach (var line in text.Split('\n'))
            {
                var l = line.Trim().TrimStart('﻿');
                if (l.Length == 0) continue;
                try { nodes.Add(JsonNode.Parse(l)); } catch (JsonException) { }
            }
        }
        var result = new List<TrainingExample>();
        foreach (var node in nodes.OfType<JsonObject>())
            if (FromJson(node, source) is { } example)
                result.Add(example);
        return result;
    }

    private static TrainingExample? FromJson(JsonObject o, string source)
    {
        string? Str(params string[] names)
        {
            foreach (var n in names)
                foreach (var (key, value) in o)
                    if (string.Equals(key, n, StringComparison.OrdinalIgnoreCase) && value is JsonValue v && v.TryGetValue<string>(out var s))
                        return s;
            return null;
        }

        var list = o["messages"] as JsonArray ?? o["conversations"] as JsonArray;
        if (list is not null)
        {
            var messages = new List<TrainingMessage>();
            foreach (var item in list.OfType<JsonObject>())
            {
                var role = (item["role"] ?? item["from"])?.GetValue<string>()?.ToLowerInvariant() switch
                {
                    "system" => "system",
                    "user" or "human" => "user",
                    "assistant" or "gpt" or "model" or "bot" => "assistant",
                    _ => null,
                };
                var content = (item["content"] ?? item["value"]) switch
                {
                    JsonValue v when v.TryGetValue<string>(out var s) => s,
                    JsonArray parts => string.Concat(parts.OfType<JsonObject>().Select(p => p["text"]?.GetValue<string>() ?? string.Empty)),
                    _ => null,
                };
                if (role is null || string.IsNullOrWhiteSpace(content)) continue;
                messages.Add(new(role, content.Trim()));
            }
            // Se entrena la ultima respuesta: lo que venga detras de ella (otra pregunta sin contestar) sobra.
            var last = messages.FindLastIndex(m => m.Role == "assistant");
            if (last < 0 || !messages.Take(last).Any(m => m.Role == "user")) return null;
            return new TrainingExample { Messages = messages.Take(last + 1).ToList(), Source = source };
        }

        var system = Str("system");
        var question = Str("prompt", "question", "pregunta", "input_text", "query");
        var answer = Str("completion", "answer", "respuesta", "response", "output_text");
        if (question is null || answer is null)
        {
            var instruction = Str("instruction", "instruccion", "instrucción");
            var output = Str("output", "salida");
            if (instruction is null || output is null) return null;
            var input = Str("input", "entrada");
            question = string.IsNullOrWhiteSpace(input) ? instruction : instruction.Trim() + "\n\n" + input.Trim();
            answer = output;
        }
        if (string.IsNullOrWhiteSpace(question) || string.IsNullOrWhiteSpace(answer)) return null;
        return TrainingExample.Pair(question, answer, source, system);
    }

    /// <summary>
    /// CSV de dos columnas (pregunta, respuesta), con comillas para los textos con comas o saltos de
    /// linea. El separador se adivina (coma, punto y coma o tabulador) y una primera fila de cabecera
    /// («pregunta;respuesta», «question,answer», «prompt,completion») se salta.
    /// </summary>
    public static List<TrainingExample> ParseCsv(string text, string source = "", char? separator = null)
    {
        text = text.TrimStart('﻿');
        var sep = separator ?? GuessSeparator(text);
        var rows = ReadCsv(text, sep);
        var result = new List<TrainingExample>();
        for (var i = 0; i < rows.Count; i++)
        {
            var row = rows[i];
            if (row.Count < 2) continue;
            if (i == 0 && IsHeader(row[0], row[1])) continue;
            if (string.IsNullOrWhiteSpace(row[0]) || string.IsNullOrWhiteSpace(row[1])) continue;
            result.Add(TrainingExample.Pair(row[0], row[1], source));
        }
        return result;
    }

    private static readonly HashSet<string> HeaderWords = new(StringComparer.OrdinalIgnoreCase)
    { "pregunta", "preguntas", "question", "questions", "prompt", "input", "entrada", "respuesta", "respuestas", "answer", "answers", "completion", "output", "salida", "q", "a", "p", "r" };

    private static bool IsHeader(string a, string b) => HeaderWords.Contains(a.Trim()) && HeaderWords.Contains(b.Trim());

    private static char GuessSeparator(string text)
    {
        var first = text.Split('\n')[0];
        var counts = new[] { '\t', ';', ',' }.Select(c => (c, n: first.Count(x => x == c))).OrderByDescending(x => x.n).First();
        return counts.n > 0 ? counts.c : ',';
    }

    internal static List<List<string>> ReadCsv(string text, char sep)
    {
        var rows = new List<List<string>>();
        var row = new List<string>();
        var field = new StringBuilder();
        var quoted = false;
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (quoted)
            {
                if (c == '"' && i + 1 < text.Length && text[i + 1] == '"') { field.Append('"'); i++; }
                else if (c == '"') quoted = false;
                else field.Append(c);
            }
            else if (c == '"' && field.Length == 0) quoted = true;
            else if (c == sep) { row.Add(field.ToString()); field.Clear(); }
            else if (c == '\n' || c == '\r')
            {
                if (c == '\r' && i + 1 < text.Length && text[i + 1] == '\n') i++;
                row.Add(field.ToString()); field.Clear();
                if (row.Any(f => f.Length > 0)) rows.Add(row);
                row = [];
            }
            else field.Append(c);
        }
        row.Add(field.ToString());
        if (row.Any(f => f.Length > 0)) rows.Add(row);
        return rows;
    }

    /// <summary>Quita vacios, repetidos y los que pasan de largo, y dice si hay bastantes.</summary>
    public static (List<TrainingExample> Kept, TrainingReport Report) Review(IEnumerable<TrainingExample> examples)
    {
        var kept = new List<TrainingExample>();
        var seen = new HashSet<string>();
        int empty = 0, duplicates = 0, tooLong = 0;
        long chars = 0;
        foreach (var e in examples)
        {
            if (string.IsNullOrWhiteSpace(e.Question) || string.IsNullOrWhiteSpace(e.Answer)) { empty++; continue; }
            if (e.Answer.Length > MaxChars) { tooLong++; continue; }
            if (!seen.Add(e.Key)) { duplicates++; continue; }
            kept.Add(e);
            chars += e.Messages.Sum(m => (long)m.Content.Length);
        }
        var verdict = kept.Count < Minimum ? TrainingVerdict.TooFew : kept.Count < Recommended ? TrainingVerdict.Few : TrainingVerdict.Good;
        return (kept, new TrainingReport(kept.Count, empty, duplicates, tooLong, chars / 4, verdict));
    }

    /// <summary>Guarda los ejemplos en el JSONL que lee el script (<c>{"messages":[…]}</c> por linea).</summary>
    public static void Save(IEnumerable<TrainingExample> examples, string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using var writer = new StreamWriter(path, false, new UTF8Encoding(false));
        foreach (var e in examples)
        {
            var messages = new JsonArray();
            foreach (var m in e.Messages)
                messages.Add(new JsonObject { ["role"] = m.Role, ["content"] = m.Content });
            writer.WriteLine(new JsonObject { ["messages"] = messages }.ToJsonString());
        }
    }
}
