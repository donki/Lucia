using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using SocLucia.Agent;
using SocLucia.Chat;
using SocLucia.Engine;
using SocLucia.Localization;
using SocLucia.Services;

namespace SocLucia.Tests;

/// <summary>
/// La ventana del chat de verdad, con un llama-server de mentira (FakeLlama, que solo se queda vivo)
/// y la red de mentira (FakeHttp, que contesta como llama-server en streaming): preguntar, pensar,
/// herramientas con permisos, adjuntos, conversaciones, tareas programadas y la bandeja.
/// </summary>
public sealed class MainWindowTests : UiTest
{
    private static string Status(MainWindow w) => Ui.Find<TextBlock>(w, "EngineStatus").Text;

    private static StackPanel Messages(MainWindow w) => Ui.Find<StackPanel>(w, "Messages");

    private static bool Answering(MainWindow w) => Ui.Field<object?>(w, "_answering") is not null;

    private static MainWindow Open()
    {
        var w = new MainWindow();
        w.Show();
        Ui.WaitFor(() => App.Engine.State == EngineState.Ready);
        return w;
    }

    private static void Ask(MainWindow w, string text)
    {
        Ui.Find<TextBox>(w, "Composer").Text = text;
        Ui.Click(Ui.Find<ButtonBase>(w, "SendButton"));
        Ui.WaitFor(() => !Answering(w));
    }

    private static string AllText(DependencyObject root) =>
        string.Join("\n", Ui.All<TextBlock>(root).Select(t => t.Text).Concat(Ui.All<TextBox>(root).Select(t => t.Text)))
        + string.Join("\n", Ui.All<FlowDocumentScrollViewer>(root).Select(v => new System.Windows.Documents.TextRange(v.Document.ContentStart, v.Document.ContentEnd).Text));

    private static string Bubbles(MainWindow w) => string.Join("\n", Messages(w).Children.OfType<Border>().Select(b => b.Tag as string ?? AllText(b)));

    [Fact]
    public void AlAbrir_ArrancaElMotor_YPreguntarContesta()
    {
        Net.Answers.Enqueue(new FakeHttp.Answer("¡Hola! ¿En qué te ayudo?"));
        Ui.Run(() =>
        {
            var w = Open();
            Ui.WaitFor(() => Status(w).StartsWith(Loc.Get("EngineReady"), StringComparison.Ordinal));
            Assert.Contains("Prueba", Status(w));
            Assert.Equal(Visibility.Visible, Ui.Find<FrameworkElement>(w, "EmptyState").Visibility);
            Ask(w, "hola");
            Assert.Equal(Visibility.Collapsed, Ui.Find<FrameworkElement>(w, "EmptyState").Visibility);
            Assert.Contains("¡Hola! ¿En qué te ayudo?", Bubbles(w));
            Assert.Single(Ui.Find<ListBox>(w, "ThreadList").Items);
            var request = Net.ChatRequests.Single();
            Assert.Equal("hola", request["messages"]!.AsArray().Last()!["content"]!.GetValue<string>());
            Assert.Equal(false, request["chat_template_kwargs"]!["enable_thinking"]!.GetValue<bool>());
            Assert.Equal(TokenLimits.AnswerDefault, request["max_tokens"]!.GetValue<int>());   // el tope de Ajustes › Respuestas y contexto
        });
        Assert.Single(ThreadStore.LoadAll());
    }

    private static FrameworkElement? WaitIndicator(MainWindow w) =>
        Ui.All<FrameworkElement>(Messages(w)).FirstOrDefault(e => e.Name == "WaitIndicator");

    private static string? WaitText(MainWindow w) => WaitIndicator(w) is { } i ? Ui.All<TextBlock>(i).Last().Text : null;

    [Fact]
    public void AlEnviar_LaPreguntaSaleYa_ConIndicadorDeEspera_YLaVentanaNoSeBloquea()
    {
        Net.Answers.Enqueue(new FakeHttp.Answer("Ya está.", DelayMs: 1500));
        Ui.Run(() =>
        {
            var w = Open();
            var composer = Ui.Find<TextBox>(w, "Composer");
            composer.Text = "una pregunta que tarda";
            Ui.Click(Ui.Find<ButtonBase>(w, "SendButton"));
            // Al momento, sin dar ni una vuelta al Dispatcher: la pregunta en el chat, la casilla
            // vacia, parar a mano y la burbuja de la respuesta con su indicador.
            Assert.Equal(string.Empty, composer.Text);
            Assert.Contains("una pregunta que tarda", Bubbles(w));
            Assert.Equal(Visibility.Visible, Ui.Find<FrameworkElement>(w, "StopButton").Visibility);
            Assert.NotNull(WaitIndicator(w));
            // Mientras la IA piensa la ventana sigue viva: cada vuelta del Dispatcher es corta y el
            // indicador va diciendo por donde va.
            var slowest = 0L;
            var watch = System.Diagnostics.Stopwatch.StartNew();
            while (WaitText(w) != Loc.Get("AnswerWaiting") && watch.ElapsedMilliseconds < 15_000)
            {
                var turn = System.Diagnostics.Stopwatch.StartNew();
                Ui.DoEvents();
                slowest = Math.Max(slowest, turn.ElapsedMilliseconds);
                Thread.Sleep(5);
            }
            Assert.Equal(Loc.Get("AnswerWaiting"), WaitText(w));
            Assert.True(Answering(w));
            Assert.True(slowest < 1000, $"una vuelta del Dispatcher tardo {slowest} ms");
            Ui.WaitFor(() => !Answering(w));
            Assert.Contains("Ya está.", Bubbles(w));
            Assert.Null(WaitIndicator(w));
            Assert.False(Ui.Field<System.Windows.Threading.DispatcherTimer>(w, "_waitTimer").IsEnabled);
        });
    }

    /// <summary>
    /// Lo que conto Josep (2026.10.21.1): enviar y, mientras contesta, abrir Ajustes congelaba la ventana.
    /// Ajustes se abre durante el streaming y cada vuelta del Dispatcher tiene que ser corta. Con
    /// LUCIA_REAL_GGUF=ruta se usa un GGUF de verdad como modelo (solo se lee su cabecera).
    /// </summary>
    [Fact]
    public void AbrirAjustesMientrasContesta_NoCongelaLaVentana()
    {
        if (Environment.GetEnvironmentVariable("LUCIA_REAL_GGUF") is { Length: > 0 } real && File.Exists(real))
            AppSettings.Current.ModelPath = real;
        Net.Answers.Enqueue(new FakeHttp.Answer(string.Concat(Enumerable.Repeat("Una respuesta larga. ", 40)), DelayMs: 2500));
        Ui.Run(() =>
        {
            var w = Open();
            Ui.Find<TextBox>(w, "Composer").Text = "explícame algo largo";
            Ui.Click(Ui.Find<ButtonBase>(w, "SendButton"));
            Ui.WaitFor(() => Net.ChatRequests.Count == 1);
            var slowest = 0L;
            var opened = System.Diagnostics.Stopwatch.StartNew();
            Ui.Expect<SettingsWindow>(s =>
            {
                Assert.True(opened.ElapsedMilliseconds < 1500, $"Ajustes tardo {opened.ElapsedMilliseconds} ms en abrir");
                var clock = System.Diagnostics.Stopwatch.StartNew();
                while (Answering(w) && clock.ElapsedMilliseconds < 15_000)
                {
                    var turn = System.Diagnostics.Stopwatch.StartNew();
                    Ui.DoEvents();
                    slowest = Math.Max(slowest, turn.ElapsedMilliseconds);
                    Thread.Sleep(5);
                }
                s.Close();
            });
            Ui.Click(Ui.Find<ButtonBase>(w, "SettingsButton"));
            Assert.True(slowest < 1000, $"una vuelta del Dispatcher tardo {slowest} ms con Ajustes abierto");
            Ui.WaitFor(() => !Answering(w));
            Assert.Contains("Una respuesta larga.", Bubbles(w));
        });
        var log = File.ReadAllText(DiagLog.Current);
        Assert.Contains("pregunta enviada", log);
        Assert.Contains("respuesta acabada", log);
    }

    /// <summary>
    /// Ajustes abierto (modal) y la ventana se minimiza a la bandeja (Win+D, la barra de tareas): al volver
    /// con el icono, Ajustes tiene que verse. Si no, la principal se ve pero esta desactivada por un dialogo
    /// que no se ve: parece congelada aunque el hilo responda.
    /// </summary>
    [Fact]
    public void AjustesAbierto_MinimizarALaBandejaYVolver_NoDejaLaVentanaBloqueada()
    {
        Ui.Run(() =>
        {
            var w = Open();
            var hwnd = new WindowInteropHelper(w).Handle;
            bool? settingsVisible = null, mainEnabled = null, mainVisible = null;
            Ui.Expect<SettingsWindow>(s =>
            {
                // Minimizar el propio dialogo (Win+D; ya no tiene boton): se minimiza la aplicacion entera, a la bandeja.
                s.WindowState = WindowState.Minimized;
                Ui.WaitFor(() => !w.IsVisible && s.WindowState == WindowState.Normal);
                SendMessage(hwnd, 0x8001, 0, 0x0202);
                Ui.DoEvents();
                Assert.True(IsWindowVisible(new WindowInteropHelper(s).Handle), "minimizar Ajustes y volver: Ajustes no se ve");
                Assert.Equal(0, GetWindowLong(new WindowInteropHelper(s).Handle, -16) & 0x00020000);   // sin boton de minimizar
                SendMessage(hwnd, 0x0112, 0xF020, 0);   // SC_MINIMIZE: a la bandeja
                Ui.DoEvents();
                SendMessage(hwnd, 0x8001, 0, 0x0202);   // clic en el icono: volver
                Ui.DoEvents();
                var settings = new WindowInteropHelper(s).Handle;
                Assert.True(IsWindowVisible(settings), "SC_MINIMIZE y volver: Ajustes no se ve");
                // Minimizar desde la barra de tareas o con Win+D (cambio de estado, sin SC_MINIMIZE).
                w.WindowState = WindowState.Minimized;
                Ui.WaitFor(() => !w.IsVisible);
                SendMessage(hwnd, 0x8001, 0, 0x0202);
                Ui.DoEvents();
                (settingsVisible, mainEnabled, mainVisible) = (IsWindowVisible(settings), IsWindowEnabled(hwnd), IsWindowVisible(hwnd));
                s.Close();
            });
            Ui.Click(Ui.Find<ButtonBase>(w, "SettingsButton"));
            Assert.True(mainVisible);
            Assert.False(mainEnabled);          // con Ajustes abierto la principal esta desactivada (modal, Win32)…
            Assert.True(settingsVisible);       // …y por eso Ajustes tiene que verse
            Assert.True(IsWindowEnabled(hwnd));
            w.Tray?.Dispose();
        });
    }

    /// <summary>
    /// El «congelado» que el hilo no ve: Ajustes abierto pero escondido, la principal visible y desactivada.
    /// El vigilante lo detecta por Win32, lo apunta y vuelve a enseñar el dialogo.
    /// </summary>
    [Fact]
    public void DialogoQueNoSeVe_ElVigilanteLoApuntaYLoVuelveAEnseñar()
    {
        if (File.Exists(DiagLog.Current)) File.Delete(DiagLog.Current);
        Ui.Run(() =>
        {
            var w = Open();
            var hwnd = new WindowInteropHelper(w).Handle;
            Ui.Expect<SettingsWindow>(s =>
            {
                using var watchdog = new UiWatchdog(w.Dispatcher, TimeSpan.FromSeconds(5), TimeSpan.FromMilliseconds(50))
                {
                    MainWindow = hwnd,
                    RecoverHiddenDialog = () => App.ShowHiddenDialogs(w),
                    HiddenDialogAfter = TimeSpan.FromMilliseconds(300),
                };
                Ui.WaitFor(() => IsWindowVisible(new WindowInteropHelper(s).Handle));
                Thread.Sleep(500);
                Assert.Equal(0, watchdog.HiddenDialogs);   // con Ajustes a la vista no pasa nada
                // Minimizado (su boton o Win+D) y sin boton en la barra de tareas: desaparece y la principal sigue desactivada.
                // Windows esconde el dialogo sin que WPF se entere: la principal sigue desactivada y no se ve nada que pulsar.
                var dialog = new WindowInteropHelper(s).Handle;
                ShowWindow(dialog, 0 /* SW_HIDE */);
                Ui.WaitFor(() => watchdog.HiddenDialogs == 1 && IsWindowVisible(dialog), 10_000);   // lo ve, lo apunta y lo vuelve a enseñar
                Assert.False(IsWindowEnabled(hwnd));   // sigue siendo modal: ahora con su dialogo a la vista
                Ui.WaitFor(() => File.ReadAllText(DiagLog.Current).Contains("vuelve a estar disponible"));
                Assert.False(IsWindowEnabled(hwnd));
                s.Close();
            });
            Ui.Click(Ui.Find<ButtonBase>(w, "SettingsButton"));
            Assert.True(IsWindowEnabled(hwnd));
        });
        var log = File.ReadAllText(DiagLog.Current);
        Assert.Contains("VENTANA BLOQUEADA", log);
        Assert.Contains("vuelve a estar disponible", log);
        Assert.Contains("dialogo traido delante: SettingsWindow", log);
        Assert.Contains("abierta: SettingsWindow «Ajustes», sobre MainWindow", log);
        Assert.Contains("cerrada: SettingsWindow", log);
    }

    [Fact]
    public async Task ConMlflow_CadaPreguntaEsUnaTrazaDeGenAI_SinTextoPorDefecto()
    {
        var previous = SocLucia.Services.Mlflow.MlflowTelemetry.Current;
        var fake = new FakeMlflow();
        var telemetry = new SocLucia.Services.Mlflow.MlflowTelemetry(fake, TimeSpan.FromMilliseconds(50)) { ConfigNow = App.RunConfig };
        SocLucia.Services.Mlflow.MlflowTelemetry.Current = telemetry;
        try
        {
            telemetry.Configure(new MlflowSettings { Enabled = true, TrackingUri = "http://mlflow.test:5000", InstallId = "lucia-prueba" });
            Net.Answers.Enqueue(new FakeHttp.Answer("Una respuesta privada"));
            string? conversation = null;
            Ui.Run(() =>
            {
                var w = Open();
                Ask(w, "una pregunta privada");
                conversation = ThreadStore.LoadAll().Single().Id;
            });
            Assert.True(await telemetry.DrainAsync(TimeSpan.FromSeconds(10)));
            var spans = fake.Spans;
            var root = spans.Single(sp => sp.ParentId is null);
            Assert.Equal(("chat", "CHAT_MODEL"), (root.Name, root.Type));
            Assert.Equal(conversation, root.Attributes["session.id"]);
            Assert.Equal("lucia-prueba", root.Attributes["user.id"]);
            Assert.Equal("ok", root.Attributes["lucia.outcome"]);
            Assert.Contains(spans, sp => sp.Type == "TASK" && sp.ParentId == root.SpanId);       // el motor
            Assert.Contains(spans, sp => sp.Type == "RETRIEVER" && sp.ParentId == root.SpanId);  // los documentos
            Assert.Contains(spans, sp => sp.Type == "LLM" && sp.ParentId == root.SpanId);        // la llamada al modelo
            Assert.DoesNotContain("privada", System.Text.Encoding.UTF8.GetString(fake.Of("/v1/traces").Single().Raw!));
        }
        finally
        {
            telemetry.Dispose();
            SocLucia.Services.Mlflow.MlflowTelemetry.Current = previous;
        }
    }

    /// <summary>
    /// El modelo atascado pide una y otra vez la misma accion, que siempre falla. Antes se ejecutaba sin
    /// fin; ahora se ejecuta una vez, la repeticion no se ejecuta (ni pide permiso), y a la siguiente se para
    /// y se dice. La ventana sigue respondiendo todo el rato.
    /// </summary>
    [Fact]
    public void ModoAgente_UnaAccionQueFallaNoSeRepiteSinFin_YLaVentanaResponde()
    {
        var missing = new JsonObject { ["path"] = Path.Combine(Sandbox.Work, "no-existe.txt"), ["reason"] = "leerlo" }.ToJsonString();
        for (var i = 0; i < 20; i++)
            Net.Answers.Enqueue(new FakeHttp.Answer(Tools: [(ToolBox.ReadFile, missing)]));
        Ui.Run(() =>
        {
            var w = Open();
            Ui.Click(Ui.Find<ButtonBase>(w, "WorkModeButton"));
            var asked = 0;
            Ui.Expect<PermissionWindow>(p => { asked++; Ui.Click(Ui.All<Button>(p).Single(b => Equals(b.ToolTip, Loc.Get("PermAllow")))); });   // una vez
            Ui.Find<TextBox>(w, "Composer").Text = "lee ese fichero";
            Ui.Click(Ui.Find<ButtonBase>(w, "SendButton"));
            var slowest = 0L;
            var clock = System.Diagnostics.Stopwatch.StartNew();
            while (Answering(w) && clock.ElapsedMilliseconds < 20_000)
            {
                var turn = System.Diagnostics.Stopwatch.StartNew();
                Ui.DoEvents();
                slowest = Math.Max(slowest, turn.ElapsedMilliseconds);
                Thread.Sleep(5);
            }
            Assert.False(Answering(w));
            Assert.Equal(1, asked);                        // el permiso, una sola vez
            Assert.Equal(3, Net.ChatRequests.Count);       // ejecuta, avisa de la repeticion, y para
            Assert.True(slowest < 1000, $"una vuelta del Dispatcher tardo {slowest} ms");
            var text = Bubbles(w);
            Assert.Contains(Loc.Get("AgentRepeatBlocked"), text);
            Assert.Contains(Loc.Format("AgentStoppedRepeats", "", "")[..20], text);
        });
        var thread = ThreadStore.LoadAll().Single();
        var tools = thread.Messages.Where(m => m.Role == "tool").ToList();
        Assert.Equal(3, tools.Count);                       // cada peticion de herramienta tiene su respuesta
        Assert.Single(tools, t => t.Content.StartsWith("[error]", StringComparison.Ordinal));   // ejecutada una sola vez
        Assert.StartsWith(Loc.Format("AgentStoppedRepeats", "", "")[..20], thread.Messages.Last().Content);
        Assert.Contains("respuesta parada: Repeats", File.ReadAllText(DiagLog.Current));
    }

    [Fact]
    public void ModoAgente_ElTopeDeAccionesPara_YLoDice()
    {
        AppSettings.Current.AgentMaxActions = 3;
        try
        {
            for (var i = 0; i < 10; i++)
                Net.Answers.Enqueue(new FakeHttp.Answer(Tools: [(ToolBox.SystemInfo, "{}")]));
            Ui.Run(() =>
            {
                var w = Open();
                Ui.Click(Ui.Find<ButtonBase>(w, "WorkModeButton"));
                Ask(w, "dime cosas del PC sin parar");
                Assert.Contains(Loc.Format("AgentStoppedCap", 3), Bubbles(w));
            });
            Assert.Equal(4, Net.ChatRequests.Count);
            Assert.Equal(3, ThreadStore.LoadAll().Single().Messages.Count(m => m.Role == "tool" && m.Content.Contains("Windows")));
        }
        finally
        {
            AppSettings.Current.AgentMaxActions = ToolLoopGuard.DefaultMaxCalls;
        }
    }

    [Fact]
    public void MientrasCargaLaIA_ElIndicadorLoDice_YLuegoContesta()
    {
        Net.Healthy = false;   // llama-server arrancado pero cargando el modelo
        Net.Answers.Enqueue(new FakeHttp.Answer("Lista."));
        Ui.Run(() =>
        {
            var w = new MainWindow();
            w.Show();
            Ui.WaitFor(() => App.Engine.State == EngineState.Starting);
            Ui.Find<TextBox>(w, "Composer").Text = "¿estás?";
            Ui.Click(Ui.Find<ButtonBase>(w, "SendButton"));
            Assert.Contains("¿estás?", Bubbles(w));
            Ui.WaitFor(() => WaitText(w) == Loc.Get("AnswerLoadingModel"));
            Assert.Empty(Net.ChatRequests);
            Net.Healthy = true;
            Ui.WaitFor(() => !Answering(w));
            Assert.Contains("Lista.", Bubbles(w));
            Assert.Null(WaitIndicator(w));
        });
    }

    [Fact]
    public void Pensar_SoloRazonamiento_SeRepiteSinPensar_YElRazonamientoSeGuarda()
    {
        Net.Answers.Enqueue(new FakeHttp.Answer(Reasoning: "Estoy pensando mucho"));
        Net.Answers.Enqueue(new FakeHttp.Answer("La respuesta es 42."));
        Ui.Run(() =>
        {
            var w = Open();
            Ui.Click(Ui.Find<ToggleButton>(w, "ThinkButton"));
            Assert.True(AppSettings.Current.Thinking);
            Ask(w, "¿cuál es la respuesta?");
            Assert.Contains("42", Bubbles(w));
            Assert.Equal(2, Net.ChatRequests.Count);
            Assert.True(Net.ChatRequests[0]["chat_template_kwargs"]!["enable_thinking"]!.GetValue<bool>());
            Assert.False(Net.ChatRequests[1]["chat_template_kwargs"]!["enable_thinking"]!.GetValue<bool>());
            Assert.NotEmpty(Ui.All<Expander>(Messages(w)));   // el razonamiento, plegado
        });
        var answer = ThreadStore.LoadAll().Single().Messages.Last();
        Assert.Equal("Estoy pensando mucho", answer.Reasoning);
    }

    [Fact]
    public void SiElServidorFalla_LoDiceEnLaConversacion()
    {
        Net.Answers.Enqueue(new FakeHttp.Answer(Status: 500, Error: "{\"error\":{\"message\":\"sin memoria\"}}"));
        Ui.Run(() =>
        {
            var w = Open();
            Ask(w, "hola");
            Assert.Contains(Loc.Format("AnswerFailed", "500: sin memoria"), Bubbles(w));
        });
    }

    [Fact]
    public void SiLaPlantillaRechazaLaConversacion_MensajeLimpioYAlRegistro_SinElStackDeJinja()
    {
        const string jinja = "While executing CallExpression at line 79, column 24: Jinja Exception: No user query found in messages.";
        var error = new FakeHttp.Answer(Status: 500, Error: "{\"error\":{\"message\":\"" + jinja + "\",\"code\":500}}");
        Net.Answers.Enqueue(error);   // la primera llamada…
        Net.Answers.Enqueue(error);   // …y el reintento sin herramientas: ambos fallan, para que salga el mensaje limpio
        if (File.Exists(DiagLog.Current)) File.Delete(DiagLog.Current);
        Ui.Run(() =>
        {
            var w = Open();
            Ask(w, "hola");
            var bubbles = Bubbles(w);
            Assert.Contains(Loc.Get("AnswerTemplateRejected"), bubbles);     // al usuario, en claro
            Assert.DoesNotContain("Jinja", bubbles);                         // nunca el stack
            Assert.DoesNotContain("No user query", bubbles);
            Assert.DoesNotContain("line 79", bubbles);
        });
        var log = File.ReadAllText(DiagLog.Current);
        Assert.Contains("No user query found in messages", log);            // el detalle, solo en el registro
    }

    [Fact]
    public void SiLaPlantillaRechaza_ConHerramientas_SeReintentaSinEllas()
    {
        Net.Answers.Enqueue(new FakeHttp.Answer(Status: 500, Error: "{\"error\":{\"message\":\"Jinja Exception: No user query found in messages.\"}}"));
        Net.Answers.Enqueue(new FakeHttp.Answer("Ya está, sin herramientas."));
        Ui.Run(() =>
        {
            var w = Open();
            Ui.Click(Ui.Find<ButtonBase>(w, "WorkModeButton"));
            Ask(w, "haz algo");
            Assert.Contains("Ya está, sin herramientas.", Bubbles(w));
            Assert.Equal(2, Net.ChatRequests.Count);
            Assert.True(Net.ChatRequests[0].ContainsKey("tools"));           // la primera, con herramientas
            Assert.False(Net.ChatRequests[1].ContainsKey("tools"));          // el reintento, sin ellas
        });
    }

    [Fact]
    public void ModoAgente_TrasMuchasVueltas_LaPreguntaSiempreVaAlModelo()
    {
        // 15 vueltas de herramienta (asistente + resultado) harian caer la pregunta de una ventana ciega de 30.
        for (var i = 0; i < 14; i++)
            Net.Answers.Enqueue(new FakeHttp.Answer(Tools: [(ToolBox.SystemInfo, "{}")]));
        Net.Answers.Enqueue(new FakeHttp.Answer("Hecho."));
        AppSettings.Current.AgentMaxActions = 20;
        try
        {
            Ui.Run(() =>
            {
                var w = Open();
                Ui.Click(Ui.Find<ButtonBase>(w, "WorkModeButton"));
                Ask(w, "la pregunta original");
                Assert.Contains("Hecho.", Bubbles(w));
                // En cada peticion al modelo hay siempre al menos un mensaje de usuario.
                Assert.All(Net.ChatRequests, r => Assert.Contains(r["messages"]!.AsArray(), m => m!["role"]!.GetValue<string>() == "user"));
                Assert.Contains("la pregunta original", Net.ChatRequests[^1]["messages"]!.AsArray().Select(m => m!["content"]?.ToString()));
            });
        }
        finally
        {
            AppSettings.Current.AgentMaxActions = ToolLoopGuard.DefaultMaxCalls;
        }
    }

    [Fact]
    public void Parar_CortaLaRespuesta()
    {
        Net.Answers.Enqueue(new FakeHttp.Answer("no llega", DelayMs: 10_000));
        Ui.Run(() =>
        {
            var w = Open();
            Ui.Find<TextBox>(w, "Composer").Text = "algo largo";
            Ui.Click(Ui.Find<ButtonBase>(w, "SendButton"));
            Ui.WaitFor(() => Net.ChatRequests.Count == 1);
            Assert.Equal(Visibility.Visible, Ui.Find<FrameworkElement>(w, "StopButton").Visibility);
            Ui.Click(Ui.Find<ButtonBase>(w, "StopButton"));
            Ui.WaitFor(() => !Answering(w));
            Assert.Equal(Visibility.Collapsed, Ui.Find<FrameworkElement>(w, "StopButton").Visibility);
            Assert.DoesNotContain("no llega", Bubbles(w));
        });
    }

    [Fact]
    public void DosPreguntasSeguidas_LaSegundaSeContestaAlAcabar()
    {
        Net.Answers.Enqueue(new FakeHttp.Answer("primera", DelayMs: 300));
        Net.Answers.Enqueue(new FakeHttp.Answer("segunda"));
        Ui.Run(() =>
        {
            var w = Open();
            var composer = Ui.Find<TextBox>(w, "Composer");
            composer.Text = "uno";
            Ui.Click(Ui.Find<ButtonBase>(w, "SendButton"));
            Ui.WaitFor(() => Net.ChatRequests.Count == 1);
            composer.Text = "dos";
            Ui.Click(Ui.Find<ButtonBase>(w, "SendButton"));
            Ui.WaitFor(() => Net.ChatRequests.Count == 2 && !Answering(w));
            Assert.Contains("segunda", Bubbles(w));
        });
    }

    [Fact]
    public void ModoTrabajo_LaHerramientaPidePermiso_YSuSalidaVuelveAlModelo()
    {
        var file = Path.Combine(Sandbox.Work, "nota.txt");
        File.WriteAllText(file, "contenido de la nota");
        Net.Answers.Enqueue(new FakeHttp.Answer("Voy a leerla.", Tools: [(ToolBox.ReadFile, new JsonObject { ["path"] = file, ["reason"] = "para resumirla" }.ToJsonString())]));
        Net.Answers.Enqueue(new FakeHttp.Answer("La nota dice: contenido de la nota."));
        Ui.Run(() =>
        {
            var w = Open();
            Ui.Click(Ui.Find<ButtonBase>(w, "WorkModeButton"));
            Ui.Expect<PermissionWindow>(p =>
            {
                Assert.Contains("para resumirla", AllText(p));
                Ui.All<CheckBox>(p).Last().IsChecked = true;   // siempre
                Ui.Click(Ui.All<Button>(p).Single(b => Equals(b.ToolTip, Loc.Get("PermAllow"))));
            });
            Ask(w, "lee la nota");
            Assert.Contains("La nota dice", Bubbles(w));
            var tool = Net.ChatRequests[1]["messages"]!.AsArray().Single(m => m!["role"]!.GetValue<string>() == "tool");
            Assert.Contains("contenido de la nota", tool!["content"]!.GetValue<string>());
            Assert.Equal(Permission.Allow, AppSettings.Current.PermissionFor(Resource.FilesRead));
        });
        Assert.Contains(ThreadStore.LoadAll().Single().Messages, m => m.Role == "tool");
    }

    [Fact]
    public void ModoTrabajo_Denegada_Desconocida_Apagada_YSoloEstaConversacion()
    {
        Net.Answers.Enqueue(new FakeHttp.Answer(Tools:
        [
            (ToolBox.ListFolder, new JsonObject { ["path"] = Sandbox.Work }.ToJsonString()),
            ("no_existe", "{}"),
            (ToolBox.WriteClipboard, "no es json"),
            (ToolBox.SystemInfo, "{}"),
        ]));
        Net.Answers.Enqueue(new FakeHttp.Answer(Tools: [(ToolBox.FindFiles, new JsonObject { ["pattern"] = "*.txt" }.ToJsonString()), (ToolBox.FindFiles, new JsonObject { ["pattern"] = "*.md" }.ToJsonString())]));
        Net.Answers.Enqueue(new FakeHttp.Answer("Hecho."));
        AppSettings.Current.SetPermission(Resource.Clipboard, Permission.Deny);
        Ui.Run(() =>
        {
            var w = Open();
            Ui.Click(Ui.Find<ButtonBase>(w, "WorkModeButton"));
            Ui.Expect<PermissionWindow>(p => Ui.Click(Ui.All<Button>(p).Single(b => Equals(b.ToolTip, Loc.Get("Cancel")))));   // list_folder: no
            Ui.Expect<PermissionWindow>(p =>
            {
                Ui.All<CheckBox>(p).First().IsChecked = true;   // en esta conversacion
                Ui.Click(Ui.All<Button>(p).Single(b => Equals(b.ToolTip, Loc.Get("PermAllow"))));
            });
            Ask(w, "busca cosas");
            Assert.Contains("Hecho.", Bubbles(w));
            var tools = Net.ChatRequests[1]["messages"]!.AsArray().Where(m => m!["role"]!.GetValue<string>() == "tool").Select(m => m!["content"]!.GetValue<string>()).ToList();
            Assert.Contains("The user declined this action.", tools);
            Assert.Contains("Unknown tool: no_existe", tools);
            Assert.Contains("This resource is turned off in the app settings.", tools);
            Assert.Equal(Permission.Ask, AppSettings.Current.PermissionFor(Resource.FilesRead));
            Ui.Click(Ui.Find<ButtonBase>(w, "AskModeButton"));   // y vuelta al modo preguntas
        });
        var thread = ThreadStore.LoadAll().Single();
        Assert.Contains(Resource.FilesRead.ToString(), thread.Approved);
        Assert.False(thread.WorkMode);
    }

    [Fact]
    public void TareaProgramada_SeHaceSola_YSinPermisoNoToca()
    {
        Scheduler.Add("Resumen", "resume mis notas", "every 1 minute");
        foreach (var t in Scheduler.All)
            t.NextRun = DateTimeOffset.Now.AddMinutes(-1);
        Net.Answers.Enqueue(new FakeHttp.Answer(Tools: [(ToolBox.ReadFile, new JsonObject { ["path"] = "x" }.ToJsonString())]));
        Net.Answers.Enqueue(new FakeHttp.Answer("Resumen hecho."));
        try
        {
            Ui.Run(() =>
            {
                var w = Open();
                Ui.Wait((Task)Ui.Call(w, "RunDueTasksAsync")!);
                Assert.Contains("Resumen hecho.", Bubbles(w));
                Assert.StartsWith("⏰ ", Ui.Find<ListBox>(w, "ThreadList").Items[0].ToString());
            });
            var tool = ThreadStore.LoadAll().Single().Messages.Single(m => m.Role == "tool");
            Assert.StartsWith("Unattended run", tool.Content);
        }
        finally
        {
            foreach (var t in Scheduler.All.ToList())
                Scheduler.Remove(t.Id);
        }
    }

    [Fact]
    public void Conversaciones_Cambiar_Renombrar_Borrar_YNueva()
    {
        Net.Answers.Enqueue(new FakeHttp.Answer("uno"));
        Net.Answers.Enqueue(new FakeHttp.Answer("dos"));
        Ui.Run(() =>
        {
            var w = Open();
            Ask(w, "primera pregunta");
            Ui.Click(Ui.Find<ButtonBase>(w, "NewChatButton"));
            Assert.Empty(Messages(w).Children);
            Ask(w, "segunda pregunta");
            var list = Ui.Find<ListBox>(w, "ThreadList");
            Assert.Equal(2, list.Items.Count);
            list.SelectedIndex = 1;   // la primera
            Assert.Contains("uno", Bubbles(w));

            var row = list.Items[1];
            Ui.Answer("Ok", text: "  Mi charla  ");
            Ui.Call(w, "OnRenameThread", new FrameworkElement { DataContext = row }, new RoutedEventArgs());
            Assert.Equal("Mi charla", list.Items[1].ToString());
            Ui.Answer("Cancel");
            Ui.Call(w, "OnRenameThread", new FrameworkElement { DataContext = list.Items[1] }, new RoutedEventArgs());
            Ui.Call(w, "OnRenameThread", new FrameworkElement(), new RoutedEventArgs());

            Ui.Answer("Cancel");
            Ui.Call(w, "OnDeleteThread", new FrameworkElement { DataContext = list.Items[1] }, new RoutedEventArgs());
            Assert.Equal(2, list.Items.Count);
            Ui.Answer("Yes");
            Ui.Call(w, "OnDeleteThread", new FrameworkElement { DataContext = list.Items[1] }, new RoutedEventArgs());   // la que se ve
            Assert.Single(list.Items);
            Assert.Equal(Visibility.Visible, Ui.Find<FrameworkElement>(w, "EmptyState").Visibility);
            Ui.Call(w, "OnDeleteThread", new FrameworkElement(), new RoutedEventArgs());
            Loc.Use("en");
            Assert.Equal(Loc.Get("Conversations"), Ui.Find<TextBlock>(w, "ConversationsTitle").Text);
            Loc.Use("es");
        });
        Assert.Single(ThreadStore.LoadAll());
    }

    [Fact]
    public void Burbujas_Copiar_Editar_YVolverAEnviar()
    {
        Net.Answers.Enqueue(new FakeHttp.Answer("Respuesta con `codigo`\n\n```cs\nvar x = 1;\n```"));
        Net.Answers.Enqueue(new FakeHttp.Answer("Otra vez."));
        Ui.Run(() =>
        {
            var w = Open();
            Ask(w, "pregunta original");
            var buttons = Ui.All<Button>(Messages(w)).ToList();
            Ui.Click(buttons.First(b => Equals(b.ToolTip, Loc.Get("Copy"))));          // la de la pregunta
            Assert.Equal("pregunta original", Fake.Clipboard);
            Ui.Click(buttons.First(b => Equals(b.ToolTip, Loc.Get("EditQuestion"))));
            Assert.Equal("pregunta original", Ui.Find<TextBox>(w, "Composer").Text);
            Ui.Click(buttons.Last(b => Equals(b.ToolTip, Loc.Get("Copy"))));           // la de la respuesta
            Assert.StartsWith("Respuesta con", Fake.Clipboard);
            Fake.Saves.Enqueue(Path.Combine(Sandbox.Work, "codigo.cs"));
            Ui.Click(buttons.First(b => Equals(b.ToolTip, Loc.Get("SaveCode"))));
            Assert.Equal("var x = 1;", File.ReadAllText(Path.Combine(Sandbox.Work, "codigo.cs")).Trim());
            Ui.Click(buttons.First(b => Equals(b.ToolTip, Loc.Get("ResendQuestion"))));
            Ui.WaitFor(() => Net.ChatRequests.Count == 2 && !Answering(w));
            Assert.Contains("Otra vez.", Bubbles(w));
        });
    }

    private static string Png(string path, Color color)
    {
        var bitmap = BitmapSource.Create(4, 4, 96, 96, PixelFormats.Bgra32, null, Enumerable.Repeat(new[] { color.B, color.G, color.R, (byte)255 }, 16).SelectMany(b => b).ToArray(), 16);
        using var stream = File.Create(path);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        encoder.Save(stream);
        return path;
    }

    [Fact]
    public void Adjuntos_TextoEImagen_ElBinarioYElEnormeNo()
    {
        var text = Path.Combine(Sandbox.Work, "datos.csv");
        File.WriteAllText(text, "a,b\n1,2");
        var noExt = Path.Combine(Sandbox.Work, "LEEME");
        File.WriteAllText(noExt, "texto sin extension");
        var binary = Path.Combine(Sandbox.Work, "raro.bin");
        File.WriteAllBytes(binary, [1, 0, 2, 0]);
        var huge = Path.Combine(Sandbox.Work, "enorme.txt");
        using (var f = File.Create(huge))
            f.SetLength(21L * 1024 * 1024);
        var image = Png(Path.Combine(Sandbox.Work, "foto.png"), Colors.Red);
        Net.Answers.Enqueue(new FakeHttp.Answer("Visto."));
        Ui.Run(() =>
        {
            var w = Open();
            Fake.Files.Enqueue([text, noExt, binary, huge, image, Path.Combine(Sandbox.Work, "no-esta.txt")]);
            var messages = new List<string>();
            Ui.Answer("Ok", messages.Add);   // binario
            Ui.Answer("Ok", messages.Add);   // enorme
            Ui.Click(Ui.Find<ButtonBase>(w, "AttachButton"));
            Assert.Equal(2, messages.Count);
            var chips = Ui.Find<WrapPanel>(w, "AttachmentsPanel");
            Assert.Equal(3, chips.Children.Count);
            // quitar la ficha sin extension
            Ui.Click(Ui.All<Button>(chips.Children[1]).Single());
            Assert.Equal(2, chips.Children.Count);
            Fake.Files.Enqueue(null);   // cancelado
            Ui.Click(Ui.Find<ButtonBase>(w, "AttachButton"));
            Ui.Find<TextBox>(w, "Composer").Text = string.Empty;
            Ui.Click(Ui.Find<ButtonBase>(w, "SendButton"));   // solo adjuntos
            Ui.WaitFor(() => Net.ChatRequests.Count == 1 && !Answering(w));
            var sent = Net.ChatRequests[0]["messages"]!.AsArray().Last()!["content"]!.GetValue<string>();
            Assert.Contains("[Attached file: datos.csv]", sent);
            Assert.Contains("a,b", sent);
            Assert.Contains("no vision", sent);   // la IA no ve la imagen y se le dice
            Assert.Contains(Loc.Get("NoVisionModel"), Bubbles(w));
            Assert.Equal(Visibility.Collapsed, chips.Visibility);
            // la ficha de la burbuja abre el fichero
            Ui.Click(Ui.All<Button>(Messages(w)).First(b => Equals(b.ToolTip, Loc.Get("Open"))));
            Assert.Single(Fake.Opened);
        });
    }

    [Fact]
    public void Pegar_ImagenOFicheros_SeAdjuntan()
    {
        var text = Path.Combine(Sandbox.Work, "pegado.txt");
        File.WriteAllText(text, "hola");
        Ui.Run(() =>
        {
            var w = Open();
            Assert.False(w.PasteAttachments());   // portapapeles vacio
            Fake.ClipboardFileList = [text];
            Assert.True(w.PasteAttachments());
            Fake.ClipboardFileList = null;
            Fake.Image = BitmapSource.Create(2, 2, 96, 96, PixelFormats.Bgra32, null, new byte[16], 8);
            Assert.True(w.PasteAttachments());
            Fake.Clipboard = "con texto: pega el texto, no la imagen";
            Assert.False(w.PasteAttachments());
            var chips = Ui.Find<WrapPanel>(w, "AttachmentsPanel");
            Assert.Equal(2, chips.Children.Count);
            Ui.Click(Ui.All<Button>(chips.Children[1]).Single());   // quitar la imagen pegada (borra su temporal)
            Assert.Single(chips.Children);
        });
    }

    [Fact]
    public void Imagenes_Generadas_YDocumentos_EnLaConversacion()
    {
        var png = Png(Path.Combine(Sandbox.Work, "generada.png"), Colors.Blue);
        var doc = Path.Combine(Sandbox.Work, "informe.docx");
        File.WriteAllText(doc, "x");
        Ui.Run(() =>
        {
            var w = Open();
            var image = (Border)Ui.Call(w, "ToolBubble", Resource.Free, "generate_image  un gato", "Image: " + png)!;
            Assert.Single(Ui.All<System.Windows.Controls.Image>(image));
            var buttons = Ui.All<Button>(image).ToList();
            Ui.Click(buttons.Single(b => Equals(b.Content, Loc.Get("OpenImage"))));
            Assert.Equal(png, Fake.Opened.Single());
            var copy = Path.Combine(Sandbox.Work, "copia.png");
            Fake.Saves.Enqueue(copy);
            Ui.Click(buttons.Single(b => Equals(b.Content, Loc.Get("SaveImage"))));
            Assert.True(File.Exists(copy));
            Fake.Saves.Enqueue(Path.Combine(Sandbox.Work, "no", "existe", "x.png"));
            Ui.Answer("Ok");
            Ui.Click(buttons.Single(b => Equals(b.Content, Loc.Get("SaveImage"))));
            Fake.Saves.Enqueue(null);
            Ui.Click(buttons.Single(b => Equals(b.Content, Loc.Get("SaveImage"))));
            Ui.All<System.Windows.Controls.Image>(image).Single().RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left) { RoutedEvent = UIElement.MouseLeftButtonUpEvent });
            Assert.Equal(2, Fake.Opened.Count);

            var saved = (Border)Ui.Call(w, "ToolBubble", Resource.Free, "save_document", "Saved: " + doc)!;
            Ui.Click(Ui.All<Button>(saved).Single());
            Assert.Equal(doc, Fake.Opened[^1]);
            Fake.OpenError = new InvalidOperationException("sin programa");
            Ui.Click(Ui.All<Button>(saved).Single());   // sin programa: nada
            var plain = (Border)Ui.Call(w, "ToolBubble", Resource.Commands, "run_command dir", new string('x', 700))!;
            Assert.False(Ui.All<Expander>(plain).Single().IsExpanded);   // salida larga: plegada
        });
    }

    [Fact]
    public void ElegirOtraIA_DeLasInstaladas()
    {
        var other = Path.Combine(Paths.DefaultModels, "Otra-Q4_K_M.gguf");
        File.WriteAllBytes(other, [9]);
        AppSettings.Current.Remember(new InstalledModel { Name = "Prueba", Path = ModelFile });
        AppSettings.Current.Remember(new InstalledModel { Name = "Otra", Path = other });
        Ui.Run(() =>
        {
            var w = Open();
            var combo = Ui.Find<ComboBox>(w, "ModelCombo");
            Assert.True(combo.Items.Count >= 2);
            combo.SelectedItem = combo.Items.Cast<object>().First(i => i.ToString()!.Contains("Otra"));
            Assert.Equal(other, AppSettings.Current.ModelPath);
            combo.SelectedItem = combo.SelectedItem;   // la misma: nada
            Ui.WaitFor(() => App.Engine.State == EngineState.Ready);
        });
    }

    [Fact]
    public void SinModelo_AbreAjustes_YPreguntarTambien()
    {
        AppSettings.Current.ModelPath = null;
        Ui.Run(() =>
        {
            Ui.Expect<SettingsWindow>(s => s.Close());
            var w = new MainWindow();
            w.Show();
            Ui.WaitFor(() => Ui.Pending == 0);
            Ui.Expect<SettingsWindow>(s => s.Close());
            Ui.Find<TextBox>(w, "Composer").Text = "hola";
            Ui.Click(Ui.Find<ButtonBase>(w, "SendButton"));
            Ui.WaitFor(() => Ui.Pending == 0);
            Assert.Equal("hola", Ui.Find<TextBox>(w, "Composer").Text);   // lo escrito no se pierde
            Ui.Find<TextBox>(w, "Composer").Text = "  ";
            Ui.Click(Ui.Find<ButtonBase>(w, "SendButton"));   // vacio: nada
        });
    }

    [Fact]
    public void ElMotorNoArranca_LoDice()
    {
        Environment.SetEnvironmentVariable("FAKE_LLAMA_EXIT", "3");
        Net.Healthy = false;   // que no conteste antes de que el proceso se vaya
        try
        {
            Ui.Run(() =>
            {
                var w = new MainWindow();
                w.Show();
                Ui.WaitFor(() => App.Engine.State == EngineState.Error);
                Ui.WaitFor(() => Status(w).StartsWith(Loc.Get("EngineError"), StringComparison.Ordinal));
                Ask(w, "hola");
                Assert.Contains(Loc.Format("AnswerFailed", "")[..10], Bubbles(w));
            });
        }
        finally
        {
            Environment.SetEnvironmentVariable("FAKE_LLAMA_EXIT", null);
        }
    }

    [Fact]
    public void Ajustes_AcercaDe_Teclas_YBandeja()
    {
        Net.Answers.Enqueue(new FakeHttp.Answer("Con intro."));
        Ui.Run(() =>
        {
            var w = Open();
            Ui.Expect<SettingsWindow>(s => s.Close());
            Ui.Click(Ui.Find<ButtonBase>(w, "SettingsButton"));
            Ui.Expect<AboutWindow>(a => a.Close());
            Ui.Click(Ui.Find<ButtonBase>(w, "AboutButton"));
            var composer = Ui.Find<TextBox>(w, "Composer");
            composer.Text = "con intro";
            Ui.Call(w, "OnComposerKey", composer, new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(w)!, 0, Key.Enter) { RoutedEvent = Keyboard.KeyDownEvent });
            Ui.WaitFor(() => Net.ChatRequests.Count == 1 && !Answering(w));
            Ui.Call(w, "OnComposerKey", composer, new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(w)!, 0, Key.A) { RoutedEvent = Keyboard.KeyDownEvent });

            // la bandeja de verdad: minimizar esconde, el icono la devuelve, «Salir» cierra la aplicacion
            Assert.NotNull(w.Tray);
            var hwnd = new WindowInteropHelper(w).Handle;
            SendMessage(hwnd, 0x0112, 0xF020, 0);
            Assert.False(w.IsVisible);
            Assert.True(w.Tray!.Hidden);
            SendMessage(hwnd, 0x8001, 0, 0x0202);
            Assert.True(w.IsVisible);
            w.WindowState = WindowState.Minimized;
            Ui.WaitFor(() => !w.IsVisible);
            ClickTray(w.Tray, 0);   // Abrir (menu WPF con el aspecto comun, General 6.14)
            Assert.True(w.IsVisible);
            w.Tray.MinimizeToTray = false;
            SendMessage(hwnd, 0x0112, 0xF020, 0);
            w.WindowState = WindowState.Normal;
            ClickTray(w.Tray, 2);   // Salir (el 1 es el separador)
            Assert.Equal(1, Fake.Shutdowns);
            w.Tray.Dispose();
        });
    }

    [Fact]
    public void Vision_SeOfreceBajarla_YLuegoLaImagenVaAlModelo()
    {
        var image = Png(Path.Combine(Sandbox.Work, "gato.png"), Colors.Gray);
        AppSettings.Current.Remember(new InstalledModel { Name = "Prueba", Path = ModelFile, Repo = "org/Prueba-GGUF" });
        Net.Other = request =>
        {
            var url = request.RequestUri!.ToString();
            if (url.EndsWith("/tree/main", StringComparison.Ordinal))
                return new HttpResponseMessage(System.Net.HttpStatusCode.OK) { Content = new StringContent("""[{"type":"file","path":"mmproj-F16.gguf","size":3}]""") };
            if (url.Contains("/resolve/main/mmproj", StringComparison.Ordinal))
                return new HttpResponseMessage(System.Net.HttpStatusCode.OK) { Content = new ByteArrayContent([7, 7, 7]) };
            return null;
        };
        Net.Answers.Enqueue(new FakeHttp.Answer("No veo nada."));
        Net.Answers.Enqueue(new FakeHttp.Answer("Es un gato gris."));
        Ui.Run(() =>
        {
            var w = Open();
            // la primera vez se dice que no: sigue sin imagen
            Fake.Files.Enqueue([image]);
            Ui.Click(Ui.Find<ButtonBase>(w, "AttachButton"));
            Ui.Answer("Cancel");
            Ask(w, "¿qué hay?");
            Assert.Contains("No veo nada.", Bubbles(w));
            Assert.True(Ui.Field<bool>(w, "_visionDeclined"));
            typeof(MainWindow).GetField("_visionDeclined", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.SetValue(w, false);
            // la segunda, que si: baja la parte de vision y el motor arranca con ella
            Fake.Files.Enqueue([image]);
            Ui.Click(Ui.Find<ButtonBase>(w, "AttachButton"));
            Ui.Answer("Yes");
            Ask(w, "¿y ahora?");
            Assert.True(File.Exists(ModelCatalog.MmprojPathFor(ModelFile)));
            Assert.True(App.Engine.Vision);
            Assert.Contains("Es un gato gris.", Bubbles(w));
            var content = Net.ChatRequests[1]["messages"]!.AsArray().Last()!["content"]!.AsArray();
            Assert.StartsWith("data:image/jpeg;base64,", content[0]!["image_url"]!["url"]!.GetValue<string>());
            Assert.Contains(Loc.Get("VisionOn"), Status(w));
        });
        File.Delete(ModelCatalog.MmprojPathFor(ModelFile));
    }

    [Fact]
    public void PlataformaDeVerdad_LoQueSoloLee()
    {
        var real = new RealDesktop();
        Assert.Equal(Microsoft.Win32.Registry.CurrentUser.Name, real.CurrentUser.Name);
        Ui.Run(() =>
        {
            _ = real.ClipboardHasText();
            _ = real.ClipboardFiles();
            _ = real.ClipboardImage();
        });
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool IsWindowEnabled(IntPtr hwnd);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hwnd, int command);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern int GetWindowLong(IntPtr hwnd, int index);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr hwnd);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern IntPtr SendMessage(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam);

    /// <summary>Pulsa un elemento del menu de la bandeja (el mismo que se abre con el boton derecho).</summary>
    private static void ClickTray(TrayIcon tray, int index) =>
        ((MenuItem)tray.BuildMenu().Items[index]).RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
}
