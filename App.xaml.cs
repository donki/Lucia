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

    /// <summary>Con que se contesta ahora (los parametros de la ejecucion de MLflow).</summary>
    internal static Services.Mlflow.RunConfig RunConfig()
    {
        var s = AppSettings.Current;
        return new Services.Mlflow.RunConfig(s.ModelName ?? "-", System.IO.Path.GetFileName(s.ModelPath ?? "-"), TokenLimits.Context(s), TokenLimits.Answer(s),
            s.Thinking, ChatClient.Temperature, Engine.Accelerator, Version, TokenLimits.Pc.Gpu ?? "-");
    }

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
        Watchdog = new UiWatchdog(window.Dispatcher)
        {
            MainWindow = new System.Windows.Interop.WindowInteropHelper(window).Handle,
            RecoverHiddenDialog = () => ShowHiddenDialogs(window),
        };
        // MLflow (apagado por defecto): en segundo plano, nunca en este hilo.
        Services.Mlflow.MlflowTelemetry.Current.ConfigNow = RunConfig;
        Services.Mlflow.MlflowTelemetry.Current.Configure(AppSettings.Current.Mlflow);
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
            else if (args[i] == "--settings-after" && i + 1 < args.Length && int.TryParse(args[i + 1], out var ms))
            {
                // Como el boton del engranaje (modal), al cabo de ms: para reproducir «envio y abro Ajustes».
                i++;
                WhenLoaded(window, async () => { await Task.Delay(ms); window.OpenSettingsFromButtonForTest(); });
            }
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
            else if (args[i] == "--teach")
            {
                // Enseñar a Lucia; con --teach-run <fichero> carga esos ejemplos y entrena sin tocar nada (prueba de punta a punta).
                var run = Array.IndexOf(args, "--teach-run") is var r and >= 0 && r + 1 < args.Length ? args[r + 1] : null;
                WhenLoaded(window, () =>
                {
                    var teach = new FineTuneWindow { Owner = window };
                    teach.Show();
                    if (run is not null) _ = teach.RunForTestAsync(run);
                });
            }
            else if (args[i] == "--about")
                WhenLoaded(window, () => new AboutWindow { Owner = window }.Show());
        }
#endif
        return window;
    }

    /// <summary>
    /// Los dialogos de la ventana principal que estan abiertos pero no se ven (escondidos, minimizados o
    /// fuera de las pantallas): se enseñan, se traen a una pantalla y se ponen delante.
    /// </summary>
    internal static void ShowHiddenDialogs(Window main)
    {
        foreach (var w in main.OwnedWindows.Cast<Window>().Concat(Current.Windows.Cast<Window>().Where(x => x != main && x.Owner is null)).Distinct().ToList())
        {
            if (!w.IsLoaded) continue;
            if (w.Visibility != Visibility.Visible) w.Visibility = Visibility.Visible;
            // Tambien por Win32: si Windows la escondio sin que WPF se enterase, la propiedad no basta.
            var handle = new System.Windows.Interop.WindowInteropHelper(w).Handle;
            if (handle != IntPtr.Zero) ShowWindow(handle, 5 /* SW_SHOW */);
            if (w.WindowState == WindowState.Minimized) w.WindowState = WindowState.Normal;
            if (w.Left + 50 < SystemParameters.VirtualScreenLeft || w.Left + 50 > SystemParameters.VirtualScreenLeft + SystemParameters.VirtualScreenWidth
                || w.Top < SystemParameters.VirtualScreenTop || w.Top + 50 > SystemParameters.VirtualScreenTop + SystemParameters.VirtualScreenHeight)
            {
                w.Left = main.Left + 40;
                w.Top = main.Top + 40;
            }
            if (!HostedByTests)   // en el banco de pruebas no se le quita el foco a nadie
            {
                w.Topmost = true;
                w.Activate();
                w.Topmost = false;
            }
            DiagLog.Write("ui", $"dialogo traido delante: {w.GetType().Name} «{w.Title}»");
        }
    }

    private static bool _tracking;

    /// <summary>
    /// Cada ventana que se abre (Ajustes, avisos, permisos, «Acerca de») y cuanto estuvo abierta, al
    /// registro de diagnostico: un dialogo modal que no se ve deja la ventana principal desactivada y
    /// parece congelada aunque el hilo responda (el vigilante no lo ve).
    /// </summary>
    internal static void GuardDialogs()
    {
        if (_tracking) return;
        _tracking = true;
        EventManager.RegisterClassHandler(typeof(Window), FrameworkElement.LoadedEvent, new RoutedEventHandler((sender, _) =>
        {
            if (sender is not Window w || w is MainWindow || w.Tag is "tracked") return;
            w.Tag ??= "tracked";
            NoOwnMinimize(w);
            var clock = System.Diagnostics.Stopwatch.StartNew();
            var owner = w.Owner;
            DiagLog.Write("ventana", $"abierta: {w.GetType().Name} «{w.Title}»{(owner is not null ? $", sobre {owner.GetType().Name} (visible {owner.IsVisible}, {owner.WindowState})" : string.Empty)}, en {(int)w.Left},{(int)w.Top} de {(int)w.ActualWidth}x{(int)w.ActualHeight}, visible {w.IsVisible}");
            w.Closed += (_, _) => DiagLog.Write("ventana", $"cerrada: {w.GetType().Name} tras {clock.Elapsed.TotalSeconds:0.0} s");
        }));
    }

    /// <summary>
    /// Un dialogo (Ajustes, avisos, permisos) no se minimiza por su cuenta: no tiene boton en la barra de
    /// tareas, asi que minimizado desaparecia y la ventana principal se quedaba desactivada por el,
    /// «congelada». Se le quita el boton de minimizar y, si aun asi se minimiza (Win+D), se minimiza la
    /// aplicacion entera (a la bandeja si esta asi) y el dialogo vuelve a su tamaño para cuando se vuelva.
    /// </summary>
    private static void NoOwnMinimize(Window w)
    {
        var hwnd = new System.Windows.Interop.WindowInteropHelper(w).Handle;
        if (hwnd != IntPtr.Zero)
        {
            const int GwlStyle = -16, WsMinimizeBox = 0x00020000;
            SetWindowLong(hwnd, GwlStyle, GetWindowLong(hwnd, GwlStyle) & ~WsMinimizeBox);
        }
        w.StateChanged += (_, _) =>
        {
            if (w.WindowState != WindowState.Minimized) return;
            DiagLog.Write("ventana", $"{w.GetType().Name} minimizado: se minimiza la aplicacion entera");
            w.WindowState = WindowState.Normal;
            if (w.Owner is { } owner && owner.WindowState != WindowState.Minimized)
                owner.WindowState = WindowState.Minimized;
        };
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern bool ShowWindow(IntPtr hwnd, int command);
    [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern int GetWindowLong(IntPtr hwnd, int index);
    [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern int SetWindowLong(IntPtr hwnd, int index, int value);

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
        Services.Mlflow.MlflowTelemetry.Current.Dispose();
        Door.Dispose();
        Engine.Dispose();
    }
}
