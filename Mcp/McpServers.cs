using System.Collections.Concurrent;
using System.Text;
using System.Text.Json.Nodes;
using SocLucia.Agent;
using SocLucia.Services;

namespace SocLucia.Mcp;

/// <summary>
/// Un servidor MCP configurado: su nombre, la orden que lo arranca y si está encendido. Muchos
/// servidores se instalan y arrancan con una sola orden (<c>npx -y …</c>, <c>uvx …</c>), que los baja
/// la primera vez; otros son un programa que el usuario ya tiene. Las variables de entorno sirven para
/// las claves que algunos piden.
/// </summary>
public sealed class McpServerConfig
{
    public string Name { get; set; } = string.Empty;
    public string Command { get; set; } = string.Empty;
    public List<string> Args { get; set; } = [];
    public Dictionary<string, string> Env { get; set; } = new();
    public bool Enabled { get; set; } = true;

    /// <summary>La orden entera para enseñarla («npx -y @modelcontextprotocol/server-filesystem C:\…»).</summary>
    public string CommandLine => string.Join(" ", new[] { Command }.Concat(Args).Select(Quote));

    private static string Quote(string s) => s.Length > 0 && s.All(c => !char.IsWhiteSpace(c)) ? s : "\"" + s.Replace("\"", "\\\"") + "\"";

    /// <summary>Parte una orden escrita a mano en programa y argumentos (respeta las comillas).</summary>
    public static (string Command, List<string> Args) ParseCommandLine(string line)
    {
        var parts = new List<string>();
        var current = new StringBuilder();
        var quote = '\0';
        foreach (var c in line.Trim())
        {
            if (quote != '\0')
            {
                if (c == quote) quote = '\0';
                else current.Append(c);
            }
            else if (c is '"' or '\'') quote = c;
            else if (char.IsWhiteSpace(c)) { if (current.Length > 0) { parts.Add(current.ToString()); current.Clear(); } }
            else current.Append(c);
        }
        if (current.Length > 0) parts.Add(current.ToString());
        return parts.Count == 0 ? (string.Empty, []) : (parts[0], parts.Skip(1).ToList());
    }
}

/// <summary>Lo que se sabe de un servidor conectado: su información y las herramientas que ofrece.</summary>
public sealed record McpConnection(McpClient Client, McpServerInfo Info, IReadOnlyList<Tool> Tools);

/// <summary>
/// Los servidores MCP encendidos y las herramientas que aportan al agente. Las herramientas MCP se
/// envuelven como <see cref="Tool"/> normales (recurso <see cref="Resource.Mcp"/>), así que pasan por el
/// mismo permiso, registro y burbuja que las de serie. Se conectan la primera vez que se van a usar.
/// </summary>
public static class McpManager
{
    private static readonly ConcurrentDictionary<string, McpConnection> Connections = new();

    /// <summary>El prefijo del nombre con el que el modelo ve una herramienta MCP: <c>mcp__servidor__herramienta</c>.</summary>
    public static string ToolName(string server, string tool) => $"mcp__{Slug(server)}__{tool}";

    private static string Slug(string name)
    {
        var slug = new string(name.Trim().ToLowerInvariant().Select(c => char.IsLetterOrDigit(c) ? c : '_').ToArray());
        slug = System.Text.RegularExpressions.Regex.Replace(slug, "_+", "_").Trim('_');
        return slug.Length == 0 ? "server" : slug;
    }

    /// <summary>Conecta los servidores encendidos que aún no lo estén y descarta los que ya no están configurados.</summary>
    public static async Task EnsureAsync(AppSettings settings, CancellationToken cancel)
    {
        if (settings.PermissionFor(Resource.Mcp) == Permission.Deny) { DisconnectAll(); return; }
        var wanted = settings.McpServers.Where(s => s.Enabled && s.Command.Length > 0).ToList();
        foreach (var name in Connections.Keys.ToList())
            if (!wanted.Any(w => w.Name == name))
                Disconnect(name);
        foreach (var server in wanted)
        {
            if (Connections.ContainsKey(server.Name)) continue;
            try
            {
                var client = new McpClient(server.Name);
                var info = await client.StartAsync(server.Command, server.Args, server.Env, cancel);
                var tools = (await client.ListToolsAsync(cancel)).Select(t => Wrap(server.Name, client, t)).ToList();
                Connections[server.Name] = new McpConnection(client, info, tools);
                DiagLog.Write("mcp", $"«{server.Name}» conectado: {tools.Count} herramientas");
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                DiagLog.Write("mcp", $"«{server.Name}» no conecta: {DiagLog.Describe(ex)}");
            }
        }
    }

    /// <summary>Prueba un servidor sin dejarlo encendido: conecta, lista y se desconecta. Para el botón «Probar».</summary>
    public static async Task<(McpServerInfo Info, IReadOnlyList<McpTool> Tools)> TestAsync(McpServerConfig server, CancellationToken cancel)
    {
        using var client = new McpClient(server.Name);
        var info = await client.StartAsync(server.Command, server.Args, server.Env, cancel);
        return (info, await client.ListToolsAsync(cancel));
    }

    /// <summary>Todas las herramientas de los servidores conectados (envueltas como herramientas normales).</summary>
    public static IEnumerable<Tool> Tools(AppSettings settings)
        => settings.PermissionFor(Resource.Mcp) == Permission.Deny ? [] : Connections.Values.SelectMany(c => c.Tools);

    public static Tool? Find(string name)
        => Connections.Values.SelectMany(c => c.Tools).FirstOrDefault(t => t.Name == name);

    public static IReadOnlyCollection<McpConnection> Connected => Connections.Values.ToList();

    public static McpConnection? ConnectionOf(string server) => Connections.GetValueOrDefault(server);

    private static Tool Wrap(string server, McpClient client, McpTool tool)
        => new(ToolName(server, tool.Name), Resource.Mcp,
            $"[{server}] {tool.Description}".Trim(),
            tool.Schema,
            async (args, cancel) =>
            {
                var clean = args.DeepClone().AsObject();
                clean.Remove("reason");   // algunas IA añaden «reason»; el servidor no lo espera
                return await client.CallToolAsync(tool.Name, clean, cancel);
            });

    public static void Disconnect(string name)
    {
        if (Connections.TryRemove(name, out var conn))
        {
            conn.Client.Dispose();
            DiagLog.Write("mcp", $"«{name}» desconectado");
        }
    }

    public static void DisconnectAll()
    {
        foreach (var name in Connections.Keys.ToList())
            Disconnect(name);
    }
}
