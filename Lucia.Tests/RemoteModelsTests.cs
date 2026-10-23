using System.Net;
using SocLucia.Engine;
using SocLucia.Services;

namespace SocLucia.Tests;

/// <summary>El transporte a servicios compatibles con OpenAI: la dirección, la clave (Bearer) y el id del modelo.</summary>
public class ChatEndpointTests
{
    [Fact]
    public void A_string_endpoint_adds_the_v1_path()
    {
        Endpoint local = "http://127.0.0.1:1234";
        Assert.Equal("http://127.0.0.1:1234/v1", local.BaseUrl);
        Assert.Equal("http://127.0.0.1:1234/v1/chat/completions", local.ChatCompletionsUrl);
        // Un remoto trae ya su base con la versión.
        Assert.Equal("https://api.groq.com/openai/v1/chat/completions", new Endpoint("https://api.groq.com/openai/v1").ChatCompletionsUrl);
    }

    [Fact]
    public async Task Sends_the_key_and_model_and_reads_the_stream()
    {
        using var server = new LocalServer(ctx => LocalServer.Write(ctx, 200, "text/event-stream",
            "data: {\"choices\":[{\"delta\":{\"content\":\"hola\"}}]}\n\ndata: [DONE]\n\n"));
        var endpoint = new Endpoint(server.BaseUrl + "/v1", "SECRET", "my-model");
        var text = string.Empty;
        await foreach (var delta in ChatClient.StreamAsync(endpoint, [new ChatMessage("user", "hi")], thinking: false, maxTokens: 8, tools: null, CancellationToken.None))
            text += delta.Content ?? string.Empty;
        Assert.Equal("hola", text);
        Assert.Equal("/v1/chat/completions", server.Requests[0].Url!.AbsolutePath);
        Assert.Equal("Bearer SECRET", server.Requests[0].Headers["Authorization"]);
        Assert.Contains("\"model\":\"my-model\"", server.Bodies[0]);
    }

    [Fact]
    public async Task Without_a_key_no_authorization_header_is_sent()
    {
        using var server = new LocalServer(ctx => LocalServer.Write(ctx, 200, "text/event-stream", "data: [DONE]\n\n"));
        await foreach (var _ in ChatClient.StreamAsync(new Endpoint(server.BaseUrl + "/v1"), [new ChatMessage("user", "hi")], false, 8, null, CancellationToken.None)) { }
        Assert.Null(server.Requests[0].Headers["Authorization"]);
    }
}

/// <summary>El catálogo de proveedores en línea, las claves cifradas y la prueba de la clave.</summary>
public class RemoteModelsTests
{
    public RemoteModelsTests() => Sandbox.ResetSettings();

    [Fact]
    public void Catalog_has_the_free_openai_compatible_providers()
    {
        foreach (var id in new[] { "groq", "mistral", "cerebras", "openrouter", "siliconflow", "nvidia", "google", "cohere", "together", "fireworks", "cloudflare" })
            Assert.NotNull(RemoteModels.Provider(id));
        Assert.StartsWith("https://api.groq.com/openai/v1", RemoteModels.Provider("groq")!.BaseUrl);
        Assert.True(RemoteModels.Provider("cloudflare")!.NeedsAccountId);
    }

    [Fact]
    public void Keys_are_stored_encrypted_not_in_clear()
    {
        RemoteModels.SetKey("groq", "sk-secret-123");
        Assert.True(RemoteModels.HasKey("groq"));
        Assert.Equal("sk-secret-123", RemoteModels.Key("groq"));
        // En los ajustes solo queda el cifrado DPAPI, nunca el texto.
        var stored = AppSettings.Current.RemoteProviderKeys["groq"];
        Assert.StartsWith("dpapi1:", stored);
        Assert.DoesNotContain("sk-secret-123", stored);
        RemoteModels.SetKey("groq", "");
        Assert.False(RemoteModels.HasKey("groq"));
    }

    [Fact]
    public void Cloudflare_base_url_substitutes_the_account_id()
    {
        RemoteModels.SetAccountId("cloudflare", "acc123");
        Assert.Contains("/accounts/acc123/ai/v1", RemoteModels.BaseUrlFor(RemoteModels.Provider("cloudflare")!));
    }

    [Fact]
    public void Endpoint_for_a_model_carries_provider_base_key_and_model()
    {
        RemoteModels.SetKey("groq", "K");
        var endpoint = RemoteModels.EndpointFor(new RemoteModelRef { ProviderId = "groq", ModelId = "llama-3.3-70b" });
        Assert.Equal(RemoteModels.Provider("groq")!.BaseUrl, endpoint.BaseUrl);
        Assert.Equal("K", endpoint.ApiKey);
        Assert.Equal("llama-3.3-70b", endpoint.ModelId);
    }

    [Fact]
    public async Task Models_list_uses_the_swappable_fetcher()
    {
        var original = RemoteModels.ModelsFetcher;
        RemoteModels.ModelsFetcher = (_, _, _) => Task.FromResult<IReadOnlyList<string>>(["a", "b"]);
        try { Assert.Equal(["a", "b"], await RemoteModels.ModelsAsync(RemoteModels.Provider("groq")!, CancellationToken.None)); }
        finally { RemoteModels.ModelsFetcher = original; }
    }

    [Fact]
    public async Task Test_reports_ok_auth_and_limit_in_clear()
    {
        using var ok = new LocalServer(ctx => LocalServer.Write(ctx, 200, "text/event-stream", "data: {\"choices\":[{\"delta\":{\"content\":\"y\"}}]}\n\ndata: [DONE]\n\n"));
        var okProvider = new RemoteProvider("t", "Test", ok.BaseUrl + "/v1", "http://k", RemoteSignup.NoCard);
        RemoteModels.SetKey("t", "K");
        Assert.True((await RemoteModels.TestAsync(okProvider, "m", CancellationToken.None)).Ok);

        using var denied = new LocalServer(ctx => LocalServer.Write(ctx, 401, "application/json", "{\"error\":{\"message\":\"bad key\"}}"));
        var deniedProvider = new RemoteProvider("t2", "Test2", denied.BaseUrl + "/v1", "http://k", RemoteSignup.NoCard);
        RemoteModels.SetKey("t2", "K");
        Assert.Equal("auth", (await RemoteModels.TestAsync(deniedProvider, "m", CancellationToken.None)).Kind);

        using var limited = new LocalServer(ctx => LocalServer.Write(ctx, 429, "application/json", "{\"error\":{\"message\":\"slow down\"}}"));
        var limitedProvider = new RemoteProvider("t3", "Test3", limited.BaseUrl + "/v1", "http://k", RemoteSignup.NoCard);
        RemoteModels.SetKey("t3", "K");
        Assert.Equal("limit", (await RemoteModels.TestAsync(limitedProvider, "m", CancellationToken.None)).Kind);

        // Sin clave: lo dice sin salir a la red.
        Assert.Equal("auth", (await RemoteModels.TestAsync(new RemoteProvider("t4", "T", "http://x/v1", "http://k", RemoteSignup.NoCard), "m", CancellationToken.None)).Kind);
    }

    [Fact]
    public void Has_any_model_is_true_with_an_active_remote_even_without_a_local_gguf()
    {
        var s = AppSettings.Current;
        Assert.False(s.HasAnyModel);
        s.ActiveRemoteModel = new RemoteModelRef { ProviderId = "groq", ModelId = "m", Name = "m" };
        Assert.True(s.HasAnyModel);
    }
}
