using System.Net;
using System.Net.Sockets;
using System.Runtime.CompilerServices;
using SocLucia.Services;

[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace SocLucia.Tests;

/// <summary>
/// Arenero de las pruebas: antes de tocar nada, <c>SOC_LUCIA_ROOT</c> apunta a una carpeta temporal
/// (la aplicacion solo lo mira en Debug) y los ajustes de documentos y trabajo tambien. Si por lo que
/// sea la raiz no es la del arenero (compilado en Release), las pruebas revientan antes de escribir
/// nada en los datos de verdad. Al acabar se restaura la variable y se borra la carpeta.
/// </summary>
internal static class Sandbox
{
    public static string Root { get; private set; } = string.Empty;
    public static string Documents => Path.Combine(Root, "docs");
    public static string Work => Path.Combine(Root, "work");

    private static string? _previous;

    [ModuleInitializer]
    internal static void Init()
    {
        _previous = Environment.GetEnvironmentVariable("SOC_LUCIA_ROOT");
        Root = Path.Combine(Path.GetTempPath(), "LuciaTests-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(Root);
        Environment.SetEnvironmentVariable("SOC_LUCIA_ROOT", Root);
        if (!string.Equals(Paths.Root, Root, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"Paths.Root = {Paths.Root}: las pruebas deben compilarse en Debug para no tocar los datos reales.");
        ResetSettings();
        AppDomain.CurrentDomain.ProcessExit += (_, _) =>
        {
            Environment.SetEnvironmentVariable("SOC_LUCIA_ROOT", _previous);
            try { Directory.Delete(Root, recursive: true); } catch (Exception) { }
        };
    }

    /// <summary>Ajustes limpios con las carpetas del arenero (Current tiene el setter privado: se pisa campo a campo).</summary>
    public static AppSettings ResetSettings()
    {
        var s = AppSettings.Current;
        Directory.CreateDirectory(Documents);
        Directory.CreateDirectory(Work);
        s.DocumentsFolder = Documents;
        s.WorkFolder = Work;
        s.ModelsFolder = null;
        s.ModelPath = null;
        s.ModelName = null;
        s.ModelLicense = null;
        s.MemoryEnabled = true;
        s.InternetAccess = true;
        s.Permissions.Clear();
        s.Installed.Clear();
        s.Adapters.Clear();
        s.BrokenModels.Clear();
        s.McpServers.Clear();
        s.DisabledSkills.Clear();
        s.RemoteModels.Clear();
        s.ActiveRemoteModel = null;
        s.RemoteProviderKeys.Clear();
        s.ProviderAccountIds.Clear();
        s.RemoteOptIn = false;
        SocLucia.Mcp.McpManager.DisconnectAll();
        try { if (Directory.Exists(SkillStore.Folder)) Directory.Delete(SkillStore.Folder, recursive: true); } catch (Exception) { }
        s.Pending = null;   // una descarga a medias de otra prueba no se retoma en esta
        s.PendingDownloads.Clear();
        s.ContextTokens = Engine.TokenLimits.ContextDefault;
        s.AutoContext = false;   // las pruebas fijan el contexto a mano; el automatico se prueba aparte
        s.MaxAnswerTokens = Engine.TokenLimits.AnswerDefault;
        s.EditorDoor = new EditorDoorSettings { Token = "token-de-prueba" };
        return s;
    }

    /// <summary>Una carpeta vacia dentro del arenero.</summary>
    public static string NewFolder(string name)
    {
        var path = Path.Combine(Root, name + "-" + Guid.NewGuid().ToString("N")[..6]);
        Directory.CreateDirectory(path);
        return path;
    }

    /// <summary>Vacia la carpeta de documentos.</summary>
    public static void ClearDocuments()
    {
        if (Directory.Exists(Documents)) Directory.Delete(Documents, recursive: true);
        Directory.CreateDirectory(Documents);
    }

    public static int FreePort()
    {
        var l = new TcpListener(IPAddress.Loopback, 0);
        l.Start();
        var port = ((IPEndPoint)l.LocalEndpoint).Port;
        l.Stop();
        return port;
    }

    /// <summary>Ejecuta algo en un hilo STA (WPF lo exige para crear controles).</summary>
    public static T Sta<T>(Func<T> action)
    {
        T result = default!;
        Exception? error = null;
        var thread = new Thread(() => { try { result = action(); } catch (Exception ex) { error = ex; } });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (error is not null) throw new AggregateException(error);
        return result;
    }
}

/// <summary>Un servidor HTTP de juguete en 127.0.0.1 y un puerto libre, para hablar con los clientes de la app sin red.</summary>
internal sealed class LocalServer : IDisposable
{
    private readonly HttpListener _listener = new();
    private readonly Func<HttpListenerContext, Task> _handler;
    public string BaseUrl { get; }
    public List<string> Bodies { get; } = [];
    public List<HttpListenerRequest> Requests { get; } = [];

    public LocalServer(Func<HttpListenerContext, Task> handler)
    {
        _handler = handler;
        var port = Sandbox.FreePort();
        BaseUrl = $"http://127.0.0.1:{port}";
        _listener.Prefixes.Add(BaseUrl + "/");
        _listener.Start();
        _ = LoopAsync();
    }

    private async Task LoopAsync()
    {
        while (_listener.IsListening)
        {
            HttpListenerContext ctx;
            try { ctx = await _listener.GetContextAsync(); }
            catch (Exception) { return; }
            string body;
            using (var reader = new StreamReader(ctx.Request.InputStream))
                body = await reader.ReadToEndAsync();
            lock (Bodies) { Bodies.Add(body); Requests.Add(ctx.Request); }
            try { await _handler(ctx); }
            catch (Exception) { try { ctx.Response.StatusCode = 500; ctx.Response.Close(); } catch (Exception) { } }
        }
    }

    public static async Task Write(HttpListenerContext ctx, int status, string contentType, string body)
    {
        var bytes = System.Text.Encoding.UTF8.GetBytes(body);
        ctx.Response.StatusCode = status;
        ctx.Response.ContentType = contentType;
        ctx.Response.ContentLength64 = bytes.Length;
        await ctx.Response.OutputStream.WriteAsync(bytes);
        ctx.Response.Close();
    }

    public void Dispose()
    {
        try { _listener.Stop(); _listener.Close(); } catch (Exception) { }
    }
}
