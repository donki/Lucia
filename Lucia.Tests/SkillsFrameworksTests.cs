using System.IO.Compression;
using System.Text;
using System.Text.Json.Nodes;
using SocLucia.Agent;
using SocLucia.Mcp;
using SocLucia.Services;

namespace SocLucia.Tests;

/// <summary>Las habilidades (skills): leer el SKILL.md, instalarlas desde carpeta/zip/URL y las herramientas de la IA.</summary>
public class SkillStoreTests
{
    public SkillStoreTests() => Sandbox.ResetSettings();

    private const string Sample = "---\nname: Mi habilidad\ndescription: Hace algo útil\nlicense: MIT\n---\n\n# Título\n\nPaso uno.\nPaso dos.";

    [Fact]
    public void Parse_reads_name_description_and_body()
    {
        var (name, description, body) = SkillStore.Parse(Sample, "carpeta");
        Assert.Equal("Mi habilidad", name);
        Assert.Equal("Hace algo útil", description);
        Assert.StartsWith("# Título", body);
        Assert.Contains("Paso dos.", body);
    }

    [Fact]
    public void Parse_without_header_uses_fallback_name_and_first_line()
    {
        var (name, description, body) = SkillStore.Parse("# Cómo hacer café\n\nMolé el grano.", "cafe");
        Assert.Equal("cafe", name);
        Assert.Equal("Cómo hacer café", description);
        Assert.StartsWith("# Cómo hacer café", body);
    }

    [Theory]
    [InlineData("MIT", true)]
    [InlineData("Apache-2.0", true)]
    [InlineData("", true)]
    [InlineData("CC-BY-NC-4.0", false)]
    [InlineData("GPL-3.0", false)]
    [InlineData("Proprietary", false)]
    public void LicenseOk_rejects_non_commercial(string license, bool ok) => Assert.Equal(ok, SkillStore.LicenseOk(license));

    [Fact]
    public async Task Install_from_folder_then_list_read_enable_and_remove()
    {
        var folder = Sandbox.NewFolder("skillsrc");
        File.WriteAllText(Path.Combine(folder, "SKILL.md"), Sample);

        var (skill, message) = await SkillStore.InstallAsync(folder, CancellationToken.None);
        Assert.NotNull(skill);
        Assert.Contains("installed", message);
        Assert.Equal("Mi habilidad", skill!.Name);
        Assert.True(skill.Enabled);

        // La IA la lista, la carga y la ve en el prompt.
        Assert.Contains("Mi habilidad", SkillStore.ListText());
        Assert.Contains("Paso dos.", SkillStore.Read("Mi habilidad"));
        Assert.Contains("Mi habilidad", SkillStore.Prompt());

        // Apagarla la quita del prompt pero no del disco.
        SkillStore.SetEnabled("Mi habilidad", false);
        Assert.DoesNotContain("Mi habilidad", SkillStore.Prompt());
        Assert.Contains(AppSettings.Current.DisabledSkills, d => d == "Mi habilidad");
        Assert.Single(SkillStore.All());

        SkillStore.Remove("Mi habilidad");
        Assert.Empty(SkillStore.All());
        Assert.Null(SkillStore.Read("Mi habilidad"));
    }

    [Fact]
    public async Task Install_from_zip_and_from_url()
    {
        // .zip local con la habilidad en una subcarpeta
        var src = Sandbox.NewFolder("ziproot");
        var inner = Path.Combine(src, "mi-skill");
        Directory.CreateDirectory(inner);
        File.WriteAllText(Path.Combine(inner, "SKILL.md"), Sample);
        var zip = Path.Combine(Sandbox.NewFolder("zipout"), "skill.zip");
        ZipFile.CreateFromDirectory(src, zip);

        var (fromZip, _) = await SkillStore.InstallAsync(zip, CancellationToken.None);
        Assert.NotNull(fromZip);
        Assert.Equal("Mi habilidad", fromZip!.Name);
        SkillStore.Remove("Mi habilidad");

        // URL: el Fetcher devuelve los bytes del mismo zip (sin red)
        var original = SkillStore.Fetcher;
        SkillStore.Fetcher = (_, _) => Task.FromResult(File.ReadAllBytes(zip));
        try
        {
            var (fromUrl, _) = await SkillStore.InstallAsync("https://example.com/skill.zip", CancellationToken.None);
            Assert.NotNull(fromUrl);
            Assert.Equal("Mi habilidad", fromUrl!.Name);
        }
        finally { SkillStore.Fetcher = original; }
    }

    [Fact]
    public async Task Install_rejects_a_non_commercial_license()
    {
        var folder = Sandbox.NewFolder("ncskill");
        File.WriteAllText(Path.Combine(folder, "SKILL.md"), "---\nname: No comercial\ndescription: x\nlicense: CC-BY-NC-4.0\n---\nCuerpo.");
        var (skill, message) = await SkillStore.InstallAsync(folder, CancellationToken.None);
        Assert.Null(skill);
        Assert.StartsWith("[error]", message);
        Assert.Empty(SkillStore.All());
    }

    [Fact]
    public async Task Install_without_skill_md_fails_cleanly()
    {
        var folder = Sandbox.NewFolder("empty");
        File.WriteAllText(Path.Combine(folder, "readme.txt"), "nada");
        var (skill, message) = await SkillStore.InstallAsync(folder, CancellationToken.None);
        Assert.Null(skill);
        Assert.Contains("no SKILL.md", message);
    }

    [Fact]
    public async Task Tools_list_use_and_install_skills()
    {
        var folder = Sandbox.NewFolder("toolskill");
        File.WriteAllText(Path.Combine(folder, "SKILL.md"), Sample);

        var install = await ToolBox.Find(ToolBox.InstallSkill)!.Run(new JsonObject { ["source"] = folder }, CancellationToken.None);
        Assert.Contains("installed", install);
        Assert.Contains("Mi habilidad", await ToolBox.Find(ToolBox.ListSkills)!.Run(new JsonObject(), CancellationToken.None));
        var used = await ToolBox.Find(ToolBox.UseSkill)!.Run(new JsonObject { ["name"] = "Mi habilidad" }, CancellationToken.None);
        Assert.Contains("[skill: Mi habilidad]", used);
        Assert.Contains("Paso dos.", used);
        Assert.StartsWith("[error]", await ToolBox.Find(ToolBox.UseSkill)!.Run(new JsonObject { ["name"] = "no existe" }, CancellationToken.None));
        SkillStore.Remove("Mi habilidad");
    }

    [Fact]
    public void Context_prompt_lists_enabled_skills_unless_mcp_denied()
    {
        var folder = Sandbox.NewFolder("ctxskill");
        File.WriteAllText(Path.Combine(folder, "SKILL.md"), Sample);
        SkillStore.InstallAsync(folder, CancellationToken.None).Wait();
        var s = AppSettings.Current;
        Assert.Contains("Mi habilidad", ToolBox.ContextPrompt(s, "hola"));
        s.SetPermission(Resource.Mcp, Permission.Deny);
        Assert.DoesNotContain("Mi habilidad", ToolBox.ContextPrompt(s, "hola"));
        SkillStore.Remove("Mi habilidad");
    }
}

/// <summary>Los frameworks para IA: el catálogo (REA), preparar el motor y registrar el framework como servidor MCP.</summary>
public class FrameworksTests
{
    private static string FakeExe => Path.Combine(AppContext.BaseDirectory, "FakeMcp.exe");

    public FrameworksTests() => Sandbox.ResetSettings();

    [Fact]
    public void Catalog_has_rea_registered_as_npx_mcp_with_compact_schema()
    {
        var rea = Frameworks.ById("rea");
        Assert.NotNull(rea);
        Assert.Equal("npx", rea!.Command);
        Assert.Equal(["-y", "rea-agents@latest", "mcp"], rea.Args);
        Assert.Equal("compact", rea.Env["REA_MCP_INPUT_SCHEMA_PROFILE"]);
        Assert.Equal(FrameworkRuntime.Node, rea.Runtime);
        Assert.Equal("MIT", rea.License);
        Assert.Contains("rea", Frameworks.ListText());
    }

    [Fact]
    public async Task Detect_reads_the_version()
    {
        var original = Frameworks.CommandRunner;
        Frameworks.CommandRunner = (_, _) => Task.FromResult((0, "v22.11.0"));
        try
        {
            var status = await Frameworks.DetectAsync(FrameworkRuntime.Node, CancellationToken.None);
            Assert.True(status.Present);
            Assert.Equal("22.11.0", status.Version);
        }
        finally { Frameworks.CommandRunner = original; }
    }

    [Fact]
    public async Task Ensure_runtime_present_old_and_installed()
    {
        var original = Frameworks.CommandRunner;
        try
        {
            Frameworks.CommandRunner = (_, _) => Task.FromResult((0, "v22.11.0"));
            Assert.Contains("already installed", await Frameworks.EnsureRuntimeAsync(FrameworkRuntime.Node, CancellationToken.None));

            Frameworks.CommandRunner = (_, _) => Task.FromResult((0, "v18.0.0"));
            Assert.Contains("22 or newer", await Frameworks.EnsureRuntimeAsync(FrameworkRuntime.Node, CancellationToken.None));

            // Ausente y luego instalado por winget: la versión aparece tras la instalación.
            var installed = false;
            Frameworks.CommandRunner = (cmd, _) =>
            {
                if (cmd.Contains("winget")) { installed = true; return Task.FromResult((0, "ok")); }
                return Task.FromResult(installed ? (0, "v22.5.0") : (1, "not recognized"));
            };
            Assert.Contains("installed", await Frameworks.EnsureRuntimeAsync(FrameworkRuntime.Node, CancellationToken.None));

            // Ausente y winget no lo arregla: error claro.
            Frameworks.CommandRunner = (_, _) => Task.FromResult((1, "not recognized"));
            Assert.StartsWith("[error]", await Frameworks.EnsureRuntimeAsync(FrameworkRuntime.Node, CancellationToken.None));
        }
        finally { Frameworks.CommandRunner = original; }
    }

    [Fact]
    public async Task Install_framework_prepares_runtime_registers_mcp_and_installs_skill()
    {
        var runner = Frameworks.CommandRunner;
        var catalog = Frameworks.Catalog;
        Frameworks.CommandRunner = (_, _) => Task.FromResult((0, "v22.11.0")); // node presente
        var fake = new LlmFramework("fakefw", "Fake FW", "de prueba", FrameworkRuntime.Node, "fakefw",
            FakeExe, [], new Dictionary<string, string>(), "MIT", "https://example.com",
            SkillName: "Fake skill", SkillMarkdown: "---\nname: Fake skill\ndescription: prueba\nlicense: MIT\n---\nCuerpo.");
        Frameworks.Catalog = [fake];
        try
        {
            var message = await Frameworks.InstallAsync("fakefw", CancellationToken.None);
            Assert.Contains("is installed", message);
            Assert.Contains(AppSettings.Current.McpServers, s => s.Name == "fakefw");
            Assert.NotNull(McpManager.ConnectionOf("fakefw"));
            Assert.Contains("mcp__fakefw__echo", ToolBox.Definitions(AppSettings.Current).Select(d => d!["function"]!["name"]!.GetValue<string>()));
            // La habilidad del framework quedó instalada.
            Assert.NotNull(SkillStore.Find("Fake skill"));
        }
        finally
        {
            McpManager.DisconnectAll();
            Frameworks.CommandRunner = runner;
            Frameworks.Catalog = catalog;
            Sandbox.ResetSettings();
        }
    }

    [Fact]
    public async Task Install_framework_unknown_id_is_an_error()
        => Assert.StartsWith("[error]", await ToolBox.Find(ToolBox.InstallFramework)!.Run(new JsonObject { ["id"] = "no-existe" }, CancellationToken.None));

    [Fact]
    public async Task Ensure_runtime_tool_validates_the_runtime_name()
    {
        var original = Frameworks.CommandRunner;
        Frameworks.CommandRunner = (_, _) => Task.FromResult((0, "v22.0.0"));
        try
        {
            Assert.Contains("already installed", await ToolBox.Find(ToolBox.EnsureRuntime)!.Run(new JsonObject { ["runtime"] = "node" }, CancellationToken.None));
            Assert.StartsWith("[error]", await ToolBox.Find(ToolBox.EnsureRuntime)!.Run(new JsonObject { ["runtime"] = "cobol" }, CancellationToken.None));
        }
        finally { Frameworks.CommandRunner = original; }
    }
}
