using System.IO;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using SocLucia.Engine;
using SocLucia.Services;

namespace SocLucia.Editor;

/// <summary>
/// Puerta local para editores de codigo (VS Code y cualquier cliente que hable la API de chat de
/// OpenAI): <c>http://127.0.0.1:&lt;puerto&gt;/v1/...</c>, apagada por defecto. Solo escucha en loopback,
/// exige el token (Bearer) y reenvia cada peticion tal cual al llama-server que ya corre la
/// aplicacion, arrancandolo si hace falta. Streaming incluido. Nada sale del PC.
/// </summary>
public sealed class EditorDoor : IDisposable
{
    private readonly EngineHost _engine;
    private HttpListener? _listener;
    private CancellationTokenSource? _cts;

    public EditorDoor(EngineHost engine) => _engine = engine;

    public bool Running => _listener?.IsListening == true;
    public string? LastError { get; private set; }
    public string BaseUrl => $"http://127.0.0.1:{AppSettings.Current.EditorDoor.Port}/v1";

    /// <summary>Que el servidor coincida con el ajuste: arranca si esta activado, para si no.</summary>
    public void Apply()
    {
        var settings = AppSettings.Current.EditorDoor;
        Stop();
        if (!settings.Enabled)
            return;
        try
        {
            var listener = new HttpListener();
            listener.Prefixes.Add($"http://127.0.0.1:{settings.Port}/");
            listener.Start();
            _listener = listener;
            _cts = new CancellationTokenSource();
            _ = AcceptLoopAsync(listener, _cts.Token);
            LastError = null;
            EngineHost.Log($"puerta de editores escuchando en 127.0.0.1:{settings.Port}");
        }
        catch (Exception ex)
        {
            LastError = ex.Message;
            EngineHost.Log($"puerta de editores: {ex.Message}");
        }
    }

    public void Stop()
    {
        try { _cts?.Cancel(); } catch (Exception) { }
        try { _listener?.Stop(); _listener?.Close(); } catch (Exception) { }
        _listener = null;
        _cts = null;
    }

    private async Task AcceptLoopAsync(HttpListener listener, CancellationToken cancel)
    {
        while (!cancel.IsCancellationRequested && listener.IsListening)
        {
            HttpListenerContext context;
            try { context = await listener.GetContextAsync(); }
            catch (Exception) { break; }
            _ = Task.Run(() => HandleAsync(context, cancel), cancel);
        }
    }

    private async Task HandleAsync(HttpListenerContext context, CancellationToken cancel)
    {
        var request = context.Request;
        var response = context.Response;
        var path = request.Url?.AbsolutePath ?? "/";
        try
        {
            if (path == "/health")
            {
                await WriteJsonAsync(response, 200, new JsonObject
                {
                    ["status"] = "ok",
                    ["engine"] = _engine.State.ToString().ToLowerInvariant(),
                    ["model"] = AppSettings.Current.ModelName,
                });
                return;
            }
            if (!Authorized(request))
            {
                await ErrorAsync(response, 401, Localization.Loc.Get("DoorBadToken"));
                return;
            }
            if (path == "/v1/models" && request.HttpMethod == "GET")
            {
                var settings = AppSettings.Current;
                var data = new JsonArray();
                if (settings.HasModel)
                    data.Add(new JsonObject
                    {
                        ["id"] = ModelId(settings.ModelName ?? "local"),
                        ["object"] = "model",
                        ["created"] = 0,
                        ["owned_by"] = "soc-lucia",
                        ["name"] = settings.ModelName,
                        ["file"] = Path.GetFileName(settings.ModelPath),
                    });
                await WriteJsonAsync(response, 200, new JsonObject { ["object"] = "list", ["data"] = data });
                return;
            }
            if (request.HttpMethod == "POST" && path is "/v1/chat/completions" or "/v1/completions" or "/v1/embeddings")
            {
                await ForwardAsync(request, response, path, cancel);
                return;
            }
            await ErrorAsync(response, 404, "No such route");
        }
        catch (Exception ex)
        {
            try { await ErrorAsync(response, 502, ex.Message); } catch (Exception) { }
        }
    }

    private async Task ForwardAsync(HttpListenerRequest request, HttpListenerResponse response, string path, CancellationToken cancel)
    {
        string baseUrl;
        try
        {
            baseUrl = await _engine.EnsureReadyAsync(null, cancel);
        }
        catch (Exception ex)
        {
            await ErrorAsync(response, 503, ex.Message);
            return;
        }
        byte[] body;
        using (var ms = new MemoryStream())
        {
            await request.InputStream.CopyToAsync(ms, cancel);
            body = ms.ToArray();
        }
        if (path == "/v1/chat/completions")
            body = ThinkingOffByDefault(body);
        using var upstreamRequest = new HttpRequestMessage(HttpMethod.Post, baseUrl + path)
        {
            Content = new ByteArrayContent(body) { Headers = { ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/json") } },
        };
        using var upstream = await Downloader.Http.SendAsync(upstreamRequest, HttpCompletionOption.ResponseHeadersRead, cancel);
        response.StatusCode = (int)upstream.StatusCode;
        response.ContentType = upstream.Content.Headers.ContentType?.ToString() ?? "application/json";
        response.Headers["Cache-Control"] = "no-cache";
        response.SendChunked = true;
        await using var source = await upstream.Content.ReadAsStreamAsync(cancel);
        var buffer = new byte[8192];
        int read;
        while ((read = await source.ReadAsync(buffer, cancel)) > 0)
        {
            await response.OutputStream.WriteAsync(buffer.AsMemory(0, read), cancel);
            await response.OutputStream.FlushAsync(cancel);   // cada trozo del SSE sale al momento
        }
        response.Close();
    }

    /// <summary>Sin indicacion del cliente, el modelo no «piensa»: un razonador se gastaria toda la respuesta dentro de &lt;think&gt;.</summary>
    public static byte[] ThinkingOffByDefault(byte[] body)
    {
        try
        {
            if (JsonNode.Parse(body) is not JsonObject obj)
                return body;
            if (obj.ContainsKey("chat_template_kwargs") || obj.ContainsKey("reasoning_effort") || obj.ContainsKey("reasoning") || obj.ContainsKey("thinking"))
                return body;
            obj["chat_template_kwargs"] = new JsonObject { ["enable_thinking"] = false };
            return Encoding.UTF8.GetBytes(obj.ToJsonString());
        }
        catch (JsonException)
        {
            return body;
        }
    }

    private static bool Authorized(HttpListenerRequest request)
    {
        var token = AppSettings.Current.EditorDoor.Token;
        var presented = request.Headers["Authorization"] is { } auth && auth.StartsWith("Bearer ", StringComparison.Ordinal)
            ? auth[7..].Trim()
            : request.Headers["x-api-key"]?.Trim() ?? string.Empty;
        return presented.Length > 0 && CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(presented), Encoding.UTF8.GetBytes(token));
    }

    /// <summary>Un identificador sin espacios: «Qwen3.5 4B» → «qwen3.5-4b».</summary>
    public static string ModelId(string name)
    {
        var id = new string(name.Trim().Select(c => char.IsAsciiLetterOrDigit(c) || c is '.' or '-' ? char.ToLowerInvariant(c) : '-').ToArray()).Trim('-');
        return id.Length == 0 ? "local" : id;
    }

    private static Task ErrorAsync(HttpListenerResponse response, int status, string message) =>
        WriteJsonAsync(response, status, new JsonObject { ["error"] = new JsonObject { ["message"] = message, ["type"] = "soc_lucia_error", ["code"] = status } });

    private static async Task WriteJsonAsync(HttpListenerResponse response, int status, JsonNode body)
    {
        var bytes = Encoding.UTF8.GetBytes(body.ToJsonString());
        response.StatusCode = status;
        response.ContentType = "application/json";
        response.ContentLength64 = bytes.Length;
        await response.OutputStream.WriteAsync(bytes);
        response.Close();
    }

    public void Dispose() => Stop();
}
