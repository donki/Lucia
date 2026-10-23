using System.Text.Json.Nodes;
using SocLucia.Agent;
using SocLucia.Mcp;
using SocLucia.Services;

namespace SocLucia.Tests;

/// <summary>Los catálogos navegables: modelos gratuitos por proveedor y el catálogo de frameworks/MCP.</summary>
public class CatalogsTests
{
    private static string FakeExe => Path.Combine(AppContext.BaseDirectory, "FakeMcp.exe");

    public CatalogsTests() => Sandbox.ResetSettings();

    [Fact]
    public void Providers_list_example_free_models()
    {
        Assert.NotEmpty(RemoteModels.Provider("groq")!.Free);
        Assert.Contains("gemini-2.0-flash", RemoteModels.Provider("google")!.Free);
        Assert.Contains(RemoteModels.Provider("openrouter")!.Free, m => m.EndsWith(":free"));
    }

    [Fact]
    public void Frameworks_catalog_has_the_mcp_servers_and_re_tools()
    {
        foreach (var id in new[] { "rea", "mcp-filesystem", "mcp-fetch", "mcp-git", "mcp-memory", "mcp-sequential-thinking", "mcp-sqlite", "playwright", "context7", "serena", "github-mcp", "ghidra", "rizin", "whisper-cpp" })
            Assert.NotNull(Frameworks.ById(id));
        Assert.True(Frameworks.ById("mcp-filesystem")!.Installable);
        Assert.False(Frameworks.ById("github-mcp")!.Installable);   // Docker + token: no es de un clic
        Assert.False(Frameworks.ById("ghidra")!.Installable);
        Assert.Contains("filesystem", Frameworks.ListText());
    }

    [Fact]
    public async Task Install_refuses_a_non_one_click_framework()
    {
        var runner = Frameworks.CommandRunner;
        Frameworks.CommandRunner = (_, _) => Task.FromResult((0, "v22.0.0")); // node presente, para llegar a la comprobación
        try
        {
            var message = await Frameworks.InstallAsync("github-mcp", CancellationToken.None);
            Assert.StartsWith("[error]", message);
            Assert.DoesNotContain(AppSettings.Current.McpServers, s => s.Name == "github");
        }
        finally { Frameworks.CommandRunner = runner; }
    }

    [Fact]
    public async Task Install_substitutes_the_workfolder_token_in_the_args()
    {
        var runner = Frameworks.CommandRunner;
        var catalog = Frameworks.Catalog;
        Frameworks.CommandRunner = (_, _) => Task.FromResult((0, "v22.0.0"));
        var fake = new LlmFramework("fakefs", "FS", "de prueba", FrameworkRuntime.Node, "fakefs", FakeExe, ["{workfolder}"], new(), "MIT", "https://x");
        Frameworks.Catalog = [fake];
        try
        {
            var message = await Frameworks.InstallAsync("fakefs", CancellationToken.None);
            Assert.Contains("is installed", message);
            var cfg = AppSettings.Current.McpServers.First(s => s.Name == "fakefs");
            Assert.Contains(AppSettings.Current.WorkFolder, cfg.Args);
            Assert.DoesNotContain("{workfolder}", cfg.Args);
        }
        finally { McpManager.DisconnectAll(); Frameworks.CommandRunner = runner; Frameworks.Catalog = catalog; Sandbox.ResetSettings(); }
    }
}
