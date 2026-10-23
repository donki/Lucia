using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using SocLucia.Engine;
using SocLucia.Services;

namespace SocLucia.Mcp;

/// <summary>Una herramienta que ofrece un servidor MCP: su nombre, para qué sirve y el esquema de sus argumentos.</summary>
public sealed record McpTool(string Name, string Description, JsonObject Schema);

/// <summary>Lo que el servidor dice de sí mismo al conectar.</summary>
public sealed record McpServerInfo(string Name, string Version, string ProtocolVersion);

/// <summary>El servidor MCP devolvió un error (lo normal: una herramienta que falla). Se le cuenta a la IA como salida.</summary>
public sealed class McpException(string message) : Exception(message);

/// <summary>
/// Cliente MCP (Model Context Protocol) por stdio: arranca el servidor como proceso hijo y habla con él
/// en JSON-RPC 2.0, un mensaje JSON por línea. Descubre sus herramientas (<c>tools/list</c>) y las
/// ejecuta (<c>tools/call</c>). Es el mismo protocolo que usan Claude Desktop, VS Code, etc., así que
/// cualquier servidor MCP de stdio vale (muchos se instalan solos con <c>npx</c> o <c>uvx</c>).
/// </summary>
public sealed class McpClient : IDisposable
{
    private readonly string _name;
    private Process? _process;
    private StreamWriter? _stdin;
    private long _nextId;
    private readonly ConcurrentDictionary<long, TaskCompletionSource<JsonNode?>> _pending = new();
    private readonly object _writeLock = new();
    private volatile bool _down;

    public McpClient(string name) => _name = name;

    public McpServerInfo? Info { get; private set; }

    /// <summary>Protocolo que pedimos. Si el servidor usa otro, lo dice en su respuesta y seguimos con el suyo.</summary>
    private const string ProtocolVersion = "2025-06-18";

    public static ProcessStartInfo BuildStartInfo(string command, IEnumerable<string> args, IReadOnlyDictionary<string, string>? env)
    {
        var info = new ProcessStartInfo(command)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
            WorkingDirectory = AppSettings.Current.WorkFolder,
        };
        foreach (var a in args) info.ArgumentList.Add(a);
        if (env is not null)
            foreach (var (k, v) in env) info.Environment[k] = v;
        return info;
    }

    /// <summary>Arranca el servidor, hace el saludo (initialize) y devuelve lo que dice ser.</summary>
    public async Task<McpServerInfo> StartAsync(string command, IReadOnlyList<string> args, IReadOnlyDictionary<string, string>? env, CancellationToken cancel)
    {
        var info = BuildStartInfo(command, args, env);
        Process process;
        try
        {
            process = Process.Start(info) ?? throw new McpException("no se pudo arrancar");
        }
        catch (Exception ex) when (ex is not McpException)
        {
            throw new McpException(FriendlyStart(command, ex));
        }
        _process = process;
        ChildJob.Add(process);   // muere con la aplicacion
        _stdin = process.StandardInput;
        _ = Task.Run(() => ReadLoopAsync(process.StandardOutput), CancellationToken.None);
        _ = Task.Run(() => ErrorLoopAsync(process.StandardError), CancellationToken.None);
        process.Exited += (_, _) => Fail(new McpException(Localization.Loc.Format("McpExited", _name)));
        process.EnableRaisingEvents = true;

        var init = await RequestAsync("initialize", new JsonObject
        {
            ["protocolVersion"] = ProtocolVersion,
            ["capabilities"] = new JsonObject(),
            ["clientInfo"] = new JsonObject { ["name"] = "sOC Lucia", ["version"] = typeof(McpClient).Assembly.GetName().Version?.ToString() ?? "1" },
        }, TimeSpan.FromSeconds(30), cancel);
        var serverInfo = init?["serverInfo"] as JsonObject;
        Info = new McpServerInfo(serverInfo?["name"]?.GetValue<string>() ?? _name, serverInfo?["version"]?.GetValue<string>() ?? "?",
            init?["protocolVersion"]?.GetValue<string>() ?? ProtocolVersion);
        Notify("notifications/initialized", null);
        return Info;
    }

    /// <summary>Las herramientas del servidor (sigue las páginas si las hay).</summary>
    public async Task<List<McpTool>> ListToolsAsync(CancellationToken cancel)
    {
        var tools = new List<McpTool>();
        string? cursor = null;
        do
        {
            var result = await RequestAsync("tools/list", cursor is null ? new JsonObject() : new JsonObject { ["cursor"] = cursor }, TimeSpan.FromSeconds(30), cancel);
            if (result?["tools"] is JsonArray array)
                foreach (var t in array.OfType<JsonObject>())
                {
                    var name = t["name"]?.GetValue<string>();
                    if (string.IsNullOrEmpty(name)) continue;
                    var schema = t["inputSchema"] as JsonObject ?? t["input_schema"] as JsonObject;
                    tools.Add(new McpTool(name, t["description"]?.GetValue<string>() ?? string.Empty, CleanSchema(schema)));
                }
            cursor = result?["nextCursor"]?.GetValue<string>();
        }
        while (!string.IsNullOrEmpty(cursor) && tools.Count < 500);
        return tools;
    }

    /// <summary>Ejecuta una herramienta y devuelve su salida como texto (lo que la IA lee).</summary>
    public async Task<string> CallToolAsync(string tool, JsonObject arguments, CancellationToken cancel)
    {
        var result = await RequestAsync("tools/call", new JsonObject { ["name"] = tool, ["arguments"] = arguments.DeepClone() }, TimeSpan.FromMinutes(2), cancel);
        var text = TextOf(result?["content"] as JsonArray);
        if (result?["isError"]?.GetValue<bool>() == true)
            return "[error] " + (text.Length > 0 ? text : "the tool reported an error");
        return text.Length > 0 ? text : "[ok]";
    }

    /// <summary>El texto de un resultado MCP (los trozos de tipo «text»; lo demás se resume).</summary>
    public static string TextOf(JsonArray? content)
    {
        if (content is null) return string.Empty;
        var parts = new List<string>();
        foreach (var part in content.OfType<JsonObject>())
        {
            var type = part["type"]?.GetValue<string>();
            if (type == "text") parts.Add(part["text"]?.GetValue<string>() ?? string.Empty);
            else if (type == "resource" && part["resource"]?["text"] is JsonValue rv) parts.Add(rv.GetValue<string>());
            else if (type is not null) parts.Add($"[{type}]");
        }
        return string.Join('\n', parts).Trim();
    }

    /// <summary>Quita de un esquema lo que algunos modelos no digieren ($schema, additionalProperties) y garantiza que sea un objeto.</summary>
    internal static JsonObject CleanSchema(JsonObject? schema)
    {
        if (schema is null) return new JsonObject { ["type"] = "object", ["properties"] = new JsonObject() };
        var copy = schema.DeepClone().AsObject();
        copy.Remove("$schema");
        if (copy["type"] is null) copy["type"] = "object";
        return copy;
    }

    private async Task<JsonObject?> RequestAsync(string method, JsonObject @params, TimeSpan timeout, CancellationToken cancel)
    {
        if (_down) throw new McpException(Localization.Loc.Format("McpExited", _name));
        var id = Interlocked.Increment(ref _nextId);
        var tcs = new TaskCompletionSource<JsonNode?>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pending[id] = tcs;
        Send(new JsonObject { ["jsonrpc"] = "2.0", ["id"] = id, ["method"] = method, ["params"] = @params });
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancel);
        timeoutCts.CancelAfter(timeout);
        await using var reg = timeoutCts.Token.Register(() => tcs.TrySetException(
            cancel.IsCancellationRequested ? new OperationCanceledException(cancel) : new McpException(Localization.Loc.Format("McpTimeout", _name))));
        try
        {
            return await tcs.Task as JsonObject;
        }
        finally
        {
            _pending.TryRemove(id, out _);
        }
    }

    private void Notify(string method, JsonObject? @params)
        => Send(new JsonObject { ["jsonrpc"] = "2.0", ["method"] = method, ["params"] = @params ?? new JsonObject() });

    private void Send(JsonObject message)
    {
        var line = message.ToJsonString();
        lock (_writeLock)
        {
            if (_down || _stdin is null) return;
            try { _stdin.Write(line); _stdin.Write('\n'); _stdin.Flush(); }
            catch (IOException) { Fail(new McpException(Localization.Loc.Format("McpExited", _name))); }
        }
        Log("→ " + Trim(line));
    }

    private async Task ReadLoopAsync(StreamReader stdout)
    {
        try
        {
            while (await stdout.ReadLineAsync() is { } line)
            {
                line = line.Trim();
                if (line.Length == 0 || line[0] != '{') continue;   // algun servidor escupe texto suelto: se ignora
                Log("← " + Trim(line));
                JsonObject? message;
                try { message = JsonNode.Parse(line) as JsonObject; }
                catch (JsonException) { continue; }
                if (message?["id"] is JsonValue idv && idv.TryGetValue<long>(out var id) && _pending.TryRemove(id, out var tcs))
                {
                    if (message["error"] is JsonObject err)
                        tcs.TrySetException(new McpException(err["message"]?.GetValue<string>() ?? "error"));
                    else
                        tcs.TrySetResult(message["result"]);
                }
                // Las peticiones del servidor hacia nosotros (sampling, roots…) no se admiten en esta version: se ignoran.
            }
        }
        catch (Exception) { }
        Fail(new McpException(Localization.Loc.Format("McpExited", _name)));
    }

    private async Task ErrorLoopAsync(StreamReader stderr)
    {
        try
        {
            while (await stderr.ReadLineAsync() is { } line)
                if (line.Trim().Length > 0) Log("stderr: " + Trim(line));
        }
        catch (Exception) { }
    }

    private void Fail(Exception ex)
    {
        if (_down) return;
        _down = true;
        foreach (var tcs in _pending.Values) tcs.TrySetException(ex);
        _pending.Clear();
    }

    private static string FriendlyStart(string command, Exception ex)
    {
        // «no se encuentra el archivo» (Win32 2): el comando no esta instalado o no esta en el PATH.
        if (ex is System.ComponentModel.Win32Exception { NativeErrorCode: 2 })
            return Localization.Loc.Format("McpCommandNotFound", command);
        return ex.Message;
    }

    private static string Trim(string s) => s.Length > 2000 ? s[..2000] + "…" : s;

    private static readonly object LogLock = new();
    public static string LogPath => Path.Combine(Paths.Logs, "mcp.log");

    private void Log(string line)
    {
        try
        {
            lock (LogLock)
            {
                Directory.CreateDirectory(Paths.Logs);
                var log = new FileInfo(LogPath);
                if (log.Exists && log.Length > 2_000_000) log.Delete();
                File.AppendAllText(LogPath, $"{DateTime.Now:HH:mm:ss} [{_name}] {line}{Environment.NewLine}");
            }
        }
        catch (Exception) { }
    }

    public void Dispose()
    {
        _down = true;
        try { if (_process is { HasExited: false }) _process.Kill(entireProcessTree: true); } catch (Exception) { }
        _process?.Dispose();
        _process = null;
    }
}
