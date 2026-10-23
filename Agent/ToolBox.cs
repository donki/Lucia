using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Windows;
using SocLucia.Services;

namespace SocLucia.Agent;

/// <summary>Un tipo de recurso del PC. Cada herramienta pertenece a uno y los permisos se dan por recurso.</summary>
public enum Resource { Commands, FilesRead, FilesWrite, Internet, Clipboard, Apps, Free, Mcp }

/// <summary>Que hacer cuando la IA quiere usar un recurso: preguntar, dejar siempre o no ofrecerselo.</summary>
public enum Permission { Ask, Allow, Deny }

/// <summary>Una herramienta: nombre para el modelo, recurso al que pertenece y como se ejecuta.</summary>
public sealed record Tool(string Name, Resource Resource, string Description, JsonObject Parameters, Func<JsonObject, CancellationToken, Task<string>> Run)
{
    /// <summary>Lo que se le enseña al usuario al pedir permiso y en la burbuja (orden, ruta, direccion…).</summary>
    public string Detail(JsonObject args) => Resource switch
    {
        Resource.Commands => Arg(args, "command"),
        Resource.FilesRead or Resource.FilesWrite => Arg(args, "path").Length > 0 ? Arg(args, "path") : Arg(args, "folder"),
        Resource.Internet => Name == ToolBox.WebSearch ? Arg(args, "query") : Arg(args, "url"),
        Resource.Apps => Arg(args, "target"),
        Resource.Clipboard => Name == ToolBox.WriteClipboard ? Arg(args, "text") : string.Empty,
        Resource.Free => Name == ToolBox.Remember ? Arg(args, "fact") : Name == ToolBox.ScheduleTask ? Arg(args, "title") + " · " + Arg(args, "when") : Name == ToolBox.DeleteTask ? Arg(args, "id") : Arg(args, "name"),
        Resource.Mcp => Name switch
        {
            ToolBox.AddMcpServer => Arg(args, "name") + " · " + Arg(args, "command"),
            ToolBox.InstallModel => Arg(args, "model"),
            ToolBox.InstallFramework => Arg(args, "id"),
            ToolBox.EnsureRuntime => Arg(args, "runtime"),
            ToolBox.UseSkill => Arg(args, "name"),
            ToolBox.InstallSkill => Arg(args, "source"),
            _ => CompactArgs(args),
        },
        _ => string.Empty,
    };

    internal static string Arg(JsonObject args, string name) => args[name]?.GetValue<string>() ?? string.Empty;

    /// <summary>Un resumen corto de los argumentos de una herramienta MCP (para el permiso y la burbuja).</summary>
    internal static string CompactArgs(JsonObject args)
    {
        var clone = args.DeepClone().AsObject();
        clone.Remove("reason");
        var text = clone.ToJsonString();
        return text.Length > 160 ? text[..160] + "…" : text == "{}" ? string.Empty : text;
    }
}

/// <summary>
/// Las herramientas que la IA puede usar en «modo trabajo»: ordenes de PowerShell, ficheros,
/// internet, portapapeles y abrir cosas. Cada una pasa por la aplicacion, que pide permiso segun
/// lo que diga Ajustes › Permisos para su recurso (constitucion de herramientas, 4) y deja
/// registro de todo en <c>logs\actions.log</c>.
/// </summary>
public static class ToolBox
{
    public const string RunCommand = "run_command";
    public const string ReadFile = "read_file";
    public const string ListFolder = "list_folder";
    public const string FindFiles = "find_files";
    public const string WriteFile = "write_file";
    public const string FetchUrl = "fetch_url";
    public const string WebSearch = "web_search";
    public const string ReadClipboard = "read_clipboard";
    public const string WriteClipboard = "write_clipboard";
    public const string OpenItem = "open_item";
    public const string SystemInfo = "system_info";
    public const string Remember = "remember";
    public const string ListDocuments = "list_documents";
    public const string ReadDocument = "read_document";
    public const string SaveDocument = "save_document";
    public const string ScheduleTask = "schedule_task";
    public const string ListTasks = "list_tasks";
    public const string DeleteTask = "delete_task";
    public const string GenerateImage = "generate_image";
    public const string AddMcpServer = "add_mcp_server";
    public const string ListMcpServers = "list_mcp_servers";
    public const string InstallModel = "install_model";
    public const string ListSkills = "list_skills";
    public const string UseSkill = "use_skill";
    public const string InstallSkill = "install_skill";
    public const string ListFrameworks = "list_frameworks";
    public const string InstallFramework = "install_framework";
    public const string EnsureRuntime = "ensure_runtime";

    private const int MaxChars = 12_000;

    public static readonly Tool[] All =
    [
        new(RunCommand, Resource.Commands,
            "Run one PowerShell command line on the user's Windows PC and return its output (stdout and stderr). Use it for programs, git, dotnet, python, etc.",
            Params(("command", "The PowerShell command line to run.")),
            async (a, c) =>
            {
                var command = Tool.Arg(a, "command");
                var folder = AppSettings.Current.WorkFolder;
                var r = await CommandTool.RunAsync(command, folder, c);
                var head = r.TimedOut ? "[timed out after 3 minutes]\n" : $"[exit code {r.ExitCode}]\n";
                return head + r.Output + ProgramInfo.Note(command, head + r.Output, folder);
            }),
        new(ReadFile, Resource.FilesRead,
            "Read a text file from the PC and return its content (UTF-8, long files are cut). PDF and Word (.docx) files come back as their text.",
            Params(("path", "Full path, or relative to the working folder.")),
            (a, _) => Task.FromResult(Cut(ReadAnyText(Full(Tool.Arg(a, "path")))))),
        new(ListFolder, Resource.FilesRead,
            "List the files and subfolders of a folder with sizes.",
            Params(("path", "Folder path, or relative to the working folder.")),
            (a, _) => Task.FromResult(ListFolderText(Full(Tool.Arg(a, "path"))))),
        new(FindFiles, Resource.FilesRead,
            "Find files by name pattern under a folder, recursively (first 200 matches).",
            Params(("folder", "Folder to search in."), ("pattern", "Name pattern, e.g. *.cs or report*.pdf.")),
            (a, _) => Task.FromResult(FindFilesText(Full(Tool.Arg(a, "folder")), Tool.Arg(a, "pattern")))),
        new(WriteFile, Resource.FilesWrite,
            "Write a text file (creates it or overwrites it; folders are created). Use it to save results, notes, scripts or code.",
            Params(("path", "Full path, or relative to the working folder."), ("content", "The whole content of the file.")),
            (a, _) => { var p = Full(Tool.Arg(a, "path")); Directory.CreateDirectory(Path.GetDirectoryName(p)!); File.WriteAllText(p, Tool.Arg(a, "content"), new UTF8Encoding(false)); return Task.FromResult($"Written {new FileInfo(p).Length} bytes to {p}"); }),
        new(WebSearch, Resource.Internet,
            "Search the web and get the top results (title, address, snippet). Use it for current events, prices, versions, documentation or anything you are not sure about; then read the best result with fetch_url.",
            Params(("query", "What to search for, in the most useful language.")),
            SearchAsync),
        new(FetchUrl, Resource.Internet,
            "Download a web page or file from the internet (GET) and return its text (HTML is reduced to text; long content is cut).",
            Params(("url", "The full http(s) address.")),
            FetchAsync),
        new(ReadClipboard, Resource.Clipboard,
            "Read the text that is currently in the Windows clipboard.",
            Params(),
            (_, _) => Task.FromResult(OnUi(() => Desktop.Current.ClipboardHasText() ? Cut(Desktop.Current.ClipboardText()) : "[the clipboard has no text]"))),
        new(WriteClipboard, Resource.Clipboard,
            "Put text in the Windows clipboard so the user can paste it.",
            Params(("text", "The text to copy.")),
            (a, _) => Task.FromResult(OnUi(() => { Desktop.Current.SetClipboardText(Tool.Arg(a, "text")); return "Copied to the clipboard."; }))),
        new(OpenItem, Resource.Apps,
            "Open a file, folder, program or web address with the program Windows uses for it (like double-clicking it).",
            Params(("target", "Path or URL to open.")),
            (a, _) => { Desktop.Current.Open(Tool.Arg(a, "target")); return Task.FromResult("Opened."); }),
    ];

    /// <summary>Informacion del PC: no toca nada, no pide permiso.</summary>
    public static readonly Tool Info = new(SystemInfo, Resource.Free,
        "Basic facts about this PC: Windows version, user, processor, memory, drives, current date and time, working folder.",
        Params(), (_, _) => Task.FromResult(SystemInfoText()));

    /// <summary>Herramientas que van en los dos modos y no piden permiso: la memoria sobre el usuario y sus documentos.</summary>
    public static readonly Tool[] Free =
    [
        new(Remember, Resource.Free,
            "Save one short lasting fact about the user for future conversations (name, job, preferences, projects, family, how they like answers). Only what the user states about themselves; never secrets or passwords.",
            Params(("fact", "One sentence, third person, in the user's language.")),
            (a, _) => Task.FromResult(!AppSettings.Current.MemoryEnabled ? "Memory is turned off in the app settings." : UserMemory.Add(Tool.Arg(a, "fact")) ? "Saved." : "Already known.")),
        new(ListDocuments, Resource.Free,
            "List the documents the user keeps in the documents folder (relative names and sizes).",
            Params(),
            (_, _) => Task.FromResult(DocumentsText())),
        new(SaveDocument, Resource.Free,
            "Create a document in the user's documents folder: .md, .txt, .html, .csv or .docx (Word; write its content as Markdown-like plain text: lines starting with # are headings, blank line separates paragraphs). Use it when the user asks for a report, letter, list, summary or any file to keep. Return value tells where it was saved.",
            Params(("name", "File name with extension, e.g. informe.docx or notas.md"), ("content", "The whole content.")),
            (a, _) => Task.FromResult(SaveDocumentText(Tool.Arg(a, "name"), Tool.Arg(a, "content")))),
        new(ScheduleTask, Resource.Free,
            "Schedule a prompt to run by itself later, once or repeatedly, as a new conversation (e.g. a daily summary, a weekly reminder, a check every hour). Unattended runs can only use tools set to Always in the app settings.",
            Params(("title", "Short title."), ("prompt", "The full request that will be sent, self-contained."), ("when", "once YYYY-MM-DD HH:MM | daily HH:MM | weekly mon HH:MM | every 30m (or 2h). 24-hour local time.")),
            (a, _) => { var t = Scheduler.Add(Tool.Arg(a, "title"), Tool.Arg(a, "prompt"), Tool.Arg(a, "when")); return Task.FromResult($"Scheduled [{t.Id}] {t.Title}: {t.Describe(System.Globalization.CultureInfo.CurrentUICulture)}; next run {t.NextRun.LocalDateTime:g}"); }),
        new(ListTasks, Resource.Free,
            "List the scheduled tasks with their ids and next run.",
            Params(),
            (_, _) => Task.FromResult(Scheduler.All.Count == 0 ? "[no scheduled tasks]" : string.Join("\n", Scheduler.All.Select(t => $"[{t.Id}] {t.Title} — {t.Describe(System.Globalization.CultureInfo.CurrentUICulture)} — {(t.Enabled ? "next " + t.NextRun.LocalDateTime.ToString("g") : "off")}")))),
        new(DeleteTask, Resource.Free,
            "Delete a scheduled task by id (from list_tasks).",
            Params(("id", "Task id.")),
            (a, _) => { Scheduler.Remove(Tool.Arg(a, "id").Trim()); return Task.FromResult("Deleted."); }),
        new(ReadDocument, Resource.Free,
            "Read one of the user's documents by its relative name as listed by list_documents (long documents are cut).",
            Params(("name", "Relative name from list_documents.")),
            (a, _) => Task.FromResult(ReadDocumentText(Tool.Arg(a, "name")))),
        new(GenerateImage, Resource.Free,
            "Generate a picture from a text description with a local Stable Diffusion model (no internet). Use it whenever the user asks for an image, drawing, illustration, photo-like picture, logo idea or wallpaper. Write the prompt in English, concrete and visual (subject, style, lighting, colors); put what to avoid in negative_prompt. It takes 15-60 seconds. The result line `Image: <path>` is shown to the user as the picture: do not repeat the path, just describe briefly what you made.",
            Params(("prompt", "Visual description in English."), ("negative_prompt", "What to avoid (optional), e.g. blurry, text, watermark."), ("shape", "square, wide or tall (optional, default square).")),
            GenerateImageAsync),
    ];

    /// <summary>La imagen se genera en el PC; el resultado «Image: ruta» lo pinta la burbuja de la herramienta.</summary>
    private static async Task<string> GenerateImageAsync(JsonObject args, CancellationToken cancel)
    {
        var prompt = Tool.Arg(args, "prompt").Trim();
        if (prompt.Length == 0) return "[error] empty prompt";
        var shape = Tool.Arg(args, "shape").Trim().ToLowerInvariant();
        var (w, h) = shape switch { "wide" => (768, 448), "tall" => (448, 768), _ => (512, 512) };
        var negative = Tool.Arg(args, "negative_prompt").Trim();
        if (negative.Length == 0) negative = "blurry, low quality, deformed, text, watermark";
        var path = await Engine.ImageEngine.GenerateAsync(new Engine.ImageEngine.Request(prompt, negative, w, h), ImageProgress, cancel);
        return "Image: " + path;
    }

    /// <summary>Progreso de la descarga del motor o del modelo de imagenes (la ventana lo enseña en la barra de estado).</summary>
    public static IProgress<Engine.Downloader.Progress>? ImageProgress { get; set; }

    private static string McpServersText()
    {
        var servers = AppSettings.Current.McpServers;
        var sb = new StringBuilder();
        if (servers.Count == 0) sb.AppendLine("No MCP servers configured yet.");
        foreach (var server in servers)
        {
            var conn = Mcp.McpManager.ConnectionOf(server.Name);
            sb.AppendLine($"- {server.Name} ({(server.Enabled ? "on" : "off")}): {server.CommandLine}");
            if (conn is not null) sb.AppendLine("   tools: " + string.Join(", ", conn.Tools.Select(t => t.Name)));
        }
        sb.AppendLine("To add one: add_mcp_server with a name and the command line (e.g. 'uvx mcp-server-fetch' or 'npx -y @modelcontextprotocol/server-filesystem <folder>').");
        return sb.ToString().Trim();
    }

    private static async Task<string> AddMcpServerAsync(JsonObject args, CancellationToken cancel)
    {
        var name = Tool.Arg(args, "name").Trim();
        var commandLine = Tool.Arg(args, "command").Trim();
        if (name.Length == 0 || commandLine.Length == 0) return "[error] name and command are required";
        var (command, parsed) = Mcp.McpServerConfig.ParseCommandLine(commandLine);
        var config = new Mcp.McpServerConfig { Name = name, Command = command, Args = parsed, Enabled = true };
        try
        {
            var (info, tools) = await Mcp.McpManager.TestAsync(config, cancel);
            var s = AppSettings.Current;
            s.McpServers.RemoveAll(x => string.Equals(x.Name, name, StringComparison.OrdinalIgnoreCase));
            s.McpServers.Add(config);
            s.Save();
            await Mcp.McpManager.EnsureAsync(s, cancel);
            return tools.Count == 0
                ? $"MCP server '{name}' connected ({info.Name} {info.Version}) but it offers no tools."
                : $"MCP server '{name}' connected ({info.Name} {info.Version}). New tools: " + string.Join(", ", tools.Select(t => Mcp.McpManager.ToolName(name, t.Name)));
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            return "[error] could not start the MCP server: " + ex.Message + ". Check the command (npx needs Node.js; uvx needs uv).";
        }
    }

    private static string InstallModelText(string query)
    {
        query = query.Trim();
        if (query.Length == 0) return "[error] model is required";
        var catalog = Engine.ModelCatalog.All.FirstOrDefault(m => m.Name.Equals(query, StringComparison.OrdinalIgnoreCase) || m.Repo.Equals(query, StringComparison.OrdinalIgnoreCase));
        if (catalog is null && !query.Contains('/'))
            return "[error] unknown model. Give a catalog name (Qwen3.5 4B, Gemma 4 12B, Phi-4 Mini…) or a Hugging Face GGUF repo id like unsloth/Qwen3.5-4B-GGUF.";
        if (Engine.ModelDownloads.IsDownloading(catalog?.Repo ?? query))
            return "That model is already downloading.";
        var model = catalog ?? new Engine.CatalogModel(Engine.HuggingFace.NameFor(query), query, 0, "?", "CatalogHf");
        Engine.ModelDownloads.Start(model);
        var size = model.ApproxBytes > 0 ? $" (about {model.ApproxBytes / (1024 * 1024 * 1024.0):0.#} GB)" : string.Empty;
        return $"Downloading {model.Name}{size} in the background. It will appear in the installed AIs when done; the user can switch to it then.";
    }

    /// <summary>
    /// Herramientas para que la propia IA amplíe Lucia: añadir un servidor MCP (gana sus herramientas) o
    /// instalar un modelo. Son del recurso <see cref="Resource.Mcp"/>, así que el usuario las aprueba igual.
    /// </summary>
    public static readonly Tool[] Manage =
    [
        new(ListMcpServers, Resource.Mcp,
            "List the MCP servers configured in Lucia and the tools each one gives, plus how to add more. Use it before add_mcp_server to avoid duplicates.",
            Params(),
            (_, _) => Task.FromResult(McpServersText())),
        new(AddMcpServer, Resource.Mcp,
            "Add an MCP (Model Context Protocol) server to Lucia and connect to it, so you gain its tools for the rest of the conversation. MCP servers extend Lucia with new abilities (files, databases, web, APIs). Most install themselves the first time with npx or uvx.",
            Params(("name", "A short name for the server, e.g. filesystem."), ("command", "The command line that runs the server, e.g. 'npx -y @modelcontextprotocol/server-filesystem C:/Users/me' or 'uvx mcp-server-fetch'.")),
            AddMcpServerAsync),
        new(InstallModel, Resource.Mcp,
            "Download and install an AI model so it is available in Lucia. Give a catalog name (Qwen3.5 4B, Gemma 4 12B…) or a Hugging Face GGUF repository id (e.g. unsloth/Qwen3.5-4B-GGUF). It downloads in the background; tell the user it may take a while and how big it is.",
            Params(("model", "Catalog name or Hugging Face repo id.")),
            (a, _) => Task.FromResult(InstallModelText(Tool.Arg(a, "model")))),
        new(ListSkills, Resource.Mcp,
            "List the skills installed in Lucia (packaged instructions you can load) and how to add more. A skill is a set of instructions for a kind of task; load one with use_skill when the task matches.",
            Params(),
            (_, _) => Task.FromResult(Services.SkillStore.ListText())),
        new(UseSkill, Resource.Mcp,
            "Load a skill's instructions into the conversation and follow them. Use it when the current task matches a skill from list_skills. The returned text is the instructions you must apply now.",
            Params(("name", "The skill name, as shown by list_skills.")),
            (a, _) => Task.FromResult(UseSkillText(Tool.Arg(a, "name")))),
        new(InstallSkill, Resource.Mcp,
            "Install a skill from a local folder with a SKILL.md, a .zip, a SKILL.md file, or an http(s) address to a .zip. Only skills whose licence allows commercial use are accepted. After installing, use it with use_skill.",
            Params(("source", "A folder path, a .zip path, a SKILL.md path, or an http(s) address to a .zip.")),
            async (a, c) => (await Services.SkillStore.InstallAsync(Tool.Arg(a, "source"), c)).Message),
        new(ListFrameworks, Resource.Mcp,
            "List the LLM frameworks Lucia can set up (reverse-engineering, analysis, agent toolkits…) with their runtime and whether they are already installed. Install one with install_framework.",
            Params(),
            (_, _) => Task.FromResult(Services.Frameworks.ListText())),
        new(InstallFramework, Resource.Mcp,
            "Set up an LLM framework from the catalog: it prepares the runtime it needs (Node.js, Python or uv) and registers it so you gain its tools for the rest of the conversation. Use list_frameworks first to get the id (e.g. 'rea').",
            Params(("id", "The framework id from list_frameworks, e.g. rea.")),
            (a, c) => Services.Frameworks.InstallAsync(Tool.Arg(a, "id"), c)),
        new(EnsureRuntime, Resource.Mcp,
            "Make sure a runtime a framework needs is installed on this PC: 'node' (Node.js, for npx servers), 'python', or 'uv' (for uvx servers). If it is missing it is installed with winget (may ask Windows for permission and take a while).",
            Params(("runtime", "node, python or uv.")),
            EnsureRuntimeText),
    ];

    private static string UseSkillText(string name)
    {
        var body = Services.SkillStore.Read(name);
        return body is null
            ? $"[error] no skill named '{name}'. Use list_skills to see the installed skills."
            : $"[skill: {name}]\n{body}";
    }

    private static Task<string> EnsureRuntimeText(JsonObject args, CancellationToken cancel)
    {
        var runtime = Tool.Arg(args, "runtime").Trim().ToLowerInvariant();
        return runtime switch
        {
            "node" or "nodejs" or "npx" => Services.Frameworks.EnsureRuntimeAsync(Services.FrameworkRuntime.Node, cancel),
            "python" or "py" => Services.Frameworks.EnsureRuntimeAsync(Services.FrameworkRuntime.Python, cancel),
            "uv" or "uvx" => Services.Frameworks.EnsureRuntimeAsync(Services.FrameworkRuntime.Uv, cancel),
            _ => Task.FromResult("[error] unknown runtime. Use 'node', 'python' or 'uv'."),
        };
    }

    public static Tool? Find(string name) => name == SystemInfo ? Info
        : All.FirstOrDefault(t => t.Name == name) ?? Free.FirstOrDefault(t => t.Name == name)
          ?? Manage.FirstOrDefault(t => t.Name == name) ?? Mcp.McpManager.Find(name);

    /// <summary>Modo preguntas: las mismas herramientas que el agente (con sus permisos); cambia la consigna, no el alcance.</summary>
    public static JsonArray QuestionDefinitions(AppSettings settings) => Definitions(settings);

    /// <summary>Consigna del modo preguntas: responder; herramientas solo si la pregunta lo pide.</summary>
    public static string QuestionPrompt(AppSettings settings)
        => "Answer the user directly. You also have tools for this PC (files, commands, clipboard, opening things), the user's documents, scheduling and memory: " +
           "use one only when the request clearly needs it (a file, a command, a document, a reminder), one step at a time, and give a short reason; the user approves each action. " +
           $"The working folder is `{settings.WorkFolder}`. Never destroy data unless asked for exactly that. " + PathsHint;

    /// <summary>Las rutas completas se ven como enlaces en el chat: un clic abre el fichero o la carpeta.</summary>
    public const string PathsHint = "When you mention a file or folder on this PC, write its full absolute path (for example `C:\\Users\\me\\Documents\\notes.md`): the user can click it to open it.";

    private static IEnumerable<Tool> FreeTools(AppSettings settings) => Free.Where(t => t.Name != Remember || settings.MemoryEnabled);

    /// <summary>Lo que va en el system prompt en los dos modos: memoria sobre el usuario y documentos que pegan con la pregunta.</summary>
    public static string ContextPrompt(AppSettings settings, string question)
    {
        var parts = new List<string>();
        if (settings.MemoryEnabled)
        {
            var memory = UserMemory.Prompt();
            parts.Add((memory.Length > 0 ? memory + "\n" : string.Empty) + "When the user tells you something lasting about themselves, save it with `remember` (one short fact per call) and carry on.");
        }
        var docs = DocumentIndex.Prompt(question);
        if (docs.Length > 0) parts.Add(docs);
        // Las habilidades encendidas se le enseñan al modelo (las carga con use_skill), salvo que el
        // recurso de extensiones esté denegado (entonces no tiene esa herramienta).
        if (settings.PermissionFor(Resource.Mcp) != Permission.Deny)
        {
            var skills = Services.SkillStore.Prompt();
            if (skills.Length > 0) parts.Add(skills);
        }
        return string.Join("\n\n", parts);
    }

    /// <summary>Instrucciones del modo preguntas con internet: buscar solo cuando hace falta.</summary>
    public const string InternetPrompt =
        "You can look things up on the internet with `web_search` (top results) and `fetch_url` (read a page). " +
        "Use them when the answer needs current, local or verifiable information (news, prices, versions, documentation, facts you are unsure of); " +
        "answer directly from what you know otherwise. Do not answer from the search snippets alone: after web_search, open the one or two most relevant results with `fetch_url` and read them, and follow a link inside a page with `fetch_url` again when it leads to what you need, before answering. " +
        "If a page fails or is blocked, or a search gives nothing, try a different source (another result, another search, the official site) instead of giving up. Cite the address of what you used.";

    /// <summary>Modo agente: todo lo que no este denegado en Ajustes, mas las libres.</summary>
    public static JsonArray Definitions(AppSettings settings)
        => Definitions(settings, All.Where(t => settings.PermissionFor(t.Resource) != Permission.Deny).Append(Info).Concat(FreeTools(settings)).Concat(ManageTools(settings)).Concat(Mcp.McpManager.Tools(settings)));

    /// <summary>Las herramientas con las que la IA amplía Lucia, salvo que el recurso MCP esté denegado.</summary>
    private static IEnumerable<Tool> ManageTools(AppSettings settings) => settings.PermissionFor(Resource.Mcp) == Permission.Deny ? [] : Manage;

    private static JsonArray Definitions(AppSettings settings, IEnumerable<Tool> tools)
    {
        var array = new JsonArray();
        foreach (var tool in tools)
            array.Add(new JsonObject
            {
                ["type"] = "function",
                ["function"] = new JsonObject { ["name"] = tool.Name, ["description"] = tool.Description, ["parameters"] = JsonNode.Parse(tool.Parameters.ToJsonString()) },
            });
        return array;
    }

    /// <summary>Instrucciones que acompañan al modelo cuando el modo trabajo esta activo.</summary>
    public static string SystemPrompt(AppSettings settings)
    {
        var names = string.Join(", ", All.Where(t => settings.PermissionFor(t.Resource) != Permission.Deny).Select(t => "`" + t.Name + "`").Append("`" + SystemInfo + "`"));
        return $"You can use this Windows PC through tools: {names}. The working folder is `{settings.WorkFolder}` (relative paths start there). " +
               "Prefer the specific tools (read_file, list_folder, write_file, fetch_url) over run_command when they fit; one step per call, read the result before the next. " +
               "The user sees and approves each action, so give a short reason. Never destroy data (delete, format, overwrite, force push) unless the user asked for exactly that. " +
               "If an action fails, read the error and change your approach; never repeat the same failing command unchanged. If one path cannot work (for example, a program with a window gives no console output, so reading its bytes will not reveal what it does), say so and propose a real alternative or ask the user, instead of trying it again. " +
               "When you are done, answer the user in their language and summarise what you did. " + PathsHint;
    }

    public static void Log(Tool tool, string detail, string outcome)
    {
        try
        {
            Directory.CreateDirectory(Paths.Logs);
            File.AppendAllText(Path.Combine(Paths.Logs, "actions.log"),
                $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} {tool.Name} [{outcome}] {detail.ReplaceLineEndings(" ")}{Environment.NewLine}");
        }
        catch (Exception) { }
    }

    // ------------------------------------------------------------------ ejecutores

    private static string Full(string path)
    {
        if (path.Trim().Length == 0) throw new ArgumentException("Empty path");
        var expanded = Environment.ExpandEnvironmentVariables(path.Trim().Trim('"'));
        return Path.GetFullPath(Path.IsPathRooted(expanded) ? expanded : Path.Combine(AppSettings.Current.WorkFolder, expanded));
    }

    private static string Cut(string text) => text.Length <= MaxChars ? text : text[..(MaxChars / 2)] + "\n…[cut]…\n" + text[^(MaxChars / 2)..];

    private static string ListFolderText(string folder)
    {
        var sb = new StringBuilder();
        sb.AppendLine("size (bytes)  name");
        foreach (var d in Directory.GetDirectories(folder).OrderBy(x => x, StringComparer.OrdinalIgnoreCase))
            sb.AppendLine($"[dir]  {Path.GetFileName(d)}");
        foreach (var f in Directory.GetFiles(folder).OrderBy(x => x, StringComparer.OrdinalIgnoreCase))
            sb.AppendLine($"{new FileInfo(f).Length,12:N0}  {Path.GetFileName(f)}");
        return sb.Length <= 20 ? "[empty folder]" : Cut(sb.ToString());
    }

    private static string FindFilesText(string folder, string pattern)
    {
        var sb = new StringBuilder();
        var n = 0;
        var options = new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true, MatchCasing = MatchCasing.CaseInsensitive };
        foreach (var f in Directory.EnumerateFiles(folder, pattern.Length > 0 ? pattern : "*", options))
        {
            sb.AppendLine(f);
            if (++n >= 200) { sb.AppendLine("…[more]"); break; }
        }
        return n == 0 ? "[no matches]" : sb.ToString();
    }

    /// <summary>Busqueda en DuckDuckGo (version HTML, sin clave ni cuenta): titulo, direccion y resumen de los primeros resultados.</summary>
    /// <summary>Lo que devuelve una busqueda que no encontro nada en ningun sitio (el freno del agente lo reconoce).</summary>
    public const string NoResults = "[no results]";

    /// <summary>
    /// Buscar en internet. Primero DuckDuckGo; si no da nada o pone su pagina anti-robots (pasa: el
    /// 2026-10-22 la devolvia a todas las busquedas y la IA se quedaba buscando en bucle), Stack Overflow
    /// y Wikipedia por sus API abiertas. Si tampoco, se le dice a la IA que no insista con palabras parecidas.
    /// </summary>
    private static async Task<string> SearchAsync(JsonObject args, CancellationToken cancel)
    {
        var query = Tool.Arg(args, "query").Trim();
        if (query.Length == 0) throw new ArgumentException("Empty query");
        var blocked = false;
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, "https://html.duckduckgo.com/html/?q=" + Uri.EscapeDataString(query));
            request.Headers.TryAddWithoutValidation("Accept-Language", System.Globalization.CultureInfo.CurrentUICulture.Name + ",en;q=0.7");
            using var response = await Engine.Downloader.Http.SendAsync(request, cancel);
            response.EnsureSuccessStatusCode();
            var html = await response.Content.ReadAsStringAsync(cancel);
            var text = SearchResultsText(html);
            if (text != NoResults) return text;
            blocked = IsSearchBlocked(html);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            blocked = true;
            Services.DiagLog.Write("agente", "DuckDuckGo no responde: " + ex.Message);
        }
        if (blocked) Services.DiagLog.Write("agente", "DuckDuckGo bloquea la busqueda; se prueba con Stack Overflow y Wikipedia");
        var language = System.Globalization.CultureInfo.CurrentUICulture.TwoLetterISOLanguageName;
        var others = await Task.WhenAll(
            Fallback(() => StackOverflowAsync(query, cancel)),
            Fallback(() => WikipediaAsync(query, language, cancel)),
            language == "en" ? Task.FromResult(string.Empty) : Fallback(() => WikipediaAsync(query, "en", cancel)));
        var found = string.Join(string.Empty, others.Where(o => o.Length > 0));
        if (found.Length > 0)
            return (blocked ? "(The web search engine blocked the request; these come from Stack Overflow and Wikipedia.)\n" : string.Empty) + found;
        return NoResults + (blocked ? " The web search engine is blocking searches right now." : string.Empty)
               + " Do not repeat the search with similar words: try a different approach (fetch_url on the official documentation or source code you know, broader or English terms once), or answer with what you know and say what you could not find.";

        static async Task<string> Fallback(Func<Task<string>> search)
        {
            try { return await search(); }
            catch (Exception ex) when (ex is not OperationCanceledException) { Services.DiagLog.Write("agente", "busqueda alternativa fallida: " + ex.Message); return string.Empty; }
        }
    }

    /// <summary>La pagina anti-robots de DuckDuckGo (desafio «anomaly») en vez de resultados.</summary>
    public static bool IsSearchBlocked(string html) => html.Contains("anomaly", StringComparison.OrdinalIgnoreCase) || html.Contains("captcha", StringComparison.OrdinalIgnoreCase);

    /// <summary>Preguntas de Stack Overflow por relevancia (API publica de Stack Exchange, sin clave).</summary>
    private static async Task<string> StackOverflowAsync(string query, CancellationToken cancel)
    {
        var url = "https://api.stackexchange.com/2.3/search/advanced?order=desc&sort=relevance&pagesize=5&site=stackoverflow&q=" + Uri.EscapeDataString(query);
        using var response = await Engine.Downloader.Http.GetAsync(url, cancel);
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(cancel);
        return StackOverflowText(await ReadMaybeGzipAsync(stream, cancel));
    }

    /// <summary>La API de Stack Exchange contesta siempre en gzip.</summary>
    private static async Task<string> ReadMaybeGzipAsync(Stream stream, CancellationToken cancel)
    {
        using var buffer = new MemoryStream();
        await stream.CopyToAsync(buffer, cancel);
        var bytes = buffer.ToArray();
        if (bytes.Length > 2 && bytes[0] == 0x1f && bytes[1] == 0x8b)
        {
            using var gz = new System.IO.Compression.GZipStream(new MemoryStream(bytes), System.IO.Compression.CompressionMode.Decompress);
            using var reader = new StreamReader(gz, Encoding.UTF8);
            return await reader.ReadToEndAsync(cancel);
        }
        return Encoding.UTF8.GetString(bytes);
    }

    public static string StackOverflowText(string json)
    {
        var sb = new StringBuilder();
        if (JsonNode.Parse(json)?["items"] is not JsonArray items) return string.Empty;
        foreach (var item in items.OfType<JsonObject>().Take(5))
        {
            var title = System.Net.WebUtility.HtmlDecode(item["title"]?.GetValue<string>() ?? string.Empty);
            var link = item["link"]?.GetValue<string>() ?? string.Empty;
            if (title.Length == 0 || link.Length == 0) continue;
            var answered = item["is_answered"]?.GetValue<bool>() == true ? "answered" : "unanswered";
            var tags = item["tags"] is JsonArray t ? string.Join(", ", t.Select(x => x?.GetValue<string>())) : string.Empty;
            sb.AppendLine($"- Stack Overflow ({answered}): {title}\n   {link}\n   tags: {tags}");
        }
        return sb.ToString();
    }

    /// <summary>Articulos de Wikipedia en ese idioma (API publica).</summary>
    private static async Task<string> WikipediaAsync(string query, string language, CancellationToken cancel)
    {
        var url = $"https://{language}.wikipedia.org/w/api.php?action=query&list=search&format=json&srlimit=3&srsearch=" + Uri.EscapeDataString(query);
        using var response = await Engine.Downloader.Http.GetAsync(url, cancel);
        response.EnsureSuccessStatusCode();
        return WikipediaText(await response.Content.ReadAsStringAsync(cancel), language);
    }

    public static string WikipediaText(string json, string language)
    {
        var sb = new StringBuilder();
        if (JsonNode.Parse(json)?["query"]?["search"] is not JsonArray items) return string.Empty;
        foreach (var item in items.OfType<JsonObject>())
        {
            var title = item["title"]?.GetValue<string>() ?? string.Empty;
            if (title.Length == 0) continue;
            var snippet = Regex.Replace(System.Net.WebUtility.HtmlDecode(Regex.Replace(item["snippet"]?.GetValue<string>() ?? string.Empty, "<[^>]+>", string.Empty)), @"\s+", " ").Trim();
            sb.AppendLine($"- Wikipedia ({language}): {title}\n   https://{language}.wikipedia.org/wiki/{Uri.EscapeDataString(title.Replace(' ', '_'))}\n   {snippet}");
        }
        return sb.ToString();
    }

    /// <summary>Los primeros 8 resultados de la pagina HTML de DuckDuckGo, sin anuncios y con la direccion real.</summary>
    public static string SearchResultsText(string html)
    {
        var sb = new StringBuilder();
        var n = 0;
        foreach (Match m in Regex.Matches(html, @"<a[^>]+class=""result__a""[^>]+href=""([^""]+)""[^>]*>(.*?)</a>.*?(?:<a[^>]+class=""result__snippet""[^>]*>(.*?)</a>|$)", RegexOptions.Singleline))
        {
            var href = System.Net.WebUtility.HtmlDecode(m.Groups[1].Value);
            // DuckDuckGo envuelve el destino en /l/?uddg=<url>
            var uddg = Regex.Match(href, @"[?&]uddg=([^&]+)");
            if (uddg.Success) href = Uri.UnescapeDataString(uddg.Groups[1].Value);
            if (href.Contains("duckduckgo.com/y.js")) continue;   // anuncios
            var title = Clean(m.Groups[2].Value);
            var snippet = Clean(m.Groups[3].Value);
            sb.AppendLine($"{++n}. {title}\n   {href}\n   {snippet}");
            if (n >= 8) break;
        }
        return n == 0 ? NoResults : sb.ToString();

        static string Clean(string s) => Regex.Replace(System.Net.WebUtility.HtmlDecode(Regex.Replace(s, "<[^>]+>", string.Empty)), @"\s+", " ").Trim();
    }

    private static async Task<string> FetchAsync(JsonObject args, CancellationToken cancel)
    {
        var url = Tool.Arg(args, "url");
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || (uri.Scheme != "http" && uri.Scheme != "https"))
            throw new ArgumentException("Only http(s) addresses");
        // Cabeceras de navegador: muchas webs devuelven 403 a un cliente desconocido.
        using var request = new HttpRequestMessage(HttpMethod.Get, uri);
        request.Headers.TryAddWithoutValidation("User-Agent", BrowserUserAgent);
        request.Headers.TryAddWithoutValidation("Accept", "text/html,application/xhtml+xml,application/xml;q=0.9,*/*;q=0.8");
        request.Headers.TryAddWithoutValidation("Accept-Language", System.Globalization.CultureInfo.CurrentUICulture.Name + ",en;q=0.7");
        request.Headers.TryAddWithoutValidation("Accept-Encoding", "gzip, deflate, br");
        using var response = await Engine.Downloader.Http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancel);
        var type = response.Content.Headers.ContentType?.MediaType ?? string.Empty;
        var bytes = Decompress(await response.Content.ReadAsByteArrayAsync(cancel), response.Content.Headers.ContentEncoding);
        if (type.Length > 0 && !type.StartsWith("text/") && !type.Contains("json") && !type.Contains("xml") && !type.Contains("javascript"))
            return $"[{(int)response.StatusCode}] {type}, {bytes.Length:N0} bytes (binary content not shown)";
        var text = Encoding.UTF8.GetString(bytes);
        var isHtml = type.Contains("html") || (!type.Contains("json") && text.TrimStart().StartsWith('<'));
        if (isHtml) text = HtmlToText(text);
        // Bloqueada (403…) o un esqueleto que el JavaScript rellena: se reintenta con el navegador headless.
        var blocked = (int)response.StatusCode is 401 or 403 or 429 or 451 or 503;
        if ((blocked || (isHtml && text.Length < 300)) && HeadlessRenderer is { } render)
        {
            try
            {
                if (await render(uri.ToString(), cancel) is { Length: > 0 } dom)
                {
                    var rendered = HtmlToText(dom);
                    if (rendered.Length > text.Length)
                        return "[rendered with a headless browser]\n" + Cut(rendered);
                }
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception) { }
        }
        if (blocked && text.Length < 300)
            return $"[{(int)response.StatusCode}] the site refused the request" + (HeadlessRenderer is not null ? " and the headless browser could not read it either" : "; try another source") + ".";
        return $"[{(int)response.StatusCode}]\n" + Cut(text);
    }

    /// <summary>Con qué leer una página cuando el GET normal falla (navegador headless); las pruebas lo sustituyen.</summary>
    internal static Func<string, CancellationToken, Task<string?>>? HeadlessRenderer = Services.HeadlessBrowser.RenderAsync;

    /// <summary>Un User-Agent de navegador normal, para que las webs no rechacen la lectura.</summary>
    internal const string BrowserUserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/141.0.0.0 Safari/537.36";

    /// <summary>Descomprime el cuerpo si vino en gzip, deflate o brotli (algunas webs lo mandan aunque no se pida).</summary>
    private static byte[] Decompress(byte[] bytes, IEnumerable<string> encodings)
    {
        var enc = encodings.Select(e => e.ToLowerInvariant()).ToList();
        if (!enc.Contains("br") && !enc.Contains("gzip") && !enc.Contains("deflate")) return bytes;
        try
        {
            using var input = new MemoryStream(bytes);
            using System.IO.Stream stream = enc.Contains("br") ? new System.IO.Compression.BrotliStream(input, System.IO.Compression.CompressionMode.Decompress)
                : enc.Contains("gzip") ? new System.IO.Compression.GZipStream(input, System.IO.Compression.CompressionMode.Decompress)
                : new System.IO.Compression.DeflateStream(input, System.IO.Compression.CompressionMode.Decompress);
            using var output = new MemoryStream();
            stream.CopyTo(output);
            return output.ToArray();
        }
        catch (Exception) { return bytes; }
    }

    /// <summary>Una página HTML reducida a texto legible: fuera scripts y etiquetas, con el título delante.</summary>
    internal static string HtmlToText(string html)
    {
        var title = Regex.Match(html, @"<title[^>]*>(.*?)</title>", RegexOptions.Singleline | RegexOptions.IgnoreCase);
        var text = Regex.Replace(html, @"<(script|style|noscript|svg|head)[^>]*>.*?</\1>", string.Empty, RegexOptions.Singleline | RegexOptions.IgnoreCase);
        text = Regex.Replace(text, @"<br\s*/?>|</(p|div|li|h\d|tr|section|article|header|footer)>", "\n", RegexOptions.IgnoreCase);
        text = Regex.Replace(text, "<[^>]+>", string.Empty);
        text = System.Net.WebUtility.HtmlDecode(text);
        text = Regex.Replace(text, @"[ \t]+", " ");
        text = Regex.Replace(text, @"(\s*\n\s*){2,}", "\n\n").Trim();
        var heading = title.Success ? System.Net.WebUtility.HtmlDecode(Regex.Replace(title.Groups[1].Value, "<[^>]+>", string.Empty)).Trim() : string.Empty;
        return heading.Length > 0 && !text.StartsWith(heading, StringComparison.Ordinal) ? heading + "\n\n" + text : text;
    }

    private static string DocumentsText()
    {
        var files = DocumentIndex.Files();
        if (files.Count == 0) return $"[no documents in {DocumentIndex.Folder}]";
        var sb = new StringBuilder();
        foreach (var f in files) sb.AppendLine($"{f.Length,12:N0}  {DocumentIndex.Relative(f.FullName)}");
        return Cut(sb.ToString());
    }

    private static string SaveDocumentText(string name, string content)
    {
        name = name.Trim();
        if (name.Length == 0 || name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || name.Contains("..")) throw new ArgumentException("Bad file name");
        var ext = Path.GetExtension(name).ToLowerInvariant();
        if (ext.Length == 0) { ext = ".md"; name += ext; }
        Directory.CreateDirectory(DocumentIndex.Folder);
        var path = Path.Combine(DocumentIndex.Folder, name);
        if (ext == ".docx") Docx.Write(path, content);
        else File.WriteAllText(path, content, new UTF8Encoding(false));
        return $"Saved: {path}";
    }

    /// <summary>Un fichero como texto: PDF y .docx por su extractor; lo demas tal cual.</summary>
    private static string ReadAnyText(string path) =>
        Path.GetExtension(path).ToLowerInvariant() is ".pdf" or ".docx" ? DocumentIndex.ReadText(path) : File.ReadAllText(path);

    private static string ReadDocumentText(string name)
    {
        var file = DocumentIndex.Files().FirstOrDefault(f => string.Equals(DocumentIndex.Relative(f.FullName), name.Trim(), StringComparison.OrdinalIgnoreCase)
                                                          || string.Equals(f.Name, name.Trim(), StringComparison.OrdinalIgnoreCase))
                   ?? throw new FileNotFoundException($"No document named {name}; use list_documents");
        return Cut(DocumentIndex.ReadText(file.FullName));
    }

    private static string SystemInfoText()
    {
        var pc = Engine.PcProfile.Detect(App.Engine.Accelerator);
        var sb = new StringBuilder();
        sb.AppendLine($"Windows: {Environment.OSVersion.VersionString} ({(Environment.Is64BitOperatingSystem ? "64-bit" : "32-bit")})");
        sb.AppendLine($"Computer: {Environment.MachineName}, user: {Environment.UserName}");
        sb.AppendLine($"Processor: {pc.Cpu} ({pc.Cores} threads); RAM: {pc.RamBytes / (1024.0 * 1024 * 1024):0.0} GB" + (pc.Gpu is not null ? $"; GPU: {pc.Gpu} ({pc.VramBytes / (1024.0 * 1024 * 1024):0.0} GB)" : string.Empty));
        foreach (var d in DriveInfo.GetDrives().Where(d => d.IsReady))
            sb.AppendLine($"Drive {d.Name} {d.DriveType}: {d.AvailableFreeSpace / (1024.0 * 1024 * 1024):0.0} GB free of {d.TotalSize / (1024.0 * 1024 * 1024):0.0} GB");
        sb.AppendLine($"Now: {DateTime.Now:yyyy-MM-dd HH:mm} ({TimeZoneInfo.Local.DisplayName})");
        sb.AppendLine($"Working folder: {AppSettings.Current.WorkFolder}");
        return sb.ToString();
    }

    private static string OnUi(Func<string> action) => Application.Current.Dispatcher.CheckAccess() ? action() : Application.Current.Dispatcher.Invoke(action);

    private static JsonObject Params(params (string Name, string Description)[] properties)
    {
        var props = new JsonObject();
        foreach (var (name, description) in properties)
            props[name] = new JsonObject { ["type"] = "string", ["description"] = description };
        // Todas llevan un «porque» opcional: es lo que se le enseña al usuario al pedir permiso.
        props.TryAdd("reason", new JsonObject { ["type"] = "string", ["description"] = "One short sentence, in the user's language, saying why." });
        var o = new JsonObject { ["type"] = "object", ["properties"] = props };
        if (properties.Length > 0)
            o["required"] = new JsonArray(properties.Select(p => (JsonNode)p.Name).ToArray());
        return o;
    }
}
