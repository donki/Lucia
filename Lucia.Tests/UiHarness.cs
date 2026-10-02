using System.Diagnostics;
using System.Net;
using System.Runtime.ExceptionServices;
using System.Text;
using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Microsoft.Win32;
using SocLucia.Engine;
using SocLucia.Localization;
using SocLucia.Services;

namespace SocLucia.Tests;

/// <summary>
/// Las ventanas de verdad en un hilo STA propio, con la App y sus recursos (App.xaml). Los
/// dialogos modales se contestan con <see cref="Expect{T}"/>; uno que nadie esperaba se cierra y
/// la prueba falla. Las ventanas no se activan y se abren fuera de la pantalla.
/// </summary>
internal static class Ui
{
    private static readonly Dispatcher Dispatcher;
    private static readonly Queue<(Type Type, Action<Window> Act)> Responders = new();
    private static readonly List<Exception> Errors = [];

    static Ui()
    {
        using var ready = new ManualResetEventSlim();
        Dispatcher? dispatcher = null;
        var thread = new Thread(() =>
        {
            dispatcher = Dispatcher.CurrentDispatcher;
            App.HostedByTests = true;
            // pack://application:,,, tiene que ser la app, no testhost (WPF no deja cambiarlo dos veces).
            typeof(Application).GetField("_resourceAssembly", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)!
                .SetValue(null, typeof(App).Assembly);
            var app = new App();
            app.InitializeComponent();
            app.ShutdownMode = ShutdownMode.OnExplicitShutdown;   // App.xaml dice OnMainWindowClose
            dispatcher.UnhandledException += (_, e) =>
            {
                Errors.Add(e.Exception);
                e.Handled = true;
            };
            foreach (var type in typeof(App).Assembly.GetTypes().Where(t => t.IsSubclassOf(typeof(Window)) && !t.IsAbstract))
                Window.ShowActivatedProperty.OverrideMetadata(type, new FrameworkPropertyMetadata(false));
            EventManager.RegisterClassHandler(typeof(Window), FrameworkElement.LoadedEvent, new RoutedEventHandler(OnLoaded));
            ready.Set();
            Dispatcher.Run();
        }) { IsBackground = true, Name = "Ui" };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        ready.Wait();
        Dispatcher = dispatcher!;
    }

    public static void Run(Action action) => Run(() => { action(); return 0; });

    public static T Run<T>(Func<T> action)
    {
        var done = new ManualResetEventSlim();
        var watchdog = new Thread(() =>
        {
            // Una prueba colgada (un dialogo que no se cierra) no cuelga el banco: se cierra todo y falla.
            if (done.Wait(40_000))
                return;
            Dispatcher.BeginInvoke(() =>
            {
                var open = string.Join(", ", Application.Current.Windows.Cast<Window>().Select(w => $"{w.GetType().Name} «{w.Title}»"));
                Errors.Add(new TimeoutException("La prueba no acabo en 40 s; ventanas abiertas: " + open));
                foreach (var w in Application.Current.Windows.Cast<Window>().Where(w => w.IsVisible).ToList())
                    w.Close();
            });
        }) { IsBackground = true };
        watchdog.Start();
        var result = Dispatcher.Invoke(() =>
        {
            Responders.Clear();
            Errors.Clear();
            try
            {
                return action();
            }
            finally
            {
                foreach (var w in Application.Current.Windows.Cast<Window>().ToList())
                    w.Close();
                DoEvents();
            }
        });
        done.Set();
        var pending = Dispatcher.Invoke(() => (Responders.Count, Errors.ToList()));
        if (pending.Item2.Count > 0)
            ExceptionDispatchInfo.Capture(pending.Item2[0]).Throw();
        Assert.True(pending.Count == 0, $"Quedaron {pending.Count} dialogos esperados sin abrir");
        return result;
    }

    public static int Pending => Responders.Count;

    public static void Expect<T>(Action<T> act) where T : Window => Responders.Enqueue((typeof(T), w => act((T)w)));

    /// <summary>PromptWindow: pulsa el boton con ese tooltip («Yes», «Cancel», «Ok») y devuelve el texto del aviso.</summary>
    public static void Answer(string tooltipKey, Action<string>? message = null, string? text = null) => Expect<PromptWindow>(w =>
    {
        message?.Invoke(All<TextBlock>(w).First().Text);
        if (text is not null)
            All<TextBox>(w).Single().Text = text;
        Click(All<Button>(w).Single(b => Equals(b.ToolTip, Loc.Get(tooltipKey))));
    });

    private static void OnLoaded(object sender, RoutedEventArgs e)
    {
        var w = (Window)sender;
        w.Left = -20000;
        w.Top = -20000;
        if (w is MainWindow && (Responders.Count == 0 || Responders.Peek().Type != typeof(MainWindow)))
            return;
        if (Responders.Count == 0 || !Responders.Peek().Type.IsInstanceOfType(w))
        {
            Errors.Add(new InvalidOperationException($"Dialogo inesperado: {w.GetType().Name} «{w.Title}» {All<TextBlock>(w).FirstOrDefault()?.Text}"));
            w.Dispatcher.BeginInvoke(w.Close);
            return;
        }
        var (_, act) = Responders.Dequeue();
        w.Dispatcher.BeginInvoke(() =>
        {
            try
            {
                act(w);
            }
            catch (Exception ex)
            {
                Errors.Add(ex);
                w.Close();
            }
        }, DispatcherPriority.Background);
    }

    private static readonly System.Reflection.MethodInfo OnClick =
        typeof(ButtonBase).GetMethod("OnClick", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;

    public static bool IsClosed(Window w) => PresentationSource.FromVisual(w) is null && !w.IsVisible;

    public static void Click(ButtonBase button)
    {
        Assert.True(button.IsEnabled, $"{button.Name} esta desactivado");
        OnClick.Invoke(button, null);
    }

    public static T Find<T>(DependencyObject root, string id) where T : DependencyObject =>
        All<T>(root).FirstOrDefault(c => (c as FrameworkElement)?.Name == id || System.Windows.Automation.AutomationProperties.GetAutomationId(c) == id)
        ?? throw new InvalidOperationException($"No hay {typeof(T).Name} «{id}»");

    public static IEnumerable<T> All<T>(DependencyObject root) where T : DependencyObject
    {
        if (root is T t)
            yield return t;
        foreach (var child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>())
            foreach (var c in All<T>(child))
                yield return c;
    }

    public static void WaitFor(Func<bool> condition, int timeoutMs = 15_000)
    {
        var watch = Stopwatch.StartNew();
        while (!condition())
        {
            Assert.True(watch.ElapsedMilliseconds < timeoutMs, "La interfaz no llego al estado esperado");
            DoEvents();
            Thread.Sleep(5);
        }
    }

    public static void Wait(Task task) => WaitFor(() => task.IsCompleted);

    public static void DoEvents()
    {
        var frame = new DispatcherFrame();
        Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.SystemIdle, () => frame.Continue = false);
        Dispatcher.PushFrame(frame);
    }

    public static T Field<T>(object target, string name) =>
        (T)target.GetType().GetField(name, System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(target)!;

    public static object? Call(object target, string name, params object?[] args)
    {
        var method = target.GetType().GetMethods(System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance)
            .Single(m => m.Name == name && m.GetParameters().Length == args.Length);
        try
        {
            return method.Invoke(target, args);
        }
        catch (System.Reflection.TargetInvocationException ex) when (ex.InnerException is not null)
        {
            ExceptionDispatchInfo.Capture(ex.InnerException).Throw();
            throw;
        }
    }
}

/// <summary>Windows de mentira: abrir, dialogos, portapapeles y registro, todo apuntado.</summary>
internal sealed class FakeDesktop : IDesktop
{
    public const string RegistryBranch = @"Software\sOCLucia.Tests";

    public FakeDesktop()
    {
        Registry.CurrentUser.DeleteSubKeyTree(RegistryBranch, throwOnMissingSubKey: false);
        CurrentUser = Registry.CurrentUser.CreateSubKey(RegistryBranch);
    }

    public List<string> Opened { get; } = [];
    public Exception? OpenError { get; set; }
    public Queue<string?> Folders { get; } = new();
    public Queue<string[]?> Files { get; } = new();
    public Queue<string?> Saves { get; } = new();
    public string? Clipboard { get; set; }
    public string[]? ClipboardFileList { get; set; }
    public BitmapSource? Image { get; set; }
    public int Shutdowns { get; private set; }

    public void Open(string target)
    {
        Opened.Add(target);
        if (OpenError is not null)
            throw OpenError;
    }

    public string? PickFolder(Window owner, string? initial, string? title) => Folders.Dequeue();

    public string[]? PickFiles(Window owner, string? title, string filter, bool multiselect) => Files.Dequeue();

    public string? PickSave(Window? owner, string fileName, string filter, string defaultExt) => Saves.Dequeue();

    public bool ClipboardHasText() => Clipboard is not null;

    public string ClipboardText() => Clipboard!;

    public void SetClipboardText(string text) => Clipboard = text;

    public string[]? ClipboardFiles() => ClipboardFileList;

    public BitmapSource? ClipboardImage() => Image;

    public RegistryKey CurrentUser { get; }

    public void Shutdown() => Shutdowns++;

    public void Dispose()
    {
        CurrentUser.Dispose();
        Registry.CurrentUser.DeleteSubKeyTree(RegistryBranch, throwOnMissingSubKey: false);
    }
}

/// <summary>
/// La red de mentira (Downloader.Http): contesta como llama-server (/health y el chat en
/// streaming con las respuestas que se encolen) y lo que se le enseñe para lo demas.
/// </summary>
internal sealed class FakeHttp : HttpMessageHandler
{
    public sealed record Answer(string? Content = null, string? Reasoning = null, (string Name, string Args)[]? Tools = null, int Status = 200, string? Error = null, int DelayMs = 0);

    public Queue<Answer> Answers { get; } = new();
    public List<JsonObject> ChatRequests { get; } = [];
    public Func<HttpRequestMessage, HttpResponseMessage?>? Other { get; set; }
    public bool Healthy { get; set; } = true;
    public int HealthChecks { get; private set; }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var path = request.RequestUri!.AbsolutePath;
        if (request.RequestUri.Host == "127.0.0.1" && path == "/health")
        {
            HealthChecks++;
            return new HttpResponseMessage(Healthy ? HttpStatusCode.OK : HttpStatusCode.ServiceUnavailable);
        }
        if (request.RequestUri.Host == "127.0.0.1" && path == "/v1/chat/completions")
        {
            var body = JsonNode.Parse(await request.Content!.ReadAsStringAsync(cancellationToken))!.AsObject();
            lock (ChatRequests)
                ChatRequests.Add(body);
            var answer = Answers.Count > 0 ? Answers.Dequeue() : new Answer("Vale.");
            if (answer.DelayMs > 0)
                await Task.Delay(answer.DelayMs, cancellationToken);
            if (answer.Status != 200)
                return new HttpResponseMessage((HttpStatusCode)answer.Status) { Content = new StringContent(answer.Error ?? "{\"error\":{\"message\":\"roto\"}}") };
            var sse = new StringBuilder();
            void Data(JsonNode node) => sse.Append("data: ").Append(node.ToJsonString()).Append("\n\n");
            if (answer.Reasoning is { } r)
                Data(new JsonObject { ["choices"] = new JsonArray(new JsonObject { ["delta"] = new JsonObject { ["reasoning_content"] = r } }) });
            if (answer.Content is { } c)
                foreach (var piece in Pieces(c))
                    Data(new JsonObject { ["choices"] = new JsonArray(new JsonObject { ["delta"] = new JsonObject { ["content"] = piece } }) });
            if (answer.Tools is { } tools)
                for (var i = 0; i < tools.Length; i++)
                    Data(new JsonObject
                    {
                        ["choices"] = new JsonArray(new JsonObject
                        {
                            ["delta"] = new JsonObject
                            {
                                ["tool_calls"] = new JsonArray(new JsonObject
                                {
                                    ["index"] = i,
                                    ["id"] = $"call_{i}",
                                    ["function"] = new JsonObject { ["name"] = tools[i].Name, ["arguments"] = tools[i].Args },
                                }),
                            },
                        }),
                    });
            Data(new JsonObject { ["choices"] = new JsonArray(new JsonObject { ["delta"] = new JsonObject(), ["finish_reason"] = answer.Tools is null ? "stop" : "tool_calls" }) });
            sse.Append("data: [DONE]\n\n");
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(sse.ToString(), Encoding.UTF8, "text/event-stream") };
        }
        return Other?.Invoke(request) ?? new HttpResponseMessage(HttpStatusCode.NotFound) { Content = new StringContent("no") };
    }

    private static IEnumerable<string> Pieces(string text)
    {
        for (var i = 0; i < text.Length; i += 7)
            yield return text.Substring(i, Math.Min(7, text.Length - i));
    }
}

/// <summary>
/// Base de las pruebas de ventanas: plataforma falsa, red falsa, llama-server de mentira en la
/// carpeta del motor del arenero y un modelo de mentira activo. Lo deja todo como estaba.
/// </summary>
public abstract class UiTest : IDisposable
{
    private readonly IDesktop _desktop = Desktop.Current;
    private readonly HttpClient _http = Downloader.Http;
    private readonly string _language = Loc.Language;

    internal FakeDesktop Fake { get; } = new();
    internal FakeHttp Net { get; } = new();
    internal string ModelFile { get; }
    internal static string EngineFolder => Path.Combine(Paths.Engine, $"{EnginePin.Build}-{EnginePin.ForThisMachine(allowCuda: true).Accelerator}");

    protected UiTest()
    {
        Desktop.Current = Fake;
        Downloader.Http = new HttpClient(Net);
        var settings = Sandbox.ResetSettings();
        settings.Thinking = false;
        settings.Instructions = string.Empty;
        settings.TrayOnMinimize = true;
        settings.Language = null;
        Directory.CreateDirectory(Paths.DefaultModels);
        foreach (var old in Directory.GetFiles(Paths.DefaultModels))
            try { File.Delete(old); } catch (IOException) { }
        settings.Installed.Clear();
        ModelFile = Path.Combine(Paths.DefaultModels, "Prueba-Q4_K_M.gguf");
        File.WriteAllBytes(ModelFile, [1, 2, 3]);
        settings.ModelPath = ModelFile;
        settings.ModelName = "Prueba";
        // El llama-server de mentira, donde EngineHost lo busca (asi no descarga nada).
        Directory.CreateDirectory(EngineFolder);
        // El exe lanzador busca su FakeLlama.dll al lado: se copian todos con su nombre y el exe tambien como llama-server.exe.
        foreach (var file in Directory.GetFiles(AppContext.BaseDirectory, "FakeLlama.*"))
            CopyIfFree(file, Path.Combine(EngineFolder, Path.GetFileName(file)));
        CopyIfFree(Path.Combine(AppContext.BaseDirectory, "FakeLlama.exe"), Path.Combine(EngineFolder, "llama-server.exe"));
        File.WriteAllText(Path.Combine(EngineFolder, ".completa"), "pruebas");
        foreach (var t in Directory.Exists(Paths.Threads) ? Directory.GetFiles(Paths.Threads, "*", SearchOption.AllDirectories) : [])
            try { File.Delete(t); } catch (IOException) { }
        Ui.Run(() => Loc.Use("es"));
    }

    /// <summary>Un llama-server de mentira de otra prueba puede seguir vivo un instante: entonces ya vale el que hay.</summary>
    private static void CopyIfFree(string from, string to)
    {
        try { File.Copy(from, to, overwrite: true); }
        catch (IOException) when (File.Exists(to)) { }
    }

    public virtual void Dispose()
    {
        Ui.Run(() =>
        {
            App.Engine.Stop();
            Loc.Use(_language);
        });
        Desktop.Current = _desktop;
        Downloader.Http = _http;
        Fake.Dispose();
        Sandbox.ResetSettings();
    }
}
