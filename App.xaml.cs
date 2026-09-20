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
        _single = new Mutex(true, "sOCLucia.SingleInstance", out var first);
        if (!first)
        {
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
                window.Loaded += (_, _) => window.Dispatcher.BeginInvoke(() => window.SendText(question), System.Windows.Threading.DispatcherPriority.ApplicationIdle);
            }
            else if (e.Args[i] == "--work")
                window.SetPendingWorkMode(auto: e.Args.Contains("--auto"));
            else if (e.Args[i] == "--move-models" && i + 1 < e.Args.Length)
            {
                var target = e.Args[++i];
                window.Loaded += async (_, _) => { await SocLucia.Engine.ModelLibrary.MoveAsync(target, null, CancellationToken.None); Shutdown(); };
            }
            else if (e.Args[i] == "--settings")
                window.Loaded += (_, _) => window.Dispatcher.BeginInvoke(() => window.OpenSettingsForTest(), System.Windows.Threading.DispatcherPriority.ApplicationIdle);
            else if (e.Args[i] == "--tool" && i + 2 < e.Args.Length)
            {
                // Prueba de una herramienta sin modelo: --tool read_file {"path":"x"} (con --auto no pregunta)
                var name = e.Args[++i]; var json = e.Args[++i];
                window.Loaded += (_, _) => window.Dispatcher.BeginInvoke(() => window.RunToolForTest(name, json), System.Windows.Threading.DispatcherPriority.ApplicationIdle);
            }
            else if (e.Args[i] == "--about")
                window.Loaded += (_, _) => window.Dispatcher.BeginInvoke(() => new AboutWindow { Owner = window }.Show(), System.Windows.Threading.DispatcherPriority.ApplicationIdle);
        }
#endif
    }

    private void OnExit(object sender, ExitEventArgs e)
    {
        Door.Dispose();
        Engine.Dispose();
    }
}
