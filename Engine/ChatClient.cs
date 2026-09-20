using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace SocAiChat.Engine;

public sealed record ChatMessage(string Role, string Content);

/// <summary>Un trozo de respuesta en streaming: texto visible o razonamiento (si el modelo piensa y se le deja).</summary>
public sealed record ChatDelta(string? Content, string? Reasoning);

/// <summary>Cliente del <c>/v1/chat/completions</c> de llama-server, en streaming (SSE).</summary>
public static class ChatClient
{
    public static async IAsyncEnumerable<ChatDelta> StreamAsync(string baseUrl, IEnumerable<ChatMessage> messages, bool thinking, int maxTokens,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancel)
    {
        var body = new JsonObject
        {
            ["model"] = "local",
            ["stream"] = true,
            ["max_tokens"] = maxTokens,
            ["temperature"] = 0.7,
            ["messages"] = new JsonArray(messages.Select(m => (JsonNode)new JsonObject { ["role"] = m.Role, ["content"] = m.Content }).ToArray()),
            // Con el «pensamiento» apagado el modelo contesta directo; encendido, llama-server separa
            // el razonamiento en reasoning_content y se enseña aparte.
            ["chat_template_kwargs"] = new JsonObject { ["enable_thinking"] = thinking },
        };
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
        await using var stream = await response.Content.ReadAsStreamAsync(cancel);
        using var reader = new StreamReader(stream, Encoding.UTF8);
        while (await reader.ReadLineAsync(cancel) is { } line)
        {
            if (!line.StartsWith("data:", StringComparison.Ordinal))
                continue;
            var payload = line[5..].Trim();
            if (payload == "[DONE]")
                yield break;
            JsonNode? node;
            try { node = JsonNode.Parse(payload); }
            catch (JsonException) { continue; }
            var delta = node?["choices"]?[0]?["delta"];
            if (delta is null)
                continue;
            var content = delta["content"]?.GetValue<string>();
            var reasoning = delta["reasoning_content"]?.GetValue<string>();
            if (content is { Length: > 0 } || reasoning is { Length: > 0 })
                yield return new ChatDelta(content, reasoning);
        }
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
