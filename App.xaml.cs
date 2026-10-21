using System.Windows;
using SocLucia.Editor;
using SocLucia.Engine;
using SocLucia.Localization;
using SocLucia.Services;

namespace SocLucia;

public partial class App : Application
{
    private static Mutex? _single;

    /// <summary>El motor (llama-server) y la puerta de editores viven lo que vive la aplicacion.</summary>
    public static EngineHost Engine { get; } = new();
    public static EditorDoor Door { get; } = new(Engine);

    /// <summary>El vigilante de congelaciones de la ventana (registro de diagnostico).</summary>
    internal static UiWatchdog? Watchdog { get; private set; }

    /// <summary>La version que se enseña y se apunta en el registro (la del csproj).</summary>
    public static string Version =>
        (System.Reflection.Assembly.GetExecutingAssembly().GetCustomAttributes(typeof(System.Reflection.AssemblyInformationalVersionAttribute), false)
            .OfType<System.Reflection.AssemblyInformationalVersionAttribute>().FirstOrDefault()?.InformationalVersion ?? "?").Split('+')[0];

    /// <summary>
    /// Las pruebas crean la App solo por sus recursos: WPF lanza Startup en cuanto corre el
    /// Dispatcher, y ellas abren las ventanas cuando y como quieren.
    /// </summary>
    internal static bool HostedByTests { get; set; }

    private void OnStartup(object sender, StartupEventArgs e)
    {
        if (HostedByTests)
            return;
        GuardAgainstCrashes();
        // Una sola instancia: dos aplicaciones cargarian el modelo dos veces.
        _single = new Mutex(true, "sOCLucia.SingleInstance." + Convert.ToHexString(System.Security.Cryptography.SHA1.HashData(System.Text.Encoding.UTF8.GetBytes(Paths.Root.ToLowerInvariant())))[..12], out var first);
        if (!first)
        {
            // La que ya esta abierta (aunque este en la bandeja) se pone delante; esta se va.
            TrayIcon.AskExistingToShow();
            Shutdown();
            return;
        }
        Launch(e.Args);
    }

    /// <summary>Carpetas, ajustes, idioma, tema, puerta de editores y la ventana (las pruebas lo llaman directamente).</summary>
    internal MainWindow Launch(string[] args)
    {
        Paths.Ensure();
        ChildJob.KillStale(Paths.Engine);
        AppSettings.Load();
        DiagLog.Write("app", $"arranca sOC Lucia {Version} en {Environment.OSVersion.VersionString}, .NET {Environment.Version}, {Environment.ProcessorCount} nucleos; argumentos: {string.Join(' ', args.Where(a => a.StartsWith("--", StringComparison.Ordinal)))}");
        WindowsStartup.Refresh();
        if (AppSettings.Current.Language is { } language)
            Loc.Use(language);
        ThemeManager.Apply();
        Door.Apply();
        var window = new MainWindow();
        MainWindow = window;
        window.Show();
        Watchdog?.Dispose();
        Watchdog = new UiWatchdog(window.Dispatcher);
        // Una descarga que quedo a medias al cerrar se retoma sola (el .part sigue en la carpeta de modelos).
        window.Dispatcher.BeginInvoke(ModelDownloads.ResumePending, System.Windows.Threading.DispatcherPriority.ApplicationIdle);   // tambien con --tray, que no llega a Loaded
        // --tray (arranque con Windows): escondida en el area de notificacion desde el principio.
        if (args.Contains("--tray"))
            window.Dispatcher.BeginInvoke(() => window.Tray?.HideToTray(), System.Windows.Threading.DispatcherPriority.Loaded);
#if DEBUG
        // Solo en Debug, para probar y capturar sin teclear: --ask "pregunta" la envia al abrir; --settings abre Ajustes.
        for (var i = 0; i < args.Length; i++)
        {
            if (args[i] == "--ask" && i + 1 < args.Length)
            {
                var question = args[++i];
                WhenLoaded(window, () => window.SendText(question));
            }
            else if (args[i] == "--work")
                window.SetPendingWorkMode(auto: args.Contains("--auto"));
            else if (args[i] == "--move-models" && i + 1 < args.Length)
            {
                var target = args[++i];
                WhenLoaded(window, async () => { await SocLucia.Engine.ModelLibrary.MoveAsync(target, null, CancellationToken.None); Shutdown(); });
            }
            else if (args[i] == "--settings")
                WhenLoaded(window, () => window.OpenSettingsForTest());
            else if (args[i] == "--tool" && i + 2 < args.Length)
            {
                // Prueba de una herramienta sin modelo: --tool read_file {"path":"x"} (con --auto no pregunta)
                var name = args[++i]; var json = args[++i];
                WhenLoaded(window, () => window.RunToolForTest(name, json));
            }
            else if (args[i] == "--search" && i + 1 < args.Length)
            {
                var query = args[++i];
                WhenLoaded(window, () => window.OpenSettingsForTest().SearchForTest(query));
            }
            else if (args[i] == "--about")
                WhenLoaded(window, () => new AboutWindow { Owner = window }.Show());
        }
#endif
        return window;
    }

    private static bool _alerting;

    /// <summary>
    /// Gestor global de excepciones (constitucion General 6.12): un error que no se esperaba no cierra
    /// la aplicacion; se apunta con su traza en logs\engine.log y se avisa en el idioma del usuario.
    /// </summary>
    private void GuardAgainstCrashes()
    {
        DispatcherUnhandledException += (_, ex) =>
        {
            ex.Handled = true;
            OnUnexpected(ex.Exception);
        };
        TaskScheduler.UnobservedTaskException += (_, ex) =>
        {
            EngineHost.Log($"error no controlado en tarea: {ex.Exception}");
            DiagLog.Write("error", "no controlado en tarea: " + DiagLog.Describe(ex.Exception.GetBaseException()));
            ex.SetObserved();
        };
        AppDomain.CurrentDomain.UnhandledException += (_, ex) =>
        {
            EngineHost.Log($"error fatal: {ex.ExceptionObject}");
            DiagLog.Write("error", "fatal: " + (ex.ExceptionObject is Exception e ? DiagLog.Describe(e) : "?"));
        };
    }

    /// <summary>Un error que nadie esperaba: al registro con su traza y un aviso; la aplicacion sigue.</summary>
    internal void OnUnexpected(Exception exception)
    {
        EngineHost.Log($"error no controlado: {exception}");
        DiagLog.Write("error", $"no controlado: {DiagLog.Describe(exception)} en {exception.TargetSite?.DeclaringType?.Name}.{exception.TargetSite?.Name}");
        // Un solo aviso a la vez: un error que se repite en cada pintado no debe abrir cien ventanas.
        if (_alerting || MainWindow is not { IsLoaded: true } owner) return;
        _alerting = true;
        try { PromptWindow.Alert(owner, Loc.Get("Error"), Loc.Get("UnexpectedError")); }
        catch (Exception) { }
        finally { _alerting = false; }
    }

#if DEBUG
    /// <summary>Ejecuta algo cuando la ventana esta cargada y la interfaz libre (Show() puede haber lanzado Loaded ya).</summary>
    private static void WhenLoaded(MainWindow window, Action action)
    {
        void Run() => window.Dispatcher.BeginInvoke(action, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
        if (window.IsLoaded) Run();
        else window.Loaded += (_, _) => Run();
    }
#endif

    private void OnExit(object sender, ExitEventArgs e)
    {
        DiagLog.Write("app", "se cierra");
        Watchdog?.Dispose();
        Door.Dispose();
        Engine.Dispose();
    }
}
