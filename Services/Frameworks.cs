using System.Text;
using System.Text.RegularExpressions;
using SocLucia.Agent;
using SocLucia.Mcp;

namespace SocLucia.Services;

/// <summary>El motor que necesita un framework para funcionar.</summary>
public enum FrameworkRuntime { Node, Python, Uv }

/// <summary>
/// Un framework pensado para IA que amplía Lucia. Hoy casi todos se conectan como servidor MCP (el
/// mismo protocolo que ya habla Lucia), así que instalar un framework es preparar su motor (Node,
/// Python, uv) y registrar su servidor MCP. Solo frameworks de licencia libre y uso comercial
/// (constitución).
/// </summary>
public sealed record LlmFramework(
    string Id,
    string Name,
    string Description,
    FrameworkRuntime Runtime,
    string McpName,
    string Command,
    List<string> Args,
    Dictionary<string, string> Env,
    string License,
    string Url,
    string? SkillName = null,
    string? SkillMarkdown = null,
    string? Note = null,
    bool Installable = true);

/// <summary>
/// El catálogo de frameworks para IA y la preparación del entorno (detectar e instalar Node, Python o
/// uv). Que la propia IA pueda ampliarse sola: <c>list_frameworks</c>, <c>ensure_runtime</c> e
/// <c>install_framework</c> en <see cref="ToolBox"/>. Fuera de la interfaz para poder probarlo (8.6).
/// </summary>
public static class Frameworks
{
    /// <summary>Cómo se ejecuta una orden corta para comprobar o instalar un motor; las pruebas lo sustituyen.</summary>
    public static Func<string, CancellationToken, Task<(int Code, string Output)>> CommandRunner { get; set; } = DefaultRunAsync;

    private static async Task<(int, string)> DefaultRunAsync(string command, CancellationToken cancel)
    {
        var r = await CommandTool.RunAsync(command, AppSettings.Current.WorkFolder, cancel);
        return (r.ExitCode, r.Output);
    }

    private static IReadOnlyList<LlmFramework>? _catalog;

    /// <summary>El catálogo. Perezoso (evita el orden de inicialización estática) y mutable a propósito
    /// (es dato): las pruebas pueden poner uno de mentira y restaurarlo.</summary>
    public static IReadOnlyList<LlmFramework> Catalog
    {
        get => _catalog ??= Default;
        set => _catalog = value;
    }

    private static readonly LlmFramework Rea = new(
        Id: "rea",
        Name: "REA — Reverse Engineer Anything",
        Description: "Reverse-engineering toolkit over MCP: native binaries, .NET assemblies, Android APKs, JavaScript/Electron apps, firmware and packet captures — analyzed locally. For software you own or are authorized to examine.",
        Runtime: FrameworkRuntime.Node,
        McpName: "rea",
        Command: "npx",
        Args: ["-y", "rea-agents@latest", "mcp"],
        Env: new Dictionary<string, string> { ["REA_MCP_INPUT_SCHEMA_PROFILE"] = "compact" },
        License: "MIT",
        Url: "https://github.com/morluto/rea",
        SkillName: "REA (Reverse Engineer Anything)",
        SkillMarkdown: ReaSkill,
        Note: "REA advertises many tools; Lucia requests the compact schema to keep it usable. It works best with a capable model and a large context, and the first run downloads the package with npx (needs Node.js 22+). Some targets need extra tools (Ghidra, JADX, apktool…) that REA does not install.");

    private static IReadOnlyList<LlmFramework> Default =>
    [
        Rea,
        // Servidores MCP oficiales (MIT) — se instalan solos con npx/uvx. {workfolder} = la carpeta de trabajo.
        new("mcp-filesystem", "MCP · Files", "Read and write files in a folder (the official filesystem MCP server).",
            FrameworkRuntime.Node, "filesystem", "npx", ["-y", "@modelcontextprotocol/server-filesystem", "{workfolder}"], new(), "MIT",
            "https://github.com/modelcontextprotocol/servers/tree/main/src/filesystem"),
        new("mcp-fetch", "MCP · Fetch", "Fetch a web page and return its text (the official fetch MCP server).",
            FrameworkRuntime.Uv, "fetch", "uvx", ["mcp-server-fetch"], new(), "MIT",
            "https://github.com/modelcontextprotocol/servers/tree/main/src/fetch"),
        new("mcp-git", "MCP · Git", "Read and operate on a Git repository (the official git MCP server).",
            FrameworkRuntime.Uv, "git", "uvx", ["mcp-server-git", "--repository", "{workfolder}"], new(), "MIT",
            "https://github.com/modelcontextprotocol/servers/tree/main/src/git"),
        new("mcp-memory", "MCP · Memory", "A simple knowledge graph the model can remember across turns.",
            FrameworkRuntime.Node, "memory", "npx", ["-y", "@modelcontextprotocol/server-memory"], new(), "MIT",
            "https://github.com/modelcontextprotocol/servers/tree/main/src/memory"),
        new("mcp-sequential-thinking", "MCP · Sequential thinking", "A tool for step-by-step reasoning over a problem.",
            FrameworkRuntime.Node, "sequential-thinking", "npx", ["-y", "@modelcontextprotocol/server-sequential-thinking"], new(), "MIT",
            "https://github.com/modelcontextprotocol/servers/tree/main/src/sequentialthinking"),
        new("mcp-sqlite", "MCP · SQLite", "Query a local SQLite database (read and write).",
            FrameworkRuntime.Uv, "sqlite", "uvx", ["mcp-server-sqlite", "--db-path", "{workfolder}/lucia.db"], new(), "MIT",
            "https://github.com/modelcontextprotocol/servers/tree/main/src/sqlite"),
        new("playwright", "Playwright MCP", "Drive a real browser (navigate, click, read, fill forms) for web tasks.",
            FrameworkRuntime.Node, "playwright", "npx", ["-y", "@playwright/mcp@latest"], new(), "Apache-2.0",
            "https://github.com/microsoft/playwright-mcp",
            Note: "The first run downloads a browser."),
        new("context7", "Context7", "Live, version-correct documentation for libraries, injected on demand.",
            FrameworkRuntime.Node, "context7", "npx", ["-y", "@upstash/context7-mcp"], new(), "MIT",
            "https://github.com/upstash/context7"),
        new("serena", "Serena", "Semantic code navigation and editing over a language server (LSP) — good for large codebases.",
            FrameworkRuntime.Uv, "serena", "uvx", ["--from", "git+https://github.com/oraios/serena", "serena", "start-mcp-server"], new(), "MIT",
            "https://github.com/oraios/serena"),

        // Para ingeniería inversa junto a REA, y voz local: se instalan a mano (no como servidor MCP de un clic).
        new("github-mcp", "GitHub MCP", "GitHub's own MCP server (issues, PRs, code). Runs with Docker and a GitHub token.",
            FrameworkRuntime.Node, "github", "", [], new(), "MIT", "https://github.com/github/github-mcp-server",
            Note: "Needs Docker and a GitHub token; set it up by hand.", Installable: false),
        new("ghidra", "Ghidra", "NSA's reverse-engineering suite (decompiler, disassembler). Pairs with REA for native binaries.",
            FrameworkRuntime.Node, "", "", [], new(), "Apache-2.0", "https://ghidra-sre.org/",
            Note: "Install by hand; REA can drive it.", Installable: false),
        new("rizin", "rizin / radare2", "Open reverse-engineering framework (disassembler, analysis). Alternative to Ghidra for REA.",
            FrameworkRuntime.Node, "", "", [], new(), "LGPL-3.0", "https://rizin.re/",
            Note: "Install by hand; commercial use allowed (LGPL).", Installable: false),
        new("whisper-cpp", "whisper.cpp", "Local speech-to-text (voice input), runs on this PC with no cloud.",
            FrameworkRuntime.Node, "", "", [], new(), "MIT", "https://github.com/ggml-org/whisper.cpp",
            Note: "Install by hand; local voice input is planned for Lucia.", Installable: false),
    ];

    public static LlmFramework? ById(string id) =>
        Catalog.FirstOrDefault(f => string.Equals(f.Id, (id ?? string.Empty).Trim(), StringComparison.OrdinalIgnoreCase)
                                 || string.Equals(f.McpName, (id ?? string.Empty).Trim(), StringComparison.OrdinalIgnoreCase));

    // ---- motores (Node, Python, uv) ----

    public static string Binary(FrameworkRuntime runtime) => runtime switch
    {
        FrameworkRuntime.Node => "node",
        FrameworkRuntime.Python => "python",
        _ => "uv",
    };

    private static string VersionCommand(FrameworkRuntime runtime) => runtime switch
    {
        FrameworkRuntime.Python => "python --version",
        _ => $"{Binary(runtime)} --version",
    };

    private static string WingetId(FrameworkRuntime runtime) => runtime switch
    {
        FrameworkRuntime.Node => "OpenJS.NodeJS.LTS",
        FrameworkRuntime.Python => "Python.Python.3.12",
        _ => "astral-sh.uv",
    };

    public sealed record RuntimeStatus(FrameworkRuntime Runtime, bool Present, string Version);

    /// <summary>Comprueba si un motor está instalado y su versión (sin instalar nada).</summary>
    public static async Task<RuntimeStatus> DetectAsync(FrameworkRuntime runtime, CancellationToken cancel)
    {
        try
        {
            var (code, output) = await CommandRunner(VersionCommand(runtime), cancel);
            var version = Regex.Match(output, @"\d+(\.\d+)+").Value;
            return new RuntimeStatus(runtime, code == 0 && version.Length > 0, version);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception) { return new RuntimeStatus(runtime, false, string.Empty); }
    }

    private static int Major(string version) => int.TryParse(version.Split('.').FirstOrDefault(), out var n) ? n : 0;

    /// <summary>
    /// Asegura un motor: si está, lo dice; si no, lo instala con winget y vuelve a comprobar. No es
    /// silencioso: la propia herramienta la ha aprobado el usuario, e instalar puede pedir permiso de
    /// Windows (UAC) y tardar. Devuelve un texto para el modelo/usuario.
    /// </summary>
    public static async Task<string> EnsureRuntimeAsync(FrameworkRuntime runtime, CancellationToken cancel)
    {
        var status = await DetectAsync(runtime, cancel);
        var name = Binary(runtime);
        if (status.Present)
        {
            if (runtime == FrameworkRuntime.Node && Major(status.Version) < 22)
                return $"Node.js {status.Version} is installed, but version 22 or newer is recommended (some frameworks need it). Update it from https://nodejs.org.";
            return $"{name} {status.Version} is already installed.";
        }
        var (code, output) = await CommandRunner($"winget install -e --id {WingetId(runtime)} --accept-package-agreements --accept-source-agreements", cancel);
        var after = await DetectAsync(runtime, cancel);
        if (after.Present)
            return $"{name} {after.Version} installed.";
        return $"[error] {name} is not installed and could not be installed automatically (exit {code}). Install it by hand (Node.js: https://nodejs.org, version 22+) and try again.\n{Trim(output)}";
    }

    // ---- instalar un framework (= registrar su servidor MCP) ----

    /// <summary>Texto de list_frameworks para el modelo: cada framework, su motor y si está ya configurado.</summary>
    public static string ListText()
    {
        var sb = new StringBuilder();
        foreach (var f in Catalog)
        {
            var configured = AppSettings.Current.McpServers.Any(s => string.Equals(s.Name, f.McpName, StringComparison.OrdinalIgnoreCase));
            sb.AppendLine($"- {f.Id}: {f.Name} ({f.License}, runtime {Binary(f.Runtime)}){(configured ? " [installed]" : string.Empty)}");
            sb.AppendLine($"   {f.Description}");
            sb.AppendLine($"   {f.Url}");
        }
        sb.AppendLine("Install one with install_framework(id). It prepares the runtime and registers the framework so you gain its tools.");
        return sb.ToString().Trim();
    }

    /// <summary>
    /// Instala un framework: asegura su motor, registra su servidor MCP (lo prueba, lo guarda y lo conecta)
    /// y, si trae una habilidad, la instala. Reutiliza <see cref="McpManager"/> para no duplicar nada.
    /// </summary>
    public static async Task<string> InstallAsync(string id, CancellationToken cancel)
    {
        var framework = ById(id);
        if (framework is null)
            return $"[error] unknown framework '{id}'. Use list_frameworks to see the available ones.";

        var runtime = await EnsureRuntimeAsync(framework.Runtime, cancel);
        if (runtime.StartsWith("[error]", StringComparison.Ordinal))
            return runtime + $"\n(The framework '{framework.Id}' needs {Binary(framework.Runtime)}.)";

        if (!framework.Installable || framework.Command.Length == 0)
            return $"[error] '{framework.Id}' is not a one-click MCP server; set it up by hand. {framework.Note} {framework.Url}".Trim();

        // {workfolder}/{home} se sustituyen al instalar, para no hornear rutas en el catálogo.
        var work = AppSettings.Current.WorkFolder;
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var config = new McpServerConfig
        {
            Name = framework.McpName,
            Command = framework.Command,
            Args = framework.Args.Select(a => a.Replace("{workfolder}", work).Replace("{home}", home)).ToList(),
            Env = new Dictionary<string, string>(framework.Env),
            Enabled = true,
        };
        try
        {
            var (info, tools) = await McpManager.TestAsync(config, cancel);
            var settings = AppSettings.Current;
            settings.McpServers.RemoveAll(x => string.Equals(x.Name, config.Name, StringComparison.OrdinalIgnoreCase));
            settings.McpServers.Add(config);
            settings.Save();
            await McpManager.EnsureAsync(settings, cancel);

            var skillNote = string.Empty;
            if (framework.SkillName is { Length: > 0 } sn && framework.SkillMarkdown is { Length: > 0 } sm)
            {
                SkillStore.WriteBuiltin(sn, sm);
                skillNote = $" A skill '{sn}' was installed with instructions; use it with use_skill.";
            }

            var note = framework.Note is { Length: > 0 } n ? " " + n : string.Empty;
            return $"{runtime}\n{framework.Name} is installed ({info.Name} {info.Version}, {tools.Count} tools).{skillNote}{note}";
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            return $"{runtime}\n[error] could not start {framework.Name}: {ex.Message}";
        }
    }

    private static string Trim(string s) => s.Length > 500 ? s[..500] + "…" : s.Trim();

    /// <summary>La habilidad que acompaña a REA: cómo investigar con sus herramientas, de forma lícita.</summary>
    private const string ReaSkill = """
---
name: REA (Reverse Engineer Anything)
description: Investigate and reverse engineer software you own or are authorized to examine — native binaries, .NET assemblies, Android APKs, JavaScript/Electron apps, firmware, packet captures — through the REA MCP tools.
license: MIT
---

# Reverse engineering with REA

REA is an MCP server (`mcp__rea__*` tools) for analyzing software locally. Use it only on software the
user owns or is authorized to examine.

## How to work
1. List the REA tools you have (`mcp__rea__*`). REA exposes many tools; its input schemas are compact.
2. Start a session/inspection on the target the user points to (a file path, an APK, an assembly, a URL).
3. Identify what the target is (file type, format, runtime) before going deeper. Let REA tell you.
4. Trace the specific behavior the user asked about: follow functions, strings, imports, decompiled
   pseudocode. Quote concrete evidence (addresses, symbols, bytes) in your answer.
5. If a target type needs an external engine REA does not bundle (Ghidra, IDA/Hopper, JADX+JDK, apktool,
   TShark, Binwalk…), say so and tell the user what to install; do not guess.

## Notes
- Everything runs on this PC. Keep the target, the analysis and its output on the user's machine.
- REA works best with a capable model and a large context: raise the context in Settings if answers get
  cut off, and prefer a bigger model for hard binaries.
""";
}
