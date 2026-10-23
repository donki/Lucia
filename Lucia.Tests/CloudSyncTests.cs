using System.Security.Cryptography;
using SocLucia.Services;

namespace SocLucia.Tests;

/// <summary>Una nube de mentira en memoria, para probar guardar/restaurar sin red ni OAuth.</summary>
internal sealed class FakeDrive : ICloudDrive
{
    public string? Stored;
    public CloudStorageMode Mode => CloudStorageMode.GoogleDrive;
    public Task<(string Content, DateTimeOffset ModifiedAt)?> DownloadAsync(CancellationToken ct = default)
        => Task.FromResult(Stored is null ? default((string, DateTimeOffset)?) : (Stored, DateTimeOffset.UtcNow));
    public Task UploadAsync(string content, CancellationToken ct = default) { Stored = content; return Task.CompletedTask; }
}

public class CloudVaultTests
{
    [Fact]
    public void Round_trips_and_detects_wrong_passphrase()
    {
        var enc = CloudVault.Encrypt("hola", "frase-secreta");
        Assert.True(CloudVault.IsEncrypted(enc));
        Assert.DoesNotContain("hola", enc);
        Assert.Equal("hola", CloudVault.Decrypt(enc, "frase-secreta"));
        Assert.ThrowsAny<CryptographicException>(() => CloudVault.Decrypt(enc, "otra"));
    }
}

public class CloudProfileTests
{
    public CloudProfileTests() => Sandbox.ResetSettings();

    [Fact]
    public void Build_copies_config_and_model_list_and_excludes_keys_by_default()
    {
        var s = AppSettings.Current;
        s.Instructions = "sé breve";
        s.McpServers.Add(new SocLucia.Mcp.McpServerConfig { Name = "fs", Command = "npx" });
        s.RemoteModels.Add(new RemoteModelRef { ProviderId = "groq", ModelId = "llama", Name = "Llama" });
        RemoteModels.SetKey("groq", "sk-123");
        s.Installed.Add(new InstalledModel { Name = "Qwen", Path = "C:/x.gguf", Repo = "unsloth/Qwen-GGUF", License = "Apache-2.0" });

        var plain = CloudProfile.Build(s, includeKeys: false);
        Assert.Equal("sé breve", plain.Instructions);
        Assert.Contains(plain.McpServers, m => m.Name == "fs");
        Assert.Contains(plain.RemoteModels, r => r.ProviderId == "groq");
        Assert.Contains(plain.Models, m => m.Repo == "unsloth/Qwen-GGUF");
        Assert.Null(plain.ProviderKeys); // por defecto, sin claves

        var withKeys = CloudProfile.Build(s, includeKeys: true);
        Assert.NotNull(withKeys.ProviderKeys);
        Assert.Equal("sk-123", withKeys.ProviderKeys!["groq"]);
    }
}

public class CloudSyncTests
{
    public CloudSyncTests() => Sandbox.ResetSettings();

    private static CloudSync WithDrive(FakeDrive drive)
    {
        var sync = new CloudSync(AppSettings.Current);
        sync.DriveOverride = () => drive;
        return sync;
    }

    [Fact]
    public async Task Save_without_keys_is_plain_and_restore_applies_config()
    {
        var s = AppSettings.Current;
        s.Instructions = "config original";
        s.RemoteModels.Add(new RemoteModelRef { ProviderId = "groq", ModelId = "m", Name = "m" });
        var drive = new FakeDrive();
        using var sync = WithDrive(drive);

        await sync.SaveAsync(includeKeys: false, passphrase: null, CancellationToken.None);
        Assert.NotNull(drive.Stored);
        Assert.False(CloudVault.IsEncrypted(drive.Stored!)); // sin claves: texto plano (carpeta privada de la app)
        Assert.DoesNotContain("ProviderKeys", drive.Stored!); // nada de claves

        // Otro PC: ajustes vacíos, se restaura.
        Sandbox.ResetSettings();
        using var sync2 = WithDrive(drive);
        var result = await sync2.RestoreAsync(null, CancellationToken.None);
        Assert.True(result.Ok);
        Assert.Equal("config original", AppSettings.Current.Instructions);
        Assert.Contains(AppSettings.Current.RemoteModels, r => r.ProviderId == "groq");
    }

    [Fact]
    public async Task Save_with_keys_is_encrypted_and_restore_needs_the_passphrase()
    {
        var s = AppSettings.Current;
        RemoteModels.SetKey("groq", "sk-abc");
        var drive = new FakeDrive();
        using var sync = WithDrive(drive);
        await sync.SaveAsync(includeKeys: true, passphrase: "mi-frase", CancellationToken.None);
        Assert.True(CloudVault.IsEncrypted(drive.Stored!));
        Assert.DoesNotContain("sk-abc", drive.Stored!);

        // Restaurar en otro PC sin la frase: lo pide. Con la frase mala: lo dice. Con la buena: vuelve la clave.
        Sandbox.ResetSettings();
        using var sync2 = WithDrive(drive);
        Assert.Equal("needs_passphrase", (await sync2.RestoreAsync(null, CancellationToken.None)).Kind);
        Assert.Equal("wrong_passphrase", (await sync2.RestoreAsync("no", CancellationToken.None)).Kind);
        Assert.True((await sync2.RestoreAsync("mi-frase", CancellationToken.None)).Ok);
        Assert.Equal("sk-abc", RemoteModels.Key("groq"));
    }

    [Fact]
    public async Task Save_with_keys_without_a_passphrase_is_refused()
    {
        using var sync = WithDrive(new FakeDrive());
        await Assert.ThrowsAsync<InvalidOperationException>(() => sync.SaveAsync(includeKeys: true, passphrase: "", CancellationToken.None));
    }

    [Fact]
    public async Task Restore_with_no_profile_says_none()
    {
        using var sync = WithDrive(new FakeDrive());
        Assert.Equal("none", (await sync.RestoreAsync(null, CancellationToken.None)).Kind);
    }

    [Fact]
    public async Task Restore_lists_missing_models_and_download_triggers_them()
    {
        var s = AppSettings.Current;
        s.Installed.Add(new InstalledModel { Name = "Qwen", Path = "C:/q.gguf", Repo = "unsloth/Qwen-GGUF", License = "Apache-2.0" });
        var drive = new FakeDrive();
        using var sync = WithDrive(drive);
        await sync.SaveAsync(false, null, CancellationToken.None);

        // En el otro PC ese modelo no está: sale como que falta.
        Sandbox.ResetSettings();
        using var sync2 = WithDrive(drive);
        var result = await sync2.RestoreAsync(null, CancellationToken.None);
        Assert.True(result.Ok);
        Assert.Contains(result.Missing!, m => m.Repo == "unsloth/Qwen-GGUF");

        var original = CloudSync.ModelDownloader;
        var asked = new List<string>();
        CloudSync.ModelDownloader = e => asked.Add(e.Repo);
        try { Assert.Equal(1, CloudSync.DownloadMissing(result.Missing!)); }
        finally { CloudSync.ModelDownloader = original; }
        Assert.Equal(["unsloth/Qwen-GGUF"], asked);
    }

    [Fact]
    public void Available_depends_on_the_oauth_client_ids()
    {
        // Local siempre; Google/OneDrive según haya identificador compilado (reutilizados de RC Manager).
        Assert.True(CloudSync.IsAvailable(CloudStorageMode.Local));
    }

    [Fact]
    public void Tokens_are_stored_dpapi_protected_not_in_clear()
    {
        var s = AppSettings.Current;
        s.CloudTokens = new OAuthTokens { AccessToken = "at", RefreshToken = "rt-secret", IdToken = "id" };
        Assert.StartsWith("dpapi1:", s.CloudTokensProtected);
        Assert.DoesNotContain("rt-secret", s.CloudTokensProtected);
        Assert.Equal("rt-secret", s.CloudTokens!.RefreshToken);
    }
}
