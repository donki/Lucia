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

/// <summary>Un trozo de respuesta en streaming: texto visible, razonamiento, o el fin con las herramientas pedidas.</summary>
public sealed record ChatDelta(string? Content, string? Reasoning, IReadOnlyList<ToolCall>? ToolCalls = null, string? FinishReason = null);

/// <summary>Cliente del <c>/v1/chat/completions</c> de llama-server, en streaming (SSE), con herramientas.</summary>
public static class ChatClient
{
    public static async IAsyncEnumerable<ChatDelta> StreamAsync(string baseUrl, IEnumerable<ChatMessage> messages, bool thinking, int maxTokens, JsonArray? tools,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancel)
    {
        var body = new JsonObject
        {
            ["model"] = "local",
            ["stream"] = true,
            // Con pensamiento, el razonamiento gasta del mismo tope: se le da sitio para que quede respuesta.
            ["max_tokens"] = thinking ? maxTokens * 3 : maxTokens,
            ["temperature"] = 0.7,
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
            if (node?["choices"]?[0]?["finish_reason"]?.GetValue<string>() is { Length: > 0 } reason)
                finish = reason;
            var delta = node?["choices"]?[0]?["delta"];
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
        if (finish is not null)
            yield return new ChatDelta(null, null, null, finish);
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
            if (doc.RootElement.TryGetProperty("error", out var error))
                return error.TryGetProperty("message", out var m) ? m.GetString() ?? text : error.ToString();
        }
        catch (JsonException) { }
        return text.Length > 300 ? text[..300] : text;
    }
}
