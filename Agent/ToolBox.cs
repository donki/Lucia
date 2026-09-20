using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Windows;
using SocLucia.Services;

namespace SocLucia.Agent;

/// <summary>Un tipo de recurso del PC. Cada herramienta pertenece a uno y los permisos se dan por recurso.</summary>
public enum Resource { Commands, FilesRead, FilesWrite, Internet, Clipboard, Apps, Free }

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
        _ => string.Empty,
    };

    internal static string Arg(JsonObject args, string name) => args[name]?.GetValue<string>() ?? string.Empty;
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

    private const int MaxChars = 12_000;

    public static readonly Tool[] All =
    [
        new(RunCommand, Resource.Commands,
            "Run one PowerShell command line on the user's Windows PC and return its output (stdout and stderr). Use it for programs, git, dotnet, python, etc.",
            Params(("command", "The PowerShell command line to run.")),
            async (a, c) => { var r = await CommandTool.RunAsync(Tool.Arg(a, "command"), AppSettings.Current.WorkFolder, c); return (r.TimedOut ? "[timed out after 3 minutes]\n" : $"[exit code {r.ExitCode}]\n") + r.Output; }),
        new(ReadFile, Resource.FilesRead,
            "Read a text file from the PC and return its content (UTF-8, long files are cut).",
            Params(("path", "Full path, or relative to the working folder.")),
            (a, _) => Task.FromResult(Cut(File.ReadAllText(Full(Tool.Arg(a, "path")))))),
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
            (_, _) => Task.FromResult(OnUi(() => Clipboard.ContainsText() ? Cut(Clipboard.GetText()) : "[the clipboard has no text]"))),
        new(WriteClipboard, Resource.Clipboard,
            "Put text in the Windows clipboard so the user can paste it.",
            Params(("text", "The text to copy.")),
            (a, _) => Task.FromResult(OnUi(() => { Clipboard.SetText(Tool.Arg(a, "text")); return "Copied to the clipboard."; }))),
        new(OpenItem, Resource.Apps,
            "Open a file, folder, program or web address with the program Windows uses for it (like double-clicking it).",
            Params(("target", "Path or URL to open.")),
            (a, _) => { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(Tool.Arg(a, "target")) { UseShellExecute = true }); return Task.FromResult("Opened."); }),
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
    ];

    public static Tool? Find(string name) => name == SystemInfo ? Info : All.FirstOrDefault(t => t.Name == name) ?? Free.FirstOrDefault(t => t.Name == name);

    /// <summary>Modo preguntas: las mismas herramientas que el agente (con sus permisos); cambia la consigna, no el alcance.</summary>
    public static JsonArray QuestionDefinitions(AppSettings settings) => Definitions(settings);

    /// <summary>Consigna del modo preguntas: responder; herramientas solo si la pregunta lo pide.</summary>
    public static string QuestionPrompt(AppSettings settings)
        => "Answer the user directly. You also have tools for this PC (files, commands, clipboard, opening things), the user's documents, scheduling and memory: " +
           "use one only when the request clearly needs it (a file, a command, a document, a reminder), one step at a time, and give a short reason; the user approves each action. " +
           $"The working folder is `{settings.WorkFolder}`. Never destroy data unless asked for exactly that.";

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
        return string.Join("\n\n", parts);
    }

    /// <summary>Instrucciones del modo preguntas con internet: buscar solo cuando hace falta.</summary>
    public const string InternetPrompt =
        "You can look things up on the internet with `web_search` (top results) and `fetch_url` (read a page). " +
        "Use them when the answer needs current, local or verifiable information (news, prices, versions, documentation, facts you are unsure of); " +
        "answer directly from what you know otherwise. Cite the address of what you used.";

    /// <summary>Modo agente: todo lo que no este denegado en Ajustes, mas las libres.</summary>
    public static JsonArray Definitions(AppSettings settings)
        => Definitions(settings, All.Where(t => settings.PermissionFor(t.Resource) != Permission.Deny).Append(Info).Concat(FreeTools(settings)));

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
               "When you are done, answer the user in their language and summarise what you did.";
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
    private static async Task<string> SearchAsync(JsonObject args, CancellationToken cancel)
    {
        var query = Tool.Arg(args, "query").Trim();
        if (query.Length == 0) throw new ArgumentException("Empty query");
        using var request = new HttpRequestMessage(HttpMethod.Get, "https://html.duckduckgo.com/html/?q=" + Uri.EscapeDataString(query));
        request.Headers.TryAddWithoutValidation("Accept-Language", System.Globalization.CultureInfo.CurrentUICulture.Name + ",en;q=0.7");
        using var response = await Engine.Downloader.Http.SendAsync(request, cancel);
        response.EnsureSuccessStatusCode();
        var html = await response.Content.ReadAsStringAsync(cancel);
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
        return n == 0 ? "[no results]" : sb.ToString();

        static string Clean(string s) => Regex.Replace(System.Net.WebUtility.HtmlDecode(Regex.Replace(s, "<[^>]+>", string.Empty)), @"\s+", " ").Trim();
    }

    private static async Task<string> FetchAsync(JsonObject args, CancellationToken cancel)
    {
        var url = Tool.Arg(args, "url");
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || (uri.Scheme != "http" && uri.Scheme != "https"))
            throw new ArgumentException("Only http(s) addresses");
        using var response = await Engine.Downloader.Http.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, cancel);
        var type = response.Content.Headers.ContentType?.MediaType ?? string.Empty;
        var bytes = await response.Content.ReadAsByteArrayAsync(cancel);
        if (!type.StartsWith("text/") && !type.Contains("json") && !type.Contains("xml") && !type.Contains("javascript"))
            return $"[{(int)response.StatusCode}] {type}, {bytes.Length:N0} bytes (binary content not shown)";
        var text = Encoding.UTF8.GetString(bytes);
        if (type.Contains("html"))
        {
            text = Regex.Replace(text, @"<(script|style)[^>]*>.*?</\1>", string.Empty, RegexOptions.Singleline | RegexOptions.IgnoreCase);
            text = Regex.Replace(text, @"<br\s*/?>|</(p|div|li|h\d|tr)>", "\n", RegexOptions.IgnoreCase);
            text = Regex.Replace(text, "<[^>]+>", string.Empty);
            text = System.Net.WebUtility.HtmlDecode(text);
            text = Regex.Replace(text, @"[ \t]+", " ");
            text = Regex.Replace(text, @"(\s*\n\s*){2,}", "\n\n").Trim();
        }
        return $"[{(int)response.StatusCode}]\n" + Cut(text);
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
