using System.Net.Http;
using System.Text.Json.Nodes;
using SocLucia.Engine;

namespace SocLucia.Services;

/// <summary>Qué hace falta para registrarse en un proveedor (lo que más fricción da).</summary>
public enum RemoteSignup { NoCard, Registration, Phone, Card }

/// <summary>
/// Un proveedor de modelos por internet compatible con OpenAI: su base, dónde sacar la clave y qué pide
/// para registrarse. El modelo remoto es solo {proveedor, base, clave, id de modelo} y usa el mismo
/// transporte que el motor local (<see cref="ChatClient"/>).
/// </summary>
public sealed record RemoteProvider(
    string Id,
    string Name,
    string BaseUrl,
    string KeyUrl,
    RemoteSignup Signup,
    bool ListsModels = true,
    bool NeedsAccountId = false,
    IReadOnlyList<string>? FreeModels = null)
{
    /// <summary>Modelos gratuitos conocidos (ejemplos; la lista real cambia, usa «ver modelos» o escribe el id).</summary>
    public IReadOnlyList<string> Free => FreeModels ?? [];
}

/// <summary>Un modelo remoto que el usuario ha añadido (se guarda en los ajustes; la clave va aparte, cifrada).</summary>
public sealed class RemoteModelRef
{
    public string ProviderId { get; set; } = string.Empty;
    public string ModelId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;

    public string Display => Name.Length > 0 ? Name : ModelId;
}

/// <summary>Cómo fue la prueba de una clave: bien, sin autorización, al límite, modelo no válido o red.</summary>
public sealed record RemoteTestResult(string Kind, string Detail)
{
    public bool Ok => Kind == "ok";
}

/// <summary>
/// El catálogo de proveedores gratuitos compatibles con OpenAI y el manejo de las claves (cifradas con
/// DPAPI vía <see cref="Secrets"/>, nunca en claro en los ajustes). Reutiliza el transporte de
/// <see cref="ChatClient"/>. Fuera de la interfaz para poder probarlo (constitución 8.6).
/// </summary>
public static class RemoteModels
{
    /// <summary>Catálogo de proveedores con capa gratuita permanente y endpoint compatible con OpenAI.</summary>
    public static IReadOnlyList<RemoteProvider> Catalog { get; } =
    [
        new("groq", "Groq", "https://api.groq.com/openai/v1", "https://console.groq.com/keys", RemoteSignup.Registration,
            FreeModels: ["llama-3.3-70b-versatile", "llama-3.1-8b-instant", "gemma2-9b-it", "qwen-2.5-32b"]),
        new("google", "Google AI Studio", "https://generativelanguage.googleapis.com/v1beta/openai", "https://aistudio.google.com/apikey", RemoteSignup.Registration,
            FreeModels: ["gemini-2.0-flash", "gemini-2.0-flash-lite", "gemini-1.5-flash"]),
        new("openrouter", "OpenRouter", "https://openrouter.ai/api/v1", "https://openrouter.ai/keys", RemoteSignup.Registration,
            FreeModels: ["meta-llama/llama-3.3-70b-instruct:free", "deepseek/deepseek-r1:free", "google/gemini-2.0-flash-exp:free", "qwen/qwen-2.5-72b-instruct:free"]),
        new("cerebras", "Cerebras", "https://api.cerebras.ai/v1", "https://cloud.cerebras.ai/platform", RemoteSignup.Registration,
            FreeModels: ["llama-3.3-70b", "llama3.1-8b"]),
        new("nvidia", "NVIDIA NIM", "https://integrate.api.nvidia.com/v1", "https://build.nvidia.com/settings/api-keys", RemoteSignup.Registration,
            FreeModels: ["meta/llama-3.3-70b-instruct", "nvidia/llama-3.1-nemotron-70b-instruct", "qwen/qwen2.5-coder-32b-instruct"]),
        new("mistral", "Mistral AI", "https://api.mistral.ai/v1", "https://console.mistral.ai/api-keys", RemoteSignup.Phone,
            FreeModels: ["mistral-small-latest", "open-mistral-nemo"]),
        new("cohere", "Cohere", "https://api.cohere.ai/compatibility/v1", "https://dashboard.cohere.com/api-keys", RemoteSignup.Registration,
            FreeModels: ["command-r-08-2024", "command-r7b-12-2024"]),
        new("siliconflow", "SiliconFlow", "https://api.siliconflow.cn/v1", "https://cloud.siliconflow.cn/account/ak", RemoteSignup.Phone,
            FreeModels: ["Qwen/Qwen2.5-7B-Instruct", "THUDM/glm-4-9b-chat"]),
        new("together", "Together AI", "https://api.together.xyz/v1", "https://api.together.ai/settings/api-keys", RemoteSignup.Card,
            FreeModels: ["meta-llama/Llama-3.3-70B-Instruct-Turbo-Free"]),
        new("fireworks", "Fireworks AI", "https://api.fireworks.ai/inference/v1", "https://fireworks.ai/account/api-keys", RemoteSignup.Card,
            FreeModels: ["accounts/fireworks/models/llama-v3p1-8b-instruct"]),
        new("cloudflare", "Cloudflare Workers AI", "https://api.cloudflare.com/client/v4/accounts/{account_id}/ai/v1", "https://dash.cloudflare.com/profile/api-tokens", RemoteSignup.Registration, NeedsAccountId: true,
            FreeModels: ["@cf/meta/llama-3.1-8b-instruct", "@cf/qwen/qwen1.5-14b-chat-awq"]),
    ];

    public static RemoteProvider? Provider(string id) =>
        Catalog.FirstOrDefault(p => string.Equals(p.Id, (id ?? string.Empty).Trim(), StringComparison.OrdinalIgnoreCase));

    // ---- claves (cifradas con DPAPI) ----

    public static string Key(string providerId)
        => AppSettings.Current.RemoteProviderKeys.TryGetValue(providerId, out var stored) ? Secrets.Unprotect(stored) : string.Empty;

    public static void SetKey(string providerId, string plain)
    {
        var keys = AppSettings.Current.RemoteProviderKeys;
        if (string.IsNullOrWhiteSpace(plain)) keys.Remove(providerId);
        else keys[providerId] = Secrets.Protect(plain.Trim());
        AppSettings.Current.Save();
    }

    public static bool HasKey(string providerId) => Key(providerId).Length > 0;

    /// <summary>El id de cuenta de Cloudflare (va en la base); no es secreto, se guarda en claro.</summary>
    public static string AccountId(string providerId)
        => AppSettings.Current.ProviderAccountIds.GetValueOrDefault(providerId, string.Empty);

    public static void SetAccountId(string providerId, string accountId)
    {
        var map = AppSettings.Current.ProviderAccountIds;
        if (string.IsNullOrWhiteSpace(accountId)) map.Remove(providerId);
        else map[providerId] = accountId.Trim();
        AppSettings.Current.Save();
    }

    /// <summary>La base del proveedor con el id de cuenta sustituido si lo pide (Cloudflare).</summary>
    public static string BaseUrlFor(RemoteProvider provider)
        => provider.NeedsAccountId ? provider.BaseUrl.Replace("{account_id}", AccountId(provider.Id)) : provider.BaseUrl;

    /// <summary>El destino (base + clave + id de modelo) para preguntarle a un modelo remoto.</summary>
    public static Endpoint EndpointFor(RemoteModelRef model)
    {
        var provider = Provider(model.ProviderId) ?? throw new InvalidOperationException($"unknown provider {model.ProviderId}");
        return new Endpoint(BaseUrlFor(provider), Key(provider.Id), model.ModelId);
    }

    // ---- probar la clave y listar modelos ----

    /// <summary>Cómo se baja la lista de modelos de un proveedor; las pruebas lo sustituyen.</summary>
    public static Func<RemoteProvider, string, CancellationToken, Task<IReadOnlyList<string>>> ModelsFetcher { get; set; } = DefaultFetchModelsAsync;

    private static async Task<IReadOnlyList<string>> DefaultFetchModelsAsync(RemoteProvider provider, string key, CancellationToken cancel)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, $"{BaseUrlFor(provider).TrimEnd('/')}/models");
        if (key.Length > 0) request.Headers.TryAddWithoutValidation("Authorization", "Bearer " + key);
        using var response = await Downloader.Http.SendAsync(request, cancel);
        response.EnsureSuccessStatusCode();
        var json = JsonNode.Parse(await response.Content.ReadAsStringAsync(cancel));
        var data = json?["data"] as JsonArray ?? json?["models"] as JsonArray;
        return data?.OfType<JsonObject>().Select(m => m["id"]?.GetValue<string>() ?? m["name"]?.GetValue<string>() ?? string.Empty)
            .Where(id => id.Length > 0).ToList() ?? [];
    }

    public static async Task<IReadOnlyList<string>> ModelsAsync(RemoteProvider provider, CancellationToken cancel)
    {
        try { return await ModelsFetcher(provider, Key(provider.Id), cancel); }
        catch (OperationCanceledException) { throw; }
        catch (Exception) { return []; }
    }

    /// <summary>Prueba la clave con una completación mínima real; dice en claro si va, no autoriza, está al límite o falla.</summary>
    public static async Task<RemoteTestResult> TestAsync(RemoteProvider provider, string modelId, CancellationToken cancel)
    {
        if (!HasKey(provider.Id)) return new RemoteTestResult("auth", "no key");
        if (provider.NeedsAccountId && AccountId(provider.Id).Length == 0) return new RemoteTestResult("account", "no account id");
        if (string.IsNullOrWhiteSpace(modelId)) return new RemoteTestResult("model", "no model id");
        var endpoint = new Endpoint(BaseUrlFor(provider), Key(provider.Id), modelId.Trim());
        try
        {
            var got = string.Empty;
            await foreach (var delta in ChatClient.StreamAsync(endpoint, [new ChatMessage("user", "Hi")], thinking: false, maxTokens: 1, tools: null, cancel))
                got += delta.Content ?? string.Empty;
            return new RemoteTestResult("ok", got);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            var code = System.Text.RegularExpressions.Regex.Match(ex.Message, @"\b(\d{3})\b").Value;
            var kind = code switch
            {
                "401" or "403" => "auth",
                "429" => "limit",
                "400" or "404" => "model",
                _ => "network",
            };
            return new RemoteTestResult(kind, ex.Message);
        }
    }
}
