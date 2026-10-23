using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;
using SocLucia.Localization;

namespace SocLucia.Services;

/// <summary>Cómo fue una restauración: bien, no hay perfil, hace falta la frase o la frase no vale.</summary>
public sealed record RestoreResult(string Kind, CloudProfile? Profile = null, IReadOnlyList<CloudModelEntry>? Missing = null)
{
    public static readonly RestoreResult None = new("none");
    public static readonly RestoreResult NeedsPassphrase = new("needs_passphrase");
    public static readonly RestoreResult WrongPassphrase = new("wrong_passphrase");
    public bool Ok => Kind == "ok";
}

/// <summary>
/// Cuenta y sincronización: entra con Google o Microsoft (navegador + loopback PKCE, reutilizando el
/// registro de RC Manager), guarda el <see cref="CloudProfile"/> en la carpeta de la app del Drive u
/// OneDrive del usuario y lo restaura en otro PC. Guardar y restaurar son explícitos (no hay sincronía
/// automática). Las claves de API no salen salvo que el usuario lo pida con una frase. Fuera de la
/// interfaz para poder probarlo sin red ni OAuth reales (constitución 8.6).
/// </summary>
public sealed class CloudSync : IDisposable
{
    private readonly AppSettings _settings;
    private readonly HttpClient _http;

    public CloudSync(AppSettings settings) : this(settings, new HttpClient { Timeout = TimeSpan.FromSeconds(60) }) { }

    internal CloudSync(AppSettings settings, HttpClient http)
    {
        _settings = settings;
        _http = http;
    }

    private static readonly JsonSerializerOptions Json = new() { DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull };

    /// <summary>Para las pruebas: usa esta nube en vez de construirla de los tokens.</summary>
    internal Func<ICloudDrive>? DriveOverride { get; set; }

    /// <summary>Para las pruebas: cómo se lanza la descarga de un modelo que falta.</summary>
    public static Action<CloudModelEntry> ModelDownloader { get; set; } =
        e => Engine.ModelDownloads.Start(new Engine.CatalogModel(e.Name, e.Repo, 0, e.License ?? "?", "CatalogHf"));

    public static bool IsAvailable(CloudStorageMode mode) => mode switch
    {
        CloudStorageMode.GoogleDrive => OAuthSecrets.GoogleClientId.Length > 0,
        CloudStorageMode.OneDrive => OAuthSecrets.MicrosoftClientId.Length > 0,
        _ => true,
    };

    public OAuthClient ClientFor(CloudStorageMode mode) => new(_http, mode switch
    {
        CloudStorageMode.GoogleDrive => new OAuthProvider("Google", OAuthSecrets.GoogleClientId, OAuthSecrets.GoogleClientSecret,
            "https://accounts.google.com/o/oauth2/v2/auth", "https://oauth2.googleapis.com/token", GoogleDrive.Scopes,
            "&access_type=offline&prompt=consent"),
        CloudStorageMode.OneDrive => new OAuthProvider("Microsoft", OAuthSecrets.MicrosoftClientId, string.Empty,
            "https://login.microsoftonline.com/common/oauth2/v2.0/authorize", "https://login.microsoftonline.com/common/oauth2/v2.0/token", OneDrive.Scopes,
            "&prompt=select_account"),
        _ => throw new InvalidOperationException("Sin proveedor."),
    });

    /// <summary>Entra con la cuenta del proveedor y deja los tokens guardados (DPAPI). Devuelve el correo.</summary>
    public async Task<string> SignInAsync(CloudStorageMode mode, CancellationToken cancel = default)
    {
        var tokens = await ClientFor(mode).SignInAsync(cancel).ConfigureAwait(false);
        // Google enseña cada permiso como una casilla: si la de Drive no se marca, la entrada va bien
        // pero el token no sirve. Mejor decirlo aquí que un 403 al guardar.
        var required = mode == CloudStorageMode.GoogleDrive ? "https://www.googleapis.com/auth/drive.appdata" : "Files.ReadWrite.AppFolder";
        if (tokens.Scope.Length > 0 && !tokens.Has(required))
            throw new InvalidOperationException(Loc.Get(mode == CloudStorageMode.GoogleDrive ? "GoogleScopeMissing" : "MicrosoftScopeMissing"));

        _settings.CloudStorage = mode;
        _settings.CloudTokens = tokens;
        _settings.CloudAccountEmail = OAuthClient.EmailOf(tokens);
        _settings.Save();
        return _settings.CloudAccountEmail;
    }

    public void SignOut()
    {
        _settings.CloudStorage = CloudStorageMode.Local;
        _settings.CloudTokens = null;
        _settings.CloudAccountEmail = string.Empty;
        _settings.Save();
    }

    private ICloudDrive Drive()
    {
        if (DriveOverride is not null) return DriveOverride();
        var mode = _settings.CloudStorage;
        var client = ClientFor(mode);
        Func<CancellationToken, Task<string>> token = async ct =>
        {
            var tokens = _settings.CloudTokens ?? throw new InvalidOperationException(Loc.Get("CloudNotSignedIn"));
            var fresh = await client.RefreshIfNeededAsync(tokens, ct).ConfigureAwait(false);
            if (!ReferenceEquals(fresh, tokens)) { _settings.CloudTokens = fresh; _settings.Save(); }
            return fresh.AccessToken;
        };
        return mode == CloudStorageMode.GoogleDrive ? new GoogleDrive(_http, token) : new OneDrive(_http, token);
    }

    /// <summary>Guarda el perfil en la nube. Con <paramref name="includeKeys"/> incluye las claves y cifra con la frase.</summary>
    public async Task SaveAsync(bool includeKeys, string? passphrase, CancellationToken cancel = default)
    {
        if (includeKeys && string.IsNullOrEmpty(passphrase))
            throw new InvalidOperationException(Loc.Get("CloudNeedKeysPassphrase"));
        var profile = CloudProfile.Build(_settings, includeKeys);
        var json = JsonSerializer.Serialize(profile, Json);
        var payload = includeKeys ? CloudVault.Encrypt(json, passphrase!) : json;
        await Drive().UploadAsync(payload, cancel).ConfigureAwait(false);
        _settings.CloudIncludeKeys = includeKeys;
        _settings.Save();
    }

    /// <summary>Baja el perfil, lo aplica a los ajustes y devuelve los modelos que faltan en este PC.</summary>
    public async Task<RestoreResult> RestoreAsync(string? passphrase, CancellationToken cancel = default)
    {
        var remote = await Drive().DownloadAsync(cancel).ConfigureAwait(false);
        if (remote is null) return RestoreResult.None;

        var text = remote.Value.Content;
        if (CloudVault.IsEncrypted(text))
        {
            if (string.IsNullOrEmpty(passphrase)) return RestoreResult.NeedsPassphrase;
            try { text = CloudVault.Decrypt(text, passphrase); }
            catch (CryptographicException) { return RestoreResult.WrongPassphrase; }
        }

        var profile = JsonSerializer.Deserialize<CloudProfile>(text) ?? throw new InvalidOperationException("perfil vacío");
        CloudProfile.Apply(_settings, profile);
        _settings.Save();
        return new RestoreResult("ok", profile, CloudProfile.Missing(_settings, profile));
    }

    /// <summary>Lanza la descarga de los modelos que faltan (los que no se estén bajando ya). Devuelve cuántos.</summary>
    public static int DownloadMissing(IEnumerable<CloudModelEntry> missing)
    {
        var n = 0;
        foreach (var m in missing)
        {
            if (m.Repo.Length == 0 || Engine.ModelDownloads.IsDownloading(m.Repo)) continue;
            ModelDownloader(m);
            n++;
        }
        return n;
    }

    public void Dispose() => _http.Dispose();
}
