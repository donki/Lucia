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
                        // Lo que el modelo sabe hacer, para que los editores lo declaren (Copilot Chat:
                        // toolCalling e imageInput). Herramientas siempre (llama-server con --jinja); vision
                        // si tiene su mmproj al lado, que es con lo que arranca el motor.
                        ["context_length"] = TokenLimits.Context(settings),
                        ["capabilities"] = new JsonObject
                        {
                            ["tools"] = true,
                            ["vision"] = settings.ModelPath is { Length: > 0 } model && File.Exists(Engine.ModelCatalog.MmprojPathFor(model)),
                        },
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
            DiagLog.Write("puerta", $"{path}: {(ex is OperationCanceledException ? "cancelada" : "error " + DiagLog.Describe(ex))}");
            try { await ErrorAsync(response, 502, ex.Message); } catch (Exception) { }
        }
    }

    private async Task ForwardAsync(HttpListenerRequest request, HttpListenerResponse response, string path, CancellationToken cancel)
    {
        var clock = System.Diagnostics.Stopwatch.StartNew();
        byte[] body;
        using (var ms = new MemoryStream())
        {
            await request.InputStream.CopyToAsync(ms, cancel);
            body = ms.ToArray();
        }
        // Una traza por peticion (solo con MLflow activado): tiempos, estado y, con la casilla del texto, la pregunta del editor.
        var telemetry = Services.Mlflow.MlflowTelemetry.Current;
        var question = telemetry.Enabled ? LastUserText(body) : null;
        var trace = telemetry.Enabled
            ? new Services.Mlflow.InteractionTrace("door", "door-" + Guid.NewGuid().ToString("N")[..8], telemetry.InstallId, App.RunConfig(), telemetry.SendText, question, question?.Length ?? 0, 0)
            : null;
        trace?.Root.Set("lucia.door_path", path).Set("lucia.request_bytes", (long)body.Length);
        string baseUrl;
        var engineSpan = trace?.Engine();
        try
        {
            baseUrl = await _engine.EnsureReadyAsync(null, cancel);
            if (engineSpan is not null) Services.Mlflow.InteractionTrace.End(engineSpan);
        }
        catch (Exception ex)
        {
            DiagLog.Write("puerta", $"{path}: el motor no esta listo ({DiagLog.Describe(ex)})");
            if (trace is not null) telemetry.Trace(Finish(trace, "error", DiagLog.Describe(ex)));
            await ErrorAsync(response, 503, ex.Message);
            return;
        }
        if (path == "/v1/chat/completions")
            body = ThinkingOffByDefault(body);
        if (path is "/v1/chat/completions" or "/v1/completions")
            body = TokenLimits.ApplyToDoorRequest(body, TokenLimits.Answer(AppSettings.Current), TokenLimits.Context(AppSettings.Current));
        using var upstreamRequest = new HttpRequestMessage(HttpMethod.Post, baseUrl + path)
        {
            Content = new ByteArrayContent(body) { Headers = { ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/json") } },
        };
        using var upstream = await Downloader.Http.SendAsync(upstreamRequest, HttpCompletionOption.ResponseHeadersRead, cancel);
        DiagLog.Write("puerta", $"{path}: {(int)upstream.StatusCode} del motor a los {clock.ElapsedMilliseconds} ms ({body.Length} bytes pedidos)");
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
        DiagLog.Write("puerta", $"{path}: acabada a los {clock.ElapsedMilliseconds} ms");
        if (trace is not null)
        {
            trace.Root.Set("lucia.http_status", (long)(int)upstream.StatusCode);
            telemetry.Trace(Finish(trace, (int)upstream.StatusCode < 400 ? "ok" : "error", (int)upstream.StatusCode < 400 ? null : $"HTTP {(int)upstream.StatusCode}"));
        }
        // A MLflow solo tiempos y estado: lo que pide el editor (su codigo) no sale de aqui.
        if (Services.Mlflow.MlflowTelemetry.Current.Enabled)
            Services.Mlflow.MlflowTelemetry.Current.Request(App.RunConfig(), new Services.Mlflow.RequestRecord("door", null, clock.Elapsed.TotalMilliseconds, null, null, null, 0, 0, 0,
                (int)upstream.StatusCode < 400 ? "ok" : "error", (int)upstream.StatusCode < 400 ? null : $"HTTP {(int)upstream.StatusCode}"));
    }

    private static Services.Mlflow.InteractionTrace Finish(Services.Mlflow.InteractionTrace trace, string outcome, string? error)
    {
        trace.Finish(outcome, error, null, null, null, 0, 0);
        return trace;
    }

    /// <summary>El texto del ultimo mensaje del usuario en una peticion de chat (para la traza, solo si se permite el texto).</summary>
    internal static string? LastUserText(byte[] body)
    {
        try
        {
            if (JsonNode.Parse(body)?["messages"] is not JsonArray messages) return null;
            var last = messages.LastOrDefault(m => m?["role"]?.GetValue<string>() == "user")?["content"];
            return last switch
            {
                JsonValue v when v.TryGetValue<string>(out var s) => s,
                JsonArray parts => string.Join("\n", parts.Select(p => p?["text"]?.GetValue<string>()).Where(t => t is not null)),
                _ => null,
            };
        }
        catch (Exception) { return null; }
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
