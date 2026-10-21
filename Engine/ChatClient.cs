using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace SocLucia.Engine;

/// <summary>Un mensaje tal como va al modelo. Los de herramienta llevan <see cref="ToolCallId"/>; los del asistente que pidieron herramientas, <see cref="ToolCalls"/>.</summary>
public sealed record ChatMessage(string Role, string Content, string? ToolCallId = null, JsonArray? ToolCalls = null, IReadOnlyList<string>? Images = null);

/// <summary>Una llamada a herramienta que pide el modelo (ya juntada de sus trozos).</summary>
public sealed class ToolCall
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Arguments { get; set; } = string.Empty;

    public JsonObject ToJson() => new()
    {
        ["id"] = Id,
        ["type"] = "function",
        ["function"] = new JsonObject { ["name"] = Name, ["arguments"] = Arguments },
    };
}

/// <summary>Un trozo de respuesta en streaming: texto visible, razonamiento, o el fin con las herramientas pedidas y lo que costo.</summary>
public sealed record ChatDelta(string? Content, string? Reasoning, IReadOnlyList<ToolCall>? ToolCalls = null, string? FinishReason = null, ChatUsage? Usage = null);

/// <summary>Lo que dice el servidor al acabar: tokens de la pregunta y de la respuesta y velocidad (llama-server: usage y timings).</summary>
public sealed record ChatUsage(int? PromptTokens, int? CompletionTokens, double? TokensPerSecond)
{
    public static ChatUsage? From(JsonNode? node)
    {
        var usage = node?["usage"] as JsonObject;
        var timings = node?["timings"] as JsonObject;
        if (usage is null && timings is null) return null;
        static int? Int(JsonNode? n) => n is JsonValue v && v.TryGetValue<double>(out var d) && d >= 0 ? (int)d : null;
        static double? Num(JsonNode? n) => n is JsonValue v && v.TryGetValue<double>(out var d) && d >= 0 && !double.IsInfinity(d) ? d : null;
        return new ChatUsage(
            Int(usage?["prompt_tokens"]) ?? Int(timings?["prompt_n"]),
            Int(usage?["completion_tokens"]) ?? Int(timings?["predicted_n"]),
            Num(timings?["predicted_per_second"]));
    }
}

/// <summary>Cliente del <c>/v1/chat/completions</c> de llama-server, en streaming (SSE), con herramientas.</summary>
public static class ChatClient
{
    /// <summary>La temperatura con la que se pregunta (va tambien como parametro a MLflow).</summary>
    public const double Temperature = 0.7;

    /// <summary>
    /// Lee la respuesta entera en un hilo del pool y deja cada trozo en <paramref name="target"/>.
    /// Nada de esto pasa por el hilo de la interfaz: ni la peticion HTTP ni el troceado del SSE ni
    /// el JSON de cada trozo. La interfaz solo lee <paramref name="target"/> cuando repinta. Los
    /// errores y la cancelacion salen por la tarea devuelta.
    /// </summary>
    public static Task PumpAsync(string baseUrl, IReadOnlyList<ChatMessage> messages, bool thinking, int maxTokens, JsonArray? tools,
        Chat.PendingAnswer target, CancellationToken cancel)
        => Task.Run(async () =>
        {
            await foreach (var delta in StreamAsync(baseUrl, messages, thinking, maxTokens, tools, cancel).ConfigureAwait(false))
                target.Add(delta);
        }, cancel);

    public static async IAsyncEnumerable<ChatDelta> StreamAsync(string baseUrl, IEnumerable<ChatMessage> messages, bool thinking, int maxTokens, JsonArray? tools,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancel)
    {
        var body = new JsonObject
        {
            ["model"] = "local",
            ["stream"] = true,
            // Con pensamiento, el razonamiento gasta del mismo tope: se le da sitio para que quede respuesta.
            ["max_tokens"] = thinking ? maxTokens * 3 : maxTokens,
            ["temperature"] = Temperature,
            ["messages"] = new JsonArray(messages.Select(ToJson).ToArray()),
            // Con el «pensamiento» apagado el modelo contesta directo; encendido, llama-server separa
            // el razonamiento en reasoning_content y se enseña aparte.
            ["chat_template_kwargs"] = new JsonObject { ["enable_thinking"] = thinking },
        };
        if (tools is { Count: > 0 })
        {
            body["tools"] = JsonNode.Parse(tools.ToJsonString());
            body["tool_choice"] = "auto";
        }
        using var request = new HttpRequestMessage(HttpMethod.Post, $"{baseUrl}/v1/chat/completions")
        {
            Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json"),
        };
        using var response = await Downloader.Http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancel);
        if (!response.IsSuccessStatusCode)
        {
            var text = await response.Content.ReadAsStringAsync(cancel);
            throw new HttpRequestException($"{(int)response.StatusCode}: {Trim(text)}");
        }
        var calls = new SortedDictionary<int, ToolCall>();
        string? finish = null;
        ChatUsage? usage = null;
        await using var stream = await response.Content.ReadAsStreamAsync(cancel);
        using var reader = new StreamReader(stream, Encoding.UTF8);
        while (await reader.ReadLineAsync(cancel) is { } line)
        {
            if (!line.StartsWith("data:", StringComparison.Ordinal))
                continue;
            var payload = line[5..].Trim();
            if (payload == "[DONE]")
                break;
            JsonNode? node;
            try { node = JsonNode.Parse(payload); }
            catch (JsonException) { continue; }
            usage = ChatUsage.From(node) ?? usage;
            // El ultimo trozo puede traer «choices» vacio (solo el uso de tokens): no es un error.
            var choice = node?["choices"] is JsonArray { Count: > 0 } choices ? choices[0] : null;
            if (choice?["finish_reason"]?.GetValue<string>() is { Length: > 0 } reason)
                finish = reason;
            var delta = choice?["delta"];
            if (delta is null)
                continue;
            if (delta["tool_calls"] is JsonArray fragments)
            {
                // Los argumentos llegan troceados: se juntan por indice hasta el final del stream.
                foreach (var fragment in fragments.OfType<JsonObject>())
                {
                    var index = fragment["index"]?.GetValue<int>() ?? 0;
                    if (!calls.TryGetValue(index, out var call))
                        calls[index] = call = new ToolCall();
                    if (fragment["id"]?.GetValue<string>() is { Length: > 0 } id) call.Id = id;
                    if (fragment["function"]?["name"]?.GetValue<string>() is { Length: > 0 } name) call.Name += name;
                    if (fragment["function"]?["arguments"]?.GetValue<string>() is { } args) call.Arguments += args;
                }
            }
            var content = delta["content"]?.GetValue<string>();
            var reasoning = delta["reasoning_content"]?.GetValue<string>();
            if (content is { Length: > 0 } || reasoning is { Length: > 0 })
                yield return new ChatDelta(content, reasoning);
        }
        if (calls.Count > 0)
        {
            foreach (var (i, call) in calls)
                if (call.Id.Length == 0) call.Id = $"call_{i}";
            yield return new ChatDelta(null, null, calls.Values.ToList());
        }
        if (finish is not null || usage is not null)
            yield return new ChatDelta(null, null, null, finish, usage);
    }

    private static JsonNode ToJson(ChatMessage m)
    {
        var o = new JsonObject { ["role"] = m.Role };
        if (m.Role == "tool")
        {
            o["tool_call_id"] = m.ToolCallId;
            o["content"] = m.Content;
        }
        else if (m.ToolCalls is { Count: > 0 })
        {
            o["content"] = m.Content.Length > 0 ? m.Content : null;
            o["tool_calls"] = JsonNode.Parse(m.ToolCalls.ToJsonString());
        }
        else if (m.Images is { Count: > 0 })
        {
            // Texto e imagenes en partes (formato OpenAI); las imagenes van en base64 (data URL).
            var parts = new JsonArray();
            foreach (var image in m.Images)
                parts.Add(new JsonObject { ["type"] = "image_url", ["image_url"] = new JsonObject { ["url"] = image } });
            parts.Add(new JsonObject { ["type"] = "text", ["text"] = m.Content });
            o["content"] = parts;
        }
        else
            o["content"] = m.Content;
        return o;
    }

    private static string Trim(string text)
    {
        try
        {
            using var doc = JsonDocument.Parse(text);
            // {"error":{"message":…}} o {"error":"…"}: cualquier otra forma se enseña tal cual.
            if (doc.RootElement.ValueKind == JsonValueKind.Object && doc.RootElement.TryGetProperty("error", out var error))
                return error.ValueKind == JsonValueKind.Object && error.TryGetProperty("message", out var m) ? m.GetString() ?? text
                     : error.ValueKind == JsonValueKind.String ? error.GetString() ?? text
                     : error.ToString();
        }
        catch (JsonException) { }
        return text.Length > 300 ? text[..300] : text;
    }
}
