using SocLucia.Localization;

namespace SocLucia.Services;

/// <summary>Una IA del perfil para poder volver a descargarla en otro PC (solo nombre y repo, no los pesos).</summary>
public sealed class CloudModelEntry
{
    public string Name { get; set; } = string.Empty;
    public string Repo { get; set; } = string.Empty;
    public string? License { get; set; }
}

/// <summary>
/// El «perfil» de Lucia que viaja a la nube del usuario: la configuración y la <b>lista</b> de modelos
/// (para re-descargarlos con <see cref="Engine.ModelDownloads"/> en otro PC), nunca los ficheros de
/// pesos. Las claves de API solo se incluyen si el usuario lo pide (y entonces el perfil entero va
/// cifrado con su frase, <see cref="CloudVault"/>); por defecto no salen.
/// </summary>
public sealed class CloudProfile
{
    public int Version { get; set; } = 1;
    public DateTimeOffset SavedAt { get; set; }

    public string? Instructions { get; set; }
    public bool Thinking { get; set; }
    public int ContextTokens { get; set; } = 8192;
    public bool AutoContext { get; set; } = true;
    public bool CompactContext { get; set; } = true;
    public int MaxAnswerTokens { get; set; } = 2048;
    public int AgentMaxActions { get; set; }
    public string? Language { get; set; }
    public double FontSize { get; set; } = 14;
    public bool InternetAccess { get; set; } = true;
    public bool MemoryEnabled { get; set; } = true;
    public bool RemoteOptIn { get; set; }

    public Dictionary<string, string> Permissions { get; set; } = new();
    public List<Mcp.McpServerConfig> McpServers { get; set; } = [];
    public List<string> DisabledSkills { get; set; } = [];
    public List<RemoteModelRef> RemoteModels { get; set; } = [];
    public Dictionary<string, string> ProviderAccountIds { get; set; } = new();
    public List<CloudModelEntry> Models { get; set; } = [];

    /// <summary>Claves de API en claro: presentes SOLO dentro de un perfil cifrado con la frase del usuario.</summary>
    public Dictionary<string, string>? ProviderKeys { get; set; }

    /// <summary>Arma el perfil desde los ajustes. Con <paramref name="includeKeys"/> añade las claves en claro (el perfil debe cifrarse).</summary>
    public static CloudProfile Build(AppSettings s, bool includeKeys)
    {
        var profile = new CloudProfile
        {
            SavedAt = DateTimeOffset.UtcNow,
            Instructions = s.Instructions,
            Thinking = s.Thinking,
            ContextTokens = s.ContextTokens,
            AutoContext = s.AutoContext,
            CompactContext = s.CompactContext,
            MaxAnswerTokens = s.MaxAnswerTokens,
            AgentMaxActions = s.AgentMaxActions,
            Language = s.Language,
            FontSize = s.FontSize,
            InternetAccess = s.InternetAccess,
            MemoryEnabled = s.MemoryEnabled,
            RemoteOptIn = s.RemoteOptIn,
            Permissions = new Dictionary<string, string>(s.Permissions),
            McpServers = s.McpServers.Select(m => new Mcp.McpServerConfig { Name = m.Name, Command = m.Command, Args = [.. m.Args], Env = new(m.Env), Enabled = m.Enabled }).ToList(),
            DisabledSkills = [.. s.DisabledSkills],
            RemoteModels = s.RemoteModels.Select(r => new RemoteModelRef { ProviderId = r.ProviderId, ModelId = r.ModelId, Name = r.Name }).ToList(),
            ProviderAccountIds = new Dictionary<string, string>(s.ProviderAccountIds),
        };

        // La lista de modelos para re-descargar: los instalados y los que estaban a medias, con repo.
        foreach (var m in s.Installed.Where(m => !string.IsNullOrEmpty(m.Repo)))
            profile.Models.Add(new CloudModelEntry { Name = m.Name, Repo = m.Repo!, License = m.License });
        foreach (var d in s.PendingDownloads.Where(d => !string.IsNullOrEmpty(d.Repo)))
            if (!profile.Models.Any(x => string.Equals(x.Repo, d.Repo, StringComparison.OrdinalIgnoreCase)))
                profile.Models.Add(new CloudModelEntry { Name = d.Name, Repo = d.Repo, License = d.License });

        if (includeKeys && s.RemoteProviderKeys.Count > 0)
        {
            profile.ProviderKeys = new Dictionary<string, string>();
            foreach (var (provider, _) in s.RemoteProviderKeys)
            {
                var plain = global::SocLucia.Services.RemoteModels.Key(provider);
                if (plain.Length > 0) profile.ProviderKeys[provider] = plain;
            }
        }
        return profile;
    }

    /// <summary>Aplica el perfil a los ajustes (sin tocar los modelos locales ni los tokens de la nube).</summary>
    public static void Apply(AppSettings s, CloudProfile p)
    {
        s.Instructions = p.Instructions ?? string.Empty;
        s.Thinking = p.Thinking;
        s.ContextTokens = p.ContextTokens;
        s.AutoContext = p.AutoContext;
        s.CompactContext = p.CompactContext;
        s.MaxAnswerTokens = p.MaxAnswerTokens;
        s.AgentMaxActions = p.AgentMaxActions;
        s.FontSize = p.FontSize;
        s.InternetAccess = p.InternetAccess;
        s.MemoryEnabled = p.MemoryEnabled;
        s.RemoteOptIn = p.RemoteOptIn;
        s.Permissions = new Dictionary<string, string>(p.Permissions);
        s.McpServers = p.McpServers ?? [];
        s.DisabledSkills = p.DisabledSkills ?? [];
        s.RemoteModels = p.RemoteModels ?? [];
        s.ProviderAccountIds = new Dictionary<string, string>(p.ProviderAccountIds ?? new());
        if (p.Language is { Length: > 0 }) { s.Language = p.Language; Loc.Use(p.Language); }

        // Las claves solo vienen en un perfil cifrado; se vuelven a cifrar con DPAPI en este PC.
        if (p.ProviderKeys is { Count: > 0 })
            foreach (var (provider, key) in p.ProviderKeys)
                global::SocLucia.Services.RemoteModels.SetKey(provider, key);
    }

    /// <summary>Los modelos del perfil que faltan en este PC (no instalados).</summary>
    public static List<CloudModelEntry> Missing(AppSettings s, CloudProfile p) =>
        (p.Models ?? []).Where(m => !s.Installed.Any(i => string.Equals(i.Repo, m.Repo, StringComparison.OrdinalIgnoreCase))).ToList();
}
