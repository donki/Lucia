using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json.Nodes;
using SocLucia.Editor;
using SocLucia.Engine;
using SocLucia.Services;

namespace SocLucia.Tests;

/// <summary>
/// La puerta de editores en un puerto libre de 127.0.0.1 (nunca el 41417 de Josep) y sin modelo:
/// se prueban las rutas, el token y los errores; el reenvio al motor no llega a arrancarlo porque
/// sin modelo <see cref="EngineHost.EnsureReadyAsync"/> se niega antes de hacer nada.
/// </summary>
public sealed class EditorDoorTests : IDisposable
{
    private readonly EngineHost _engine = new();
    private readonly EditorDoor _door;
    private readonly HttpClient _http = new();

    public EditorDoorTests()
    {
        var s = Sandbox.ResetSettings();
        s.EditorDoor.Port = Sandbox.FreePort();
        s.EditorDoor.Enabled = true;
        s.EditorDoor.Token = "secreto";
        _door = new EditorDoor(_engine);
        _door.Apply();
        Assert.True(_door.Running, _door.LastError);
    }

    public void Dispose()
    {
        _door.Dispose();
        _engine.Dispose();
        _http.Dispose();
        Sandbox.ResetSettings();
    }

    private string Url(string path) => $"http://127.0.0.1:{AppSettings.Current.EditorDoor.Port}{path}";

    private async Task<(HttpStatusCode Status, JsonNode? Body)> Send(HttpMethod method, string path, string? bearer = "secreto", string? apiKey = null, string? body = null)
    {
        using var request = new HttpRequestMessage(method, Url(path));
        if (bearer is not null) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearer);
        if (apiKey is not null) request.Headers.Add("x-api-key", apiKey);
        if (body is not null) request.Content = new StringContent(body, Encoding.UTF8, "application/json");
        using var response = await _http.SendAsync(request);
        var text = await response.Content.ReadAsStringAsync();
        return (response.StatusCode, text.Length > 0 ? JsonNode.Parse(text) : null);
    }

    [Fact]
    public async Task Health_needs_no_token()
    {
        AppSettings.Current.ModelName = "Qwen3.5 4B";
        var (status, body) = await Send(HttpMethod.Get, "/health", bearer: null);
        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal("ok", body!["status"]!.GetValue<string>());
        Assert.Equal("nomodel", body["engine"]!.GetValue<string>());
        Assert.Equal("Qwen3.5 4B", body["model"]!.GetValue<string>());
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData("malo", null)]
    [InlineData("secret", null)]
    [InlineData("secreto2", null)]
    [InlineData(null, "malo")]
    [InlineData("", null)]
    public async Task Wrong_or_missing_token_is_401(string? bearer, string? apiKey)
    {
        var (status, body) = await Send(HttpMethod.Get, "/v1/models", bearer, apiKey);
        Assert.Equal(HttpStatusCode.Unauthorized, status);
        Assert.Equal(401, body!["error"]!["code"]!.GetValue<int>());
        Assert.Equal("soc_lucia_error", body["error"]!["type"]!.GetValue<string>());
    }

    [Fact]
    public async Task Token_also_works_as_x_api_key()
    {
        var (status, _) = await Send(HttpMethod.Get, "/v1/models", bearer: null, apiKey: " secreto ");
        Assert.Equal(HttpStatusCode.OK, status);
    }

    [Fact]
    public async Task Models_is_empty_without_a_model_and_describes_it_when_there_is_one()
    {
        var (status, body) = await Send(HttpMethod.Get, "/v1/models");
        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal("list", body!["object"]!.GetValue<string>());
        Assert.Empty(body["data"]!.AsArray());

        var s = AppSettings.Current;
        var model = Path.Combine(Sandbox.NewFolder("puerta"), "Qwen3.5-4B-Q4_K_M.gguf");
        File.WriteAllText(model, "");
        s.ModelPath = model;
        s.ModelName = "Qwen3.5 4B";
        s.ContextTokens = 100_000;
        (_, body) = await Send(HttpMethod.Get, "/v1/models");
        var m = body!["data"]![0]!;
        Assert.Equal("qwen3.5-4b", m["id"]!.GetValue<string>());
        Assert.Equal("Qwen3.5-4B-Q4_K_M.gguf", m["file"]!.GetValue<string>());
        Assert.Equal(32768, m["context_length"]!.GetValue<int>());
        Assert.True(m["capabilities"]!["tools"]!.GetValue<bool>());
        Assert.False(m["capabilities"]!["vision"]!.GetValue<bool>());

        File.WriteAllText(ModelCatalog.MmprojPathFor(model), "");
        s.ContextTokens = 512;
        s.ModelName = null;
        (_, body) = await Send(HttpMethod.Get, "/v1/models");
        m = body!["data"]![0]!;
        Assert.True(m["capabilities"]!["vision"]!.GetValue<bool>());
        Assert.Equal(2048, m["context_length"]!.GetValue<int>());
        Assert.Equal("local", m["id"]!.GetValue<string>());
    }

    [Fact]
    public async Task Chat_without_a_model_is_503_and_does_not_start_the_engine()
    {
        var (status, body) = await Send(HttpMethod.Post, "/v1/chat/completions", body: "{\"messages\":[]}");
        Assert.Equal(HttpStatusCode.ServiceUnavailable, status);
        Assert.Equal(SocLucia.Localization.Loc.Get("NoModelYet"), body!["error"]!["message"]!.GetValue<string>());
        Assert.Equal(EngineState.NoModel, _engine.State);
    }

    [Theory]
    [InlineData("GET", "/v1/chat/completions")]
    [InlineData("POST", "/v1/models")]
    [InlineData("GET", "/otra")]
    public async Task Unknown_routes_are_404(string method, string path)
    {
        var (status, _) = await Send(new HttpMethod(method), path, body: method == "POST" ? "{}" : null);
        Assert.Equal(HttpStatusCode.NotFound, status);
    }

    [Fact]
    public void Apply_stops_when_disabled_and_reports_a_busy_port()
    {
        Assert.Equal($"http://127.0.0.1:{AppSettings.Current.EditorDoor.Port}/v1", _door.BaseUrl);
        AppSettings.Current.EditorDoor.Enabled = false;
        _door.Apply();
        Assert.False(_door.Running);

        using var blocker = new HttpListener();
        var port = Sandbox.FreePort();
        blocker.Prefixes.Add($"http://127.0.0.1:{port}/");
        blocker.Start();
        AppSettings.Current.EditorDoor.Port = port;
        AppSettings.Current.EditorDoor.Enabled = true;
        _door.Apply();
        Assert.False(_door.Running);
        Assert.NotNull(_door.LastError);
    }
}

public class EditorDoorPureTests
{
    [Theory]
    [InlineData("Qwen3.5 4B", "qwen3.5-4b")]
    [InlineData("  Gemma 3 1B ", "gemma-3-1b")]
    [InlineData("Ñandú (rápido)", "and---r-pido")]
    [InlineData("***", "local")]
    [InlineData("", "local")]
    public void Model_id_is_lowercase_ascii(string name, string id) => Assert.Equal(id, EditorDoor.ModelId(name));

    private static JsonObject Parse(byte[] body) => JsonNode.Parse(body)!.AsObject();

    [Fact]
    public void Thinking_is_turned_off_unless_the_client_says_otherwise()
    {
        var off = Parse(EditorDoor.ThinkingOffByDefault(Encoding.UTF8.GetBytes("{\"messages\":[]}")));
        Assert.False(off["chat_template_kwargs"]!["enable_thinking"]!.GetValue<bool>());
        Assert.NotNull(off["messages"]);

        foreach (var key in new[] { "chat_template_kwargs", "reasoning_effort", "reasoning", "thinking" })
        {
            var original = Encoding.UTF8.GetBytes($"{{\"{key}\":{{}}}}");
            Assert.Same(original, EditorDoor.ThinkingOffByDefault(original));
        }
    }

    [Theory]
    [InlineData("[1,2]")]
    [InlineData("no es json")]
    [InlineData("")]
    [InlineData("\"texto\"")]
    public void Bodies_that_are_not_an_object_pass_through(string text)
    {
        var body = Encoding.UTF8.GetBytes(text);
        Assert.Same(body, EditorDoor.ThinkingOffByDefault(body));
    }
}
