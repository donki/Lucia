using System.Windows;
using SocAiChat.Editor;
using SocAiChat.Engine;
using SocAiChat.Localization;
using SocAiChat.Services;

namespace SocAiChat;

public partial class App : Application
{
    private static Mutex? _single;

    /// <summary>El motor (llama-server) y la puerta de editores viven lo que vive la aplicacion.</summary>
    public static EngineHost Engine { get; } = new();
    public static EditorDoor Door { get; } = new(Engine);

    private void OnStartup(object sender, StartupEventArgs e)
    {
        // Una sola instancia: dos aplicaciones cargarian el modelo dos veces.
        _single = new Mutex(true, "sOCAIChat.SingleInstance", out var first);
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
#if DEBUG
        // Solo en Debug, para probar y capturar sin teclear: --ask "pregunta" la envia al abrir; --settings abre Ajustes.
        for (var i = 0; i < e.Args.Length; i++)
        {
            if (e.Args[i] == "--ask" && i + 1 < e.Args.Length)
            {
                var question = e.Args[++i];
                window.Loaded += (_, _) => window.Dispatcher.BeginInvoke(() => window.SendText(question), System.Windows.Threading.DispatcherPriority.ApplicationIdle);
            }
            else if (e.Args[i] == "--settings")
                window.Loaded += (_, _) => window.Dispatcher.BeginInvoke(() => window.OpenSettingsForTest(), System.Windows.Threading.DispatcherPriority.ApplicationIdle);
        }
#endif
    }

    private void OnExit(object sender, ExitEventArgs e)
    {
        Door.Dispose();
        Engine.Dispose();
    }
}
