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

    private void OnStartup(object sender, StartupEventArgs e)
    {
        // Una sola instancia: dos aplicaciones cargarian el modelo dos veces.
        _single = new Mutex(true, "sOCLucia.SingleInstance." + Convert.ToHexString(System.Security.Cryptography.SHA1.HashData(System.Text.Encoding.UTF8.GetBytes(Paths.Root.ToLowerInvariant())))[..12], out var first);
        if (!first)
        {
            // La que ya esta abierta (aunque este en la bandeja) se pone delante; esta se va.
            TrayIcon.AskExistingToShow();
            Shutdown();
            return;
        }
        Paths.Ensure();
        ChildJob.KillStale(Paths.Engine);
        AppSettings.Load();
        if (AppSettings.Current.Language is { } language)
            Loc.Use(language);
        ThemeManager.Apply();
        Door.Apply();
        var window = new MainWindow();
        MainWindow = window;
        window.Show();
        // Una descarga que quedo a medias al cerrar se retoma sola (el .part sigue en la carpeta de modelos).
        window.Dispatcher.BeginInvoke(ModelDownloads.ResumePending, System.Windows.Threading.DispatcherPriority.ApplicationIdle);   // tambien con --tray, que no llega a Loaded
        // --tray (arranque con Windows): escondida en el area de notificacion desde el principio.
        if (e.Args.Contains("--tray"))
            window.Dispatcher.BeginInvoke(() => window.Tray?.HideToTray(), System.Windows.Threading.DispatcherPriority.Loaded);
#if DEBUG
        // Solo en Debug, para probar y capturar sin teclear: --ask "pregunta" la envia al abrir; --settings abre Ajustes.
        for (var i = 0; i < e.Args.Length; i++)
        {
            if (e.Args[i] == "--ask" && i + 1 < e.Args.Length)
            {
                var question = e.Args[++i];
                WhenLoaded(window, () => window.SendText(question));
            }
            else if (e.Args[i] == "--work")
                window.SetPendingWorkMode(auto: e.Args.Contains("--auto"));
            else if (e.Args[i] == "--move-models" && i + 1 < e.Args.Length)
            {
                var target = e.Args[++i];
                WhenLoaded(window, async () => { await SocLucia.Engine.ModelLibrary.MoveAsync(target, null, CancellationToken.None); Shutdown(); });
            }
            else if (e.Args[i] == "--settings")
                WhenLoaded(window, () => window.OpenSettingsForTest());
            else if (e.Args[i] == "--tool" && i + 2 < e.Args.Length)
            {
                // Prueba de una herramienta sin modelo: --tool read_file {"path":"x"} (con --auto no pregunta)
                var name = e.Args[++i]; var json = e.Args[++i];
                WhenLoaded(window, () => window.RunToolForTest(name, json));
            }
            else if (e.Args[i] == "--search" && i + 1 < e.Args.Length)
            {
                var query = e.Args[++i];
                WhenLoaded(window, () => window.OpenSettingsForTest().SearchForTest(query));
            }
            else if (e.Args[i] == "--about")
                WhenLoaded(window, () => new AboutWindow { Owner = window }.Show());
        }
#endif
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
        Door.Dispose();
        Engine.Dispose();
    }
}
