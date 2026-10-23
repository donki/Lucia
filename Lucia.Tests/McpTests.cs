using System.Text.Json.Nodes;
using SocLucia.Agent;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using SocLucia.Localization;
using SocLucia.Mcp;
using SocLucia.Services;

namespace SocLucia.Tests;

public class McpUnitTests
{
    [Theory]
    [InlineData("npx -y @modelcontextprotocol/server-filesystem C:/Users/me", "npx", new[] { "-y", "@modelcontextprotocol/server-filesystem", "C:/Users/me" })]
    [InlineData("uvx mcp-server-fetch", "uvx", new[] { "mcp-server-fetch" })]
    [InlineData("\"C:/Program Files/app.exe\" --mcp \"a b\"", "C:/Program Files/app.exe", new[] { "--mcp", "a b" })]
    [InlineData("   ", "", new string[0])]
    public void Parse_command_line_respects_quotes(string line, string command, string[] args)
    {
        var (c, a) = McpServerConfig.ParseCommandLine(line);
        Assert.Equal(command, c);
        Assert.Equal(args, a);
    }

    [Fact]
    public void Command_line_round_trips_with_quotes()
        => Assert.Equal("app --path \"a b\"", new McpServerConfig { Command = "app", Args = ["--path", "a b"] }.CommandLine);

    [Fact]
    public void Tool_name_is_namespaced_and_slugged()
        => Assert.Equal("mcp__my_server__echo", McpManager.ToolName("My Server!", "echo"));

    [Fact]
    public void Clean_schema_fills_gaps_and_drops_meta()
    {
        var clean = McpClient.CleanSchema((JsonObject)JsonNode.Parse("""{"$schema":"x","properties":{"t":{"type":"string"}}}""")!);
        Assert.Equal("object", clean["type"]!.GetValue<string>());
        Assert.Null(clean["$schema"]);
        Assert.NotNull(clean["properties"]);
        Assert.Equal("object", McpClient.CleanSchema(null)["type"]!.GetValue<string>());
    }

    [Fact]
    public void Text_of_result_joins_text_parts()
    {
        var content = (JsonArray)JsonNode.Parse("""[{"type":"text","text":"a"},{"type":"image","data":"x"},{"type":"text","text":"b"}]""")!;
        Assert.Equal("a\n[image]\nb", McpClient.TextOf(content));
        Assert.Equal(string.Empty, McpClient.TextOf(null));
    }
}

/// <summary>El cliente y el gestor MCP contra un servidor de mentira (FakeMcp, que habla el protocolo por stdio).</summary>
public class McpServerTests
{
    private static string FakeExe => Path.Combine(AppContext.BaseDirectory, "FakeMcp.exe");

    [Fact]
    public async Task Cliente_conecta_lista_y_ejecuta_herramientas()
    {
        using var client = new McpClient("fake");
        var info = await client.StartAsync(FakeExe, [], null, CancellationToken.None);
        Assert.Equal("fake-mcp", info.Name);
        Assert.Equal("1.0", info.Version);
        var tools = await client.ListToolsAsync(CancellationToken.None);
        Assert.Equal(["echo", "boom"], tools.Select(t => t.Name).ToArray());
        Assert.Equal("object", tools[0].Schema["type"]!.GetValue<string>());
        Assert.Null(tools[0].Schema["$schema"]);   // limpiado
        Assert.Equal("HOLA", await client.CallToolAsync("echo", new JsonObject { ["text"] = "hola" }, CancellationToken.None));
        Assert.StartsWith("[error] kaboom", await client.CallToolAsync("boom", new JsonObject(), CancellationToken.None));
    }

    [Fact]
    public async Task Un_comando_que_no_existe_lo_dice_claro()
    {
        using var client = new McpClient("roto");
        var ex = await Assert.ThrowsAsync<McpException>(() => client.StartAsync("no-existe-este-comando-xyz", [], null, CancellationToken.None));
        Assert.Contains("no-existe-este-comando-xyz", ex.Message);
    }

    [Fact]
    public async Task Si_el_servidor_se_cierra_las_peticiones_fallan_sin_colgarse()
    {
        Environment.SetEnvironmentVariable("FAKE_MCP_EXIT", "0");
        try
        {
            using var client = new McpClient("vacio");
            await Assert.ThrowsAnyAsync<Exception>(() => client.StartAsync(FakeExe, [], null, CancellationToken.None));
        }
        finally { Environment.SetEnvironmentVariable("FAKE_MCP_EXIT", null); }
    }

    [Fact]
    public async Task Gestor_conecta_los_encendidos_envuelve_las_herramientas_y_el_agente_las_encuentra()
    {
        var s = Sandbox.ResetSettings();
        s.McpServers.Add(new McpServerConfig { Name = "Fake", Command = FakeExe, Enabled = true });
        s.McpServers.Add(new McpServerConfig { Name = "Apagado", Command = FakeExe, Enabled = false });
        try
        {
            await McpManager.EnsureAsync(s, CancellationToken.None);
            var conn = McpManager.ConnectionOf("Fake");
            Assert.NotNull(conn);
            Assert.Equal(2, conn!.Tools.Count);
            Assert.Null(McpManager.ConnectionOf("Apagado"));   // apagado: no conecta
            // Las herramientas salen en las definiciones del agente con su nombre con prefijo
            var definitions = ToolBox.Definitions(s);
            var names = definitions.Select(d => d!["function"]!["name"]!.GetValue<string>()).ToList();
            Assert.Contains("mcp__fake__echo", names);
            Assert.Contains(ToolBox.AddMcpServer, names);   // y la herramienta para añadir servidores
            // ToolBox.Find la encuentra y se ejecuta
            var echo = ToolBox.Find("mcp__fake__echo");
            Assert.NotNull(echo);
            Assert.Equal(Resource.Mcp, echo!.Resource);
            Assert.Equal("HOLA", await echo.Run(new JsonObject { ["text"] = "hola", ["reason"] = "porque sí" }, CancellationToken.None));
            // Quitar el servidor de la configuracion lo desconecta en el siguiente Ensure
            s.McpServers.RemoveAll(x => x.Name == "Fake");
            await McpManager.EnsureAsync(s, CancellationToken.None);
            Assert.Null(McpManager.ConnectionOf("Fake"));
            Assert.Null(ToolBox.Find("mcp__fake__echo"));
        }
        finally
        {
            McpManager.DisconnectAll();
            Sandbox.ResetSettings();
        }
    }

    [Fact]
    public async Task Con_el_recurso_denegado_no_se_conecta_ni_se_ofrecen_herramientas()
    {
        var s = Sandbox.ResetSettings();
        s.McpServers.Add(new McpServerConfig { Name = "Fake", Command = FakeExe, Enabled = true });
        s.SetPermission(Resource.Mcp, Permission.Deny);
        try
        {
            await McpManager.EnsureAsync(s, CancellationToken.None);
            Assert.Empty(McpManager.Connected);
            Assert.Empty(McpManager.Tools(s));
            Assert.DoesNotContain(ToolBox.Definitions(s).Select(d => d!["function"]!["name"]!.GetValue<string>()), n => n.StartsWith("mcp__", StringComparison.Ordinal) || n == ToolBox.AddMcpServer);
        }
        finally { McpManager.DisconnectAll(); Sandbox.ResetSettings(); }
    }

    [Fact]
    public async Task La_herramienta_add_mcp_server_lo_añade_y_lo_conecta()
    {
        var s = Sandbox.ResetSettings();
        try
        {
            var add = ToolBox.Find(ToolBox.AddMcpServer)!;
            var result = await add.Run(new JsonObject { ["name"] = "nuevo", ["command"] = $"\"{FakeExe}\"" }, CancellationToken.None);
            Assert.Contains("mcp__nuevo__echo", result);
            Assert.Contains(s.McpServers, x => x.Name == "nuevo");
            Assert.NotNull(McpManager.ConnectionOf("nuevo"));
            // Un comando que no existe: lo dice y no lo guarda
            var bad = await add.Run(new JsonObject { ["name"] = "malo", ["command"] = "no-existe-xyz" }, CancellationToken.None);
            Assert.StartsWith("[error]", bad);
            Assert.DoesNotContain(s.McpServers, x => x.Name == "malo");
        }
        finally { McpManager.DisconnectAll(); Sandbox.ResetSettings(); }
    }

    [Fact]
    public void La_herramienta_install_model_valida_la_entrada()
    {
        var unknown = ToolBox.Find(ToolBox.InstallModel)!.Run(new JsonObject { ["model"] = "cosa inventada" }, CancellationToken.None).Result;
        Assert.StartsWith("[error]", unknown);
        Assert.Equal("[error] model is required", ToolBox.Find(ToolBox.InstallModel)!.Run(new JsonObject { ["model"] = "  " }, CancellationToken.None).Result);
    }
}

/// <summary>La tarjeta de MCP en Ajustes: añadir un servidor (contra FakeMcp), verlo con sus herramientas, apagarlo y quitarlo.</summary>
public class McpSettingsTests : UiTest
{
    private static string FakeExe => Path.Combine(AppContext.BaseDirectory, "FakeMcp.exe");

    [Fact]
    public void Ajustes_AñadirUnServidorMCP_VerloYQuitarlo()
    {
        Ui.Run(() =>
        {
            var w = new SettingsWindow();
            Ui.Expect<SettingsWindow>(s =>
            {
                Assert.Equal(Loc.Get("McpTitle"), Ui.Find<TextBlock>(s, "McpTitle").Text);
                Assert.Equal(Loc.Get("McpNoServers"), Ui.All<TextBlock>(Ui.Find<StackPanel>(s, "McpList")).First().Text);
                Ui.Find<TextBox>(s, "McpNameBox").Text = "fake";
                Ui.Find<TextBox>(s, "McpCommandBox").Text = $"\"{FakeExe}\"";
                Ui.Click(Ui.Find<ButtonBase>(s, "McpAddButton"));
                Ui.WaitFor(() => Mcp.McpManager.ConnectionOf("fake") is not null && Ui.Find<TextBlock>(s, "McpStatus").Visibility == System.Windows.Visibility.Visible, 20_000);
                Assert.Equal(Loc.Format("McpAddOk", "fake-mcp", 2), Ui.Find<TextBlock>(s, "McpStatus").Text);
                Assert.Contains(AppSettings.Current.McpServers, x => x.Name == "fake");
                // La fila enseña las herramientas
                Assert.Contains(Ui.All<TextBlock>(Ui.Find<StackPanel>(s, "McpList")), t => t.Text.Contains(Loc.Format("McpConnected", 2)));
                // Apagarlo lo desconecta
                var on = Ui.All<CheckBox>(Ui.Find<StackPanel>(s, "McpList")).Single();
                on.IsChecked = false;
                on.RaiseEvent(new System.Windows.RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
                Ui.WaitFor(() => Mcp.McpManager.ConnectionOf("fake") is null && AppSettings.Current.McpServers.Single(x => x.Name == "fake").Enabled == false);
                // Quitarlo
                Ui.Answer("Yes");
                var delete = Ui.All<Button>(Ui.Find<StackPanel>(s, "McpList")).First(b => Equals(b.ToolTip, Loc.Get("McpDelete")));
                Ui.Click(delete);
                Assert.DoesNotContain(AppSettings.Current.McpServers, x => x.Name == "fake");
                s.Close();
            });
            w.ShowDialog();
        });
        Mcp.McpManager.DisconnectAll();
    }
}
