using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Text.Json.Nodes;
using SocLucia.Agent;
using SocLucia.Chat;
using SocLucia.Engine;
using SocLucia.Localization;
using SocLucia.Services;

namespace SocLucia;

public partial class MainWindow : Window
{
    private readonly List<ChatThread> _threads;
    private ChatThread? _current;
    private CancellationTokenSource? _answering;

    /// <summary>El icono del area de notificacion (lo usa Ajustes para cambiar su comportamiento y App para arrancar escondida).</summary>
    public TrayIcon? Tray { get; private set; }

    public MainWindow()
    {
        App.GuardDialogs();   // diálogos: al registro y sin minimizarse por su cuenta
        InitializeComponent();
        SourceInitialized += (_, _) =>
        {
            ThemeManager.ApplyToWindow(this);
            Tray = new TrayIcon(this, Loc.Get, () => Desktop.Current.Shutdown()) { MinimizeToTray = AppSettings.Current.TrayOnMinimize };
        };
        EmptyLogo.Source = new BitmapImage(new Uri("pack://application:,,,/Assets/logo.png"));
        EngineSpinner.Content = Controls.BusyIndicator.Spinner(12);
        _threads = ThreadStore.LoadAll();
        ApplyTexts();
        Loc.LanguageChanged += ApplyTexts;
        App.Engine.StateChanged += OnEngineStateChanged;
        // La IA de imagenes: su estado (descargando, generando) y el progreso van a la misma barra.
        ImageEngine.Status += OnImageStatus;
        ToolBox.ImageProgress = new Progress<Downloader.Progress>(PaintDownload);
        // Pegar una imagen o ficheros en el redactor los adjunta.
        DataObject.AddPastingHandler(Composer, OnComposerPaste);
        ModelDownloads.Changed += PaintEngineStatus;
        ModelDownloads.Finished += OnModelDownloadFinished;
        // Lo enganchado a piezas estaticas se suelta al cerrar (la aplicacion solo cierra al salir,
        // pero asi una ventana cerrada no sigue pintando).
        Closed += (_, _) =>
        {
            Loc.LanguageChanged -= ApplyTexts;
            App.Engine.StateChanged -= OnEngineStateChanged;
            ImageEngine.Status -= OnImageStatus;
            ModelDownloads.Changed -= PaintEngineStatus;
            ModelDownloads.Finished -= OnModelDownloadFinished;
            _scheduleTimer?.Stop();
            _waitTimer?.Stop();
        };
        RefreshThreadList();
        ShowThread(null);   // al abrir, conversacion nueva; las anteriores quedan en la lista
        Loaded += (_, _) =>
        {
            Composer.Focus();
            if (!AppSettings.Current.HasModel)
                OpenSettings();
            else
                _ = WarmUpAsync();
        };
        Closing += (_, _) => _answering?.Cancel();
        StartScheduler();
    }

    private void OnEngineStateChanged() => Dispatcher.BeginInvoke(PaintEngineStatus);

    private void OnImageStatus(string? s) => Dispatcher.BeginInvoke(() => { _imageStatus = s; PaintEngineStatus(); });

    private void OnModelDownloadFinished(CatalogModel model, Exception? error)
    {
        if (error is null) ModelChanged(); else PaintEngineStatus();
    }

    private void ApplyTexts()
    {
        Title = Loc.Get("AppTitle");
        ConversationsTitle.Text = Loc.Get("Conversations");
        NewChatButton.ToolTip = Loc.Get("NewChat");
        DeleteAllButton.ToolTip = Loc.Get("DeleteAllChats");
        SettingsButton.ToolTip = Loc.Get("Settings");
        AboutButton.ToolTip = Loc.Get("AboutTooltip");
        if (IsLoaded) RefreshThreadList();   // los avisos de renombrar/borrar de cada fila
        SendButton.ToolTip = Loc.Get("Send");
        StopButton.ToolTip = Loc.Get("Stop");
        WorkModeButton.ToolTip = Loc.Get("WorkMode");
        WorkModeText.Text = Loc.Get("AgentMode");
        AskModeButton.ToolTip = Loc.Get("AskModeHint");
        AskModeText.Text = Loc.Get("AskMode");
        PaintMode();
        PaintModels();
        EmptyTitle.Text = Loc.Get("EmptyTitle");
        EmptyHint.Text = Loc.Get("EmptyHint");
        Composer.ToolTip = Loc.Get("ComposerHint");
        AttachButton.ToolTip = Loc.Get("Attach");
        ThinkText.Text = Loc.Get("ThinkToggle");
        ThinkButton.ToolTip = Loc.Get("ThinkingHint");
        ThinkButton.IsChecked = AppSettings.Current.Thinking;
        ContextTitle.Text = Loc.Get("ContextTitle");
        PaintContext();
        PaintAttachments();
        PaintEngineStatus();
        if (_current is not null)
            ShowThread(_current);
    }

    // ------------------------------------------------------------------ motor

    private async Task WarmUpAsync()
    {
        try { await EngineReadyAsync(CancellationToken.None); }
        catch (Exception) { /* el estado ya lo enseña la barra */ }
    }

    /// <summary>
    /// La direccion del motor, arrancandolo si hace falta, siempre fuera del hilo de la interfaz:
    /// descomprimir el motor, lanzar llama-server, matar el anterior y esperar a que cargue el
    /// modelo no deben congelar la ventana. El progreso y el estado vuelven por el Dispatcher.
    /// </summary>
    private Task<string> EngineReadyAsync(CancellationToken cancel)
    {
        var progress = new Progress<Downloader.Progress>(PaintDownload);
        return Task.Run(() => App.Engine.EnsureReadyAsync(progress, cancel), cancel);
    }

    private void PaintDownload(Downloader.Progress p) => Dispatcher.BeginInvoke(() =>
    {
        EngineProgress.Visibility = Visibility.Visible;
        EngineProgress.IsIndeterminate = p.Fraction is null;
        EngineProgress.Value = (p.Fraction ?? 0) * 100;
        if (p.Fraction is >= 1)
            EngineProgress.Visibility = Visibility.Collapsed;
    });

    private string? _imageStatus;

    private void PaintEngineStatus()
    {
        var engine = App.Engine;
        var settings = AppSettings.Current;
        var model = settings.ModelName is { Length: > 0 } name ? name : null;
        // Mientras se descarga o arranca el motor, se baja una IA o se dibuja: el arco de espera junto al estado.
        EngineSpinner.Visibility = _imageStatus is { Length: > 0 } || ModelDownloads.Current is not null || engine.State is EngineState.DownloadingEngine or EngineState.Starting
            ? Visibility.Visible : Visibility.Collapsed;
        if (_imageStatus is { Length: > 0 })
        {
            EngineStatus.Text = _imageStatus;
            return;
        }
        if (ModelDownloads.Current is { } download)
        {
            // Una IA bajando en segundo plano: se ve aqui aunque Ajustes este cerrado.
            var others = ModelDownloads.All.Count - 1;
            var size = download.Total is { } t ? $"{Human(download.Received)} / {Human(t)}" : Human(download.Received);
            EngineStatus.Text = others > 0 ? Loc.Format("ModelInstallingMore", download.Model.Name, size, others) : Loc.Format("ModelInstalling", download.Model.Name, size);
            EngineProgress.Visibility = Visibility.Visible;
            EngineProgress.IsIndeterminate = download.Fraction is null;
            EngineProgress.Value = (download.Fraction ?? 0) * 100;
            return;
        }
        EngineStatus.Text = engine.State switch
        {
            EngineState.NoModel => Loc.Get("EngineNoModel"),
            EngineState.DownloadingEngine => Loc.Get("EngineDownloading"),
            EngineState.Starting => Loc.Get("EngineStarting"),
            EngineState.Ready => $"{Loc.Get("EngineReady")} · {model}" + (engine.Vision ? " · " + Loc.Get("VisionOn") : string.Empty),
            EngineState.Stopped => Loc.Get("EngineStopped"),
            _ => $"{Loc.Get("EngineError")}: {engine.Detail}",
        };
        EngineProgress.Visibility = engine.State is EngineState.DownloadingEngine or EngineState.Starting ? Visibility.Visible : Visibility.Collapsed;
        EngineProgress.IsIndeterminate = engine.State == EngineState.Starting;
    }

    /// <summary>Tras cambiar de modelo en Ajustes: el motor se reinicia con el nuevo en la siguiente pregunta, y de paso se calienta.</summary>
    public void ModelChanged()
    {
        PaintModels();
        PaintContext();
        PaintEngineStatus();
        if (AppSettings.Current.HasModel)
            _ = WarmUpAsync();
    }

    private sealed record ModelRow(string Name, string Path, string? License)
    {
        public override string ToString() => Name;
    }

    private bool _paintingModels;

    /// <summary>Las IA instaladas (y la activa aunque este fuera de la carpeta), con la activa seleccionada.</summary>
    private void PaintModels()
    {
        _paintingModels = true;
        try
        {
            var s = AppSettings.Current;
            var rows = ModelCatalog.InstalledFiles().Select(m => new ModelRow(m.Name, m.Path, m.License)).ToList();
            if (s.HasModel && !rows.Any(r => string.Equals(r.Path, s.ModelPath, StringComparison.OrdinalIgnoreCase)))
                rows.Insert(0, new ModelRow(s.ModelName ?? System.IO.Path.GetFileName(s.ModelPath!), s.ModelPath!, s.ModelLicense));
            ModelCombo.ItemsSource = rows;
            ModelCombo.SelectedItem = rows.FirstOrDefault(r => string.Equals(r.Path, s.ModelPath, StringComparison.OrdinalIgnoreCase));
            ModelCombo.ToolTip = Loc.Get("ModelPick");
            ModelCombo.Visibility = rows.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        }
        finally { _paintingModels = false; }
    }

    private void OnModelPicked(object sender, SelectionChangedEventArgs e)
    {
        if (_paintingModels || ModelCombo.SelectedItem is not ModelRow row) return;
        var s = AppSettings.Current;
        if (string.Equals(row.Path, s.ModelPath, StringComparison.OrdinalIgnoreCase)) return;
        s.ModelPath = row.Path;
        s.ModelName = row.Name;
        s.ModelLicense = row.License;
        s.Save();
        ModelChanged();
    }

    private static string Human(long bytes) => bytes switch
    {
        >= 1L << 30 => $"{bytes / (double)(1L << 30):0.0} GB",
        >= 1L << 20 => $"{bytes / (double)(1L << 20):0} MB",
        _ => $"{bytes / 1024.0:0} KB",
    };

    // ------------------------------------------------------------------ conversaciones

    private void RefreshThreadList()
    {
        var selected = _current;
        DeleteAllButton.IsEnabled = _threads.Count > 0;
        ThreadList.ItemsSource = null;
        ThreadList.ItemsSource = _threads.Select(t => new ThreadRow(t)).ToList();
        if (selected is not null)
            ThreadList.SelectedItem = ((IEnumerable<ThreadRow>)ThreadList.ItemsSource).FirstOrDefault(r => r.Thread == selected);
    }

    private sealed record ThreadRow(ChatThread Thread)
    {
        public string Title => Thread.Title.Length > 0 ? Thread.Title : "…";
        public string RenameTip => Loc.Get("RenameChat");
        public string DeleteTip => Loc.Get("DeleteChat");
        public override string ToString() => Title;
    }

    private void OnThreadSelected(object sender, SelectionChangedEventArgs e)
    {
        if (ThreadList.SelectedItem is ThreadRow row && row.Thread != _current)
            ShowThread(row.Thread);
    }

    /// <summary>Abre una conversacion nueva (p. ej. para probar la IA recien enseñada).</summary>
    public void NewConversation() => OnNewChat(this, new RoutedEventArgs());

    private void OnNewChat(object sender, RoutedEventArgs e)
    {
        _answering?.Cancel();
        ThreadList.SelectedItem = null;
        ShowThread(null);
        Composer.Focus();
    }

#if DEBUG
    /// <summary>Solo para capturas: una conversacion de mentira (sin guardar) cuya respuesta es ese Markdown.</summary>
    internal void ShowMarkdownForTest(string markdown) => ShowThread(new ChatThread
    {
        Title = "Markdown",
        Messages = [new StoredMessage { Role = "user", Content = "Enséñame Markdown" }, new StoredMessage { Role = "assistant", Content = markdown }],
    });
#endif

    private void ShowThread(ChatThread? thread)
    {
        _current = thread;
        PaintMode(thread?.WorkMode ?? _pendingWorkMode);
        Messages.Children.Clear();
        EmptyState.Visibility = thread is null || thread.Messages.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        if (thread is null)
            return;
        foreach (var message in thread.Messages)
        {
            if (message.Role == "tool")
                Messages.Children.Add(ToolBubble(ResourceOf(message.Command), message.Command ?? string.Empty, message.Content));
            else if (message.Role == "assistant" && message.Content.Length == 0 && message.ToolCalls is not null)
                continue;   // la peticion de herramienta sin texto: ya se ve la orden en la burbuja de la herramienta
            else
                Messages.Children.Add(Bubble(message.Role, message.Content, message.Reasoning, message.Attachments));
        }
        ScrollToEnd();
    }

    private void OnWorkModeToggled(object sender, RoutedEventArgs e) => SetMode(agent: true);

    /// <summary>Pensar antes de responder: se cambia desde el chat y vale para las siguientes preguntas.</summary>
    private void OnThinkToggled(object sender, RoutedEventArgs e)
    {
        AppSettings.Current.Thinking = ThinkButton.IsChecked == true;
        AppSettings.Current.Save();
    }

    // ------------------------------------------------------------------ contexto (encima del chat)

    private Engine.TokenLimits.ContextRange? _contextRange;
    private bool _paintingContext;

    /// <summary>El control de contexto: su rango sale de la IA activa (cabecera del GGUF) y cambia al cambiar de IA.</summary>
    private void PaintContext()
    {
        var s = AppSettings.Current;
        ContextBorder.Visibility = s.HasModel ? Visibility.Visible : Visibility.Collapsed;
        if (!s.HasModel) { if (ContextPopup is not null) ContextPopup.IsOpen = false; return; }
        _paintingContext = true;
        try
        {
            var range = _contextRange = Engine.TokenLimits.RangeFor(s);
            var value = Math.Clamp(Engine.TokenLimits.Context(s), range.Min, range.Max);
            ContextAutoCheck.Content = Loc.Get("AutoContext");
            ContextAutoCheck.IsChecked = s.AutoContext;
            ContextSlider.Minimum = range.Min;
            ContextSlider.Maximum = Math.Max(range.Min, range.Max);
            ContextSlider.IsEnabled = !s.AutoContext;
            ContextSlider.Value = value;
            ContextMinText.Text = ContextShort(range.Min);
            ContextMaxText.Text = ContextShort(range.Max);
            ContextValueText.Text = Loc.Format("ContextValue", value);
            ContextButtonText.Text = ContextShort(value) + (s.AutoContext ? " " + Loc.Get("ContextAutoShort") : string.Empty);
            ContextButton.ToolTip = Loc.Format("ContextButtonTip", range.Min, range.Max);
            ContextPopupHint.Text = (range.Trained is { } tr ? Loc.Format("ContextTrainedShort", tr) : Loc.Get("ContextUnknownModel")) + ". " + Loc.Get("ContextRestart");
        }
        finally { _paintingContext = false; }
    }

    private void OnContextAuto(object sender, RoutedEventArgs e)
    {
        if (_paintingContext) return;
        var s = AppSettings.Current;
        s.AutoContext = ContextAutoCheck.IsChecked == true;
        s.Save();
        DiagLog.Write("chat", $"contexto automatico: {(s.AutoContext ? "si" : "no")} (en uso al reiniciar: {Engine.TokenLimits.Context(s)})");
        PaintContext();
    }

    private void OnContextSlider(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_paintingContext || _contextRange is null) return;
        var s = AppSettings.Current;
        var value = Math.Clamp((int)Math.Round(e.NewValue), _contextRange.Min, _contextRange.Max);
        ContextValueText.Text = Loc.Format("ContextValue", value);
        ContextButtonText.Text = ContextShort(value);
        // Ajustar a mano apaga el automatico; el motor se recarga con el nuevo contexto en la siguiente pregunta.
        if (s.AutoContext) { s.AutoContext = false; ContextAutoCheck.IsChecked = false; ContextSlider.IsEnabled = true; }
        if (s.ContextTokens != value) { s.ContextTokens = value; s.Save(); }
    }

    /// <summary>8192 → «8k», 18432 → «18k», 40960 → «40k».</summary>
    internal static string ContextShort(int tokens) => tokens % 1024 == 0 ? $"{tokens / 1024}k" : $"{tokens / 1024.0:0.#}k";

    // ------------------------------------------------------------------ adjuntos

    /// <summary>Lo que se va a adjuntar a la siguiente pregunta: ficheros elegidos, arrastrados o pegados (las imagenes pegadas van a un PNG temporal).</summary>
    private readonly List<(string Name, string Path, bool IsImage, bool Temp)> _pendingFiles = [];

    private static readonly HashSet<string> TextExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".txt", ".md", ".markdown", ".csv", ".tsv", ".json", ".xml", ".yaml", ".yml", ".toml", ".ini", ".cfg", ".conf", ".log",
        ".cs", ".xaml", ".js", ".ts", ".jsx", ".tsx", ".py", ".java", ".kt", ".c", ".h", ".cpp", ".hpp", ".rs", ".go", ".rb", ".php", ".swift",
        ".html", ".htm", ".css", ".scss", ".sql", ".ps1", ".psm1", ".bat", ".cmd", ".sh", ".gradle", ".csproj", ".sln", ".props", ".targets", ".tex", ".rtf", ".docx", ".pdf",
    };

    private void OnAttach(object sender, RoutedEventArgs e)
    {
        if (Desktop.Current.PickFiles(this, Loc.Get("Attach"), Loc.Get("AttachFilter"), multiselect: true) is not { } files) return;
        foreach (var file in files) AddPendingFile(file);
    }

    private void OnComposerDragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) || e.Data.GetDataPresent(DataFormats.Bitmap) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private void OnComposerDrop(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(DataFormats.FileDrop) is string[] files)
            foreach (var file in files) AddPendingFile(file);
        else if (e.Data.GetData(DataFormats.Bitmap) is BitmapSource bitmap)
            AddPendingImage(bitmap);
        e.Handled = true;
    }

    /// <summary>Ctrl+V con una imagen o con ficheros en el portapapeles: se adjuntan; con texto, pega como siempre.</summary>
    private void OnComposerPaste(object sender, DataObjectPastingEventArgs e)
    {
        try
        {
            if (e.DataObject.GetDataPresent(DataFormats.FileDrop) && e.DataObject.GetData(DataFormats.FileDrop) is string[] files)
            {
                foreach (var file in files) AddPendingFile(file);
                e.CancelCommand();
            }
            else if (!e.DataObject.GetDataPresent(DataFormats.UnicodeText) && Desktop.Current.ClipboardImage() is { } image)
            {
                AddPendingImage(image);
                e.CancelCommand();
            }
        }
        catch (Exception) { }
    }

    private void AddPendingFile(string path)
    {
        if (!System.IO.File.Exists(path)) return;
        var isImage = ThreadStore.IsImageFile(path);
        if (!isImage && !TextExtensions.Contains(System.IO.Path.GetExtension(path)))
        {
            // Sin extension conocida: vale si parece texto (sin bytes nulos en el arranque).
            try
            {
                var head = new byte[4096];
                using var s = System.IO.File.OpenRead(path);
                var n = s.Read(head, 0, head.Length);
                if (Array.IndexOf(head, (byte)0, 0, n) >= 0) { PromptWindow.Alert(this, Loc.Get("Attach"), Loc.Format("AttachUnsupported", System.IO.Path.GetFileName(path))); return; }
            }
            catch (Exception) { return; }
        }
        if (new System.IO.FileInfo(path).Length > 20L * 1024 * 1024) { PromptWindow.Alert(this, Loc.Get("Attach"), Loc.Format("AttachTooBig", System.IO.Path.GetFileName(path))); return; }
        _pendingFiles.Add((System.IO.Path.GetFileName(path), path, isImage, false));
        PaintAttachments();
    }

    private void AddPendingImage(BitmapSource image)
    {
        try
        {
            var temp = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"soclucia-{Guid.NewGuid():N}.png");
            using (var stream = System.IO.File.Create(temp))
            {
                var encoder = new PngBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(image));
                encoder.Save(stream);
            }
            _pendingFiles.Add((Loc.Get("PastedImage"), temp, true, true));
            PaintAttachments();
        }
        catch (Exception) { }
    }

    private void PaintAttachments()
    {
        AttachmentsPanel.Children.Clear();
        AttachmentsPanel.Visibility = _pendingFiles.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        foreach (var file in _pendingFiles)
        {
            var chip = AttachmentChip(file.Name, file.Path, file.IsImage, removable: true);
            var pending = file;
            ((Button)((DockPanel)chip.Child).Children[0]).Click += (_, _) => { _pendingFiles.Remove(pending); if (pending.Temp) try { System.IO.File.Delete(pending.Path); } catch (Exception) { } PaintAttachments(); };
            AttachmentsPanel.Children.Add(chip);
        }
    }

    /// <summary>Una ficha de adjunto: miniatura si es imagen, si no el nombre; con quitar (antes de enviar) o abrir (en la burbuja).</summary>
    private Border AttachmentChip(string name, string path, bool isImage, bool removable)
    {
        var dock = new DockPanel { LastChildFill = true };
        var action = new Button { Style = (Style)FindResource("GhostIconButton"), Content = removable ? "\uE711" : "\uE8A7", Width = 24, Height = 24, FontSize = 11, ToolTip = removable ? Loc.Get("Remove") : Loc.Get("Open") };
        DockPanel.SetDock(action, Dock.Right);
        dock.Children.Add(action);
        if (!removable)
            action.Click += (_, _) => { try { Desktop.Current.Open(path); } catch (Exception) { } };
        if (isImage && System.IO.File.Exists(path))
        {
            try
            {
                var bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.DecodePixelWidth = 160;
                bitmap.UriSource = new Uri(path);
                bitmap.EndInit();
                dock.Children.Add(new Image { Source = bitmap, MaxHeight = 90, Margin = new Thickness(0, 0, 6, 0), ToolTip = name });
            }
            catch (Exception) { dock.Children.Add(new TextBlock { Text = name, VerticalAlignment = VerticalAlignment.Center }); }
        }
        else
            dock.Children.Add(new TextBlock { Text = "\uE8A5  " + name, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis, MaxWidth = 260 });
        return new Border
        {
            Child = dock,
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(8, 4, 4, 4),
            Margin = new Thickness(0, 0, 6, 6),
            Background = (Brush)FindResource("CardBackground"),
            BorderBrush = (Brush)FindResource("Separator"),
            BorderThickness = new Thickness(1),
        };
    }

    /// <summary>Copia lo pendiente a la carpeta de la conversacion y devuelve las fichas (null si no hay nada).</summary>
    private List<Attachment>? TakePendingAttachments(ChatThread thread)
    {
        if (_pendingFiles.Count == 0) return null;
        var list = new List<Attachment>();
        foreach (var file in _pendingFiles)
        {
            try
            {
                var a = ThreadStore.Attach(thread, file.Path);
                a.Name = file.Name;
                list.Add(a);
                if (file.Temp) System.IO.File.Delete(file.Path);
            }
            catch (Exception) { }
        }
        _pendingFiles.Clear();
        PaintAttachments();
        return list.Count > 0 ? list : null;
    }

    /// <summary>El texto que va al modelo por una pregunta con adjuntos: la pregunta y, detras, cada fichero de texto (recortado).</summary>
    private static string TextForModel(StoredMessage message)
    {
        if (message.Attachments is not { Count: > 0 }) return message.Content;
        var sb = new System.Text.StringBuilder(message.Content);
        foreach (var a in message.Attachments)
        {
            if (a.IsImage) continue;
            string text;
            try { text = DocumentIndex.ReadText(a.Path); }
            catch (Exception ex) { text = "[could not read: " + ex.Message + "]"; }
            if (text.Length > 40_000) text = text[..40_000] + "\n[… cut]";
            sb.Append("\n\n[Attached file: ").Append(a.Name).Append("]\n```\n").Append(text).Append("\n```");
        }
        var images = message.Attachments.Count(a => a.IsImage);
        if (images > 0 && !App.Engine.Vision)
            sb.Append("\n\n[The user attached ").Append(images).Append(" image(s), but this AI has no vision: say you cannot see them.]");
        return sb.ToString();
    }

    /// <summary>Las imagenes de una pregunta como data URL (reducidas a 1024 px como mucho) para el modelo con vision.</summary>
    private static List<string>? ImagesForModel(StoredMessage message)
    {
        if (!App.Engine.Vision || message.Attachments is not { Count: > 0 }) return null;
        var list = new List<string>();
        foreach (var a in message.Attachments.Where(a => a.IsImage))
        {
            try
            {
                var bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.DecodePixelWidth = 1024;
                bitmap.UriSource = new Uri(a.Path);
                bitmap.EndInit();
                var encoder = new JpegBitmapEncoder { QualityLevel = 85 };
                encoder.Frames.Add(BitmapFrame.Create(bitmap));
                using var ms = new System.IO.MemoryStream();
                encoder.Save(ms);
                list.Add("data:image/jpeg;base64," + Convert.ToBase64String(ms.ToArray()));
            }
            catch (Exception) { }
        }
        return list.Count > 0 ? list : null;
    }

    /// <summary>
    /// Hay una imagen adjunta y la IA no ve: si su repositorio tiene la parte de vision, se ofrece
    /// descargarla (una vez) y el motor se reinicia con ella; si no, se avisa y se sigue sin imagen.
    /// </summary>
    private async Task EnsureVisionAsync(ChatThread thread, CancellationToken cancel)
    {
        var settings = AppSettings.Current;
        if (EngineHost.VisionAvailable(settings) || settings.ModelPath is null) return;
        var last = thread.Messages.LastOrDefault(m => m.Role == "user");
        if (last?.Attachments is null || !last.Attachments.Any(a => a.IsImage)) return;
        var installed = settings.Installed.FirstOrDefault(i => string.Equals(i.Path, settings.ModelPath, StringComparison.OrdinalIgnoreCase))
                        ?? new InstalledModel { Name = settings.ModelName ?? string.Empty, Path = settings.ModelPath };
        var repo = installed.Repo ?? ModelCatalog.All.FirstOrDefault(m => m.Name == installed.Name)?.Repo;
        if (repo is null || _visionDeclined)
        {
            if (!_visionDeclined) Messages.Children.Add(Bubble("assistant", Loc.Get("NoVisionModel"), null));
            return;
        }
        if (!PromptWindow.Confirm(this, Loc.Get("VisionTitle"), Loc.Format("VisionOffer", installed.Name)))
        {
            _visionDeclined = true;
            return;
        }
        _imageStatus = Loc.Get("VisionDownloading");
        PaintEngineStatus();
        try
        {
            installed.Repo = repo;
            var path = await ModelCatalog.DownloadMmprojAsync(installed, new Progress<Downloader.Progress>(PaintDownload), cancel);
            if (path is null)
                Messages.Children.Add(Bubble("assistant", Loc.Get("NoVisionModel"), null));
            else
                App.Engine.Stop();   // la siguiente arrancada lleva --mmproj
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            Messages.Children.Add(Bubble("assistant", Loc.Format("AnswerFailed", ex.Message), null));
        }
        finally
        {
            _imageStatus = null;
            PaintEngineStatus();
        }
    }

    private bool _visionDeclined;

    private void OnAskMode(object sender, RoutedEventArgs e) => SetMode(agent: false);

    /// <summary>El modo es de la conversacion (o de la que se va a crear): preguntas, o agente con herramientas y permisos.</summary>
    private void SetMode(bool agent)
    {
        PaintMode(agent);
        if (_current is null)
        {
            _pendingWorkMode = agent;
            return;
        }
        _current.WorkMode = agent;
        ThreadStore.Save(_current);
    }

    private void PaintMode(bool? agent = null)
    {
        var a = agent ?? (_current?.WorkMode ?? _pendingWorkMode);
        WorkModeButton.IsChecked = a;
        AskModeButton.IsChecked = !a;
    }

    private bool _pendingWorkMode;

    private async void OnRenameThread(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not ThreadRow row) return;
        var title = PromptWindow.Ask(this, Loc.Get("RenameChat"), Loc.Get("RenamePrompt"), row.Thread.Title);
        if (string.IsNullOrWhiteSpace(title)) return;
        row.Thread.Title = title.Trim();
        ThreadStore.Save(row.Thread);
        RefreshThreadList();
        await Task.CompletedTask;
    }

    private void OnDeleteThread(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not ThreadRow row) return;
        if (!PromptWindow.Confirm(this, Loc.Get("DeleteChat"), Loc.Get("DeleteChatConfirm"))) return;
        if (row.Thread == _current)
            _answering?.Cancel();
        ThreadStore.Delete(row.Thread);
        _threads.Remove(row.Thread);
        var wasCurrent = row.Thread == _current;
        RefreshThreadList();
        if (wasCurrent)
            ShowThread(null);
    }

    /// <summary>Borra todas las conversaciones guardadas (con sus adjuntos), tras confirmarlo; queda una nueva vacia.</summary>
    private void OnDeleteAllThreads(object sender, RoutedEventArgs e)
    {
        var all = ThreadStore.LoadAll();
        if (all.Count == 0) return;
        if (!PromptWindow.Confirm(this, Loc.Get("DeleteAllChats"), Loc.Format("DeleteAllChatsConfirm", all.Count))) return;
        _answering?.Cancel();
        foreach (var thread in all)
            ThreadStore.Delete(thread);
        _threads.Clear();
        DiagLog.Write("chat", $"borradas todas las conversaciones ({all.Count})");
        RefreshThreadList();
        ShowThread(null);
    }

    // ------------------------------------------------------------------ enviar y responder

    private void OnComposerKey(object sender, KeyEventArgs e)
    {
        if ((e.Key == Key.Enter || e.Key == Key.Return) && !Keyboard.Modifiers.HasFlag(ModifierKeys.Shift))
        {
            e.Handled = true;
            OnSend(sender, e);
            return;
        }
        // Ctrl+V con una imagen o ficheros (sin texto) en el portapapeles: el cuadro de texto no lo
        // pegaria; se adjuntan aqui.
        if (e.Key == Key.V && Keyboard.Modifiers == ModifierKeys.Control && PasteAttachments())
            e.Handled = true;
    }

    /// <summary>Ficheros o una imagen (sin texto) en el portapapeles: se adjuntan. Verdadero si habia algo que adjuntar.</summary>
    internal bool PasteAttachments()
    {
        try
        {
            if (Desktop.Current.ClipboardFiles() is { } files)
            {
                foreach (var file in files) AddPendingFile(file);
                return true;
            }
            if (!Desktop.Current.ClipboardHasText() && Desktop.Current.ClipboardImage() is { } image)
            {
                AddPendingImage(image);
                return true;
            }
        }
        catch (Exception) { }
        return false;
    }

    private async void OnSend(object sender, RoutedEventArgs e)
    {
        var text = Composer.Text.Trim();
        if (text.Length == 0 && _pendingFiles.Count == 0)
            return;
        if (text.Length == 0)
            text = Loc.Get("AttachedOnly");
        if (!AppSettings.Current.HasModel)
        {
            OpenSettings();
            return;
        }
        Composer.Clear();
        if (_current is null)
        {
            _current = new ChatThread { Title = ThreadStore.TitleFrom(text), WorkMode = _pendingWorkMode };
#if DEBUG
            _current.AutoApprove = _pendingAutoApprove;
#endif
            _threads.Insert(0, _current);
            RefreshThreadList();
        }
        var thread = _current;
        var message = new StoredMessage { Role = "user", Content = text, Attachments = TakePendingAttachments(_current) };
        thread.Messages.Add(message);
        ThreadStore.Save(thread);
        EmptyState.Visibility = Visibility.Collapsed;
        Messages.Children.Add(Bubble("user", text, null, message.Attachments));
        ScrollToEnd();
        if (_answering is not null)
        {
            // Ya hay una respuesta en marcha: la pregunta queda en la conversacion y se contesta al acabar.
            if (!_pendingAnswers.Contains(thread)) _pendingAnswers.Add(thread);
            return;
        }
        await AnswerAsync(thread);
    }

    private readonly List<ChatThread> _pendingAnswers = [];

    // La traza de la interaccion en curso (solo con MLflow activado) y sus totales.
    private Services.Mlflow.InteractionTrace? _trace;
    private double? _traceTtft;
    private ChatUsage? _traceUsage;
    private int _traceTools;

    private void OnDiagForTrace(string area, string line) => _trace?.Event(area, line);

    /// <summary>Suma lo que gasto una vuelta del modelo a los totales de la interaccion.</summary>
    private void AddToTrace(PendingAnswer live)
    {
        _traceTtft ??= live.FirstPieceDelay?.TotalMilliseconds;
        if (live.Usage is not { } u) return;
        _traceUsage = _traceUsage is null ? u
            : new ChatUsage((_traceUsage.PromptTokens ?? 0) + (u.PromptTokens ?? 0), (_traceUsage.CompletionTokens ?? 0) + (u.CompletionTokens ?? 0), u.TokensPerSecond ?? _traceUsage.TokensPerSecond);
    }

    /// <summary>Responde a la conversacion y, si mientras tanto se enviaron mas preguntas, las va contestando.</summary>
    private async Task AnswerAsync(ChatThread thread)
    {
        while (true)
        {
            _answering = new CancellationTokenSource();
            StopButton.Visibility = Visibility.Visible;
            var cancel = _answering.Token;
            var question = thread.Messages.LastOrDefault(m => m.Role == "user");
            DiagLog.Write("chat", $"pregunta enviada en la conversacion {thread.Id}: {question?.Content.Length ?? 0} caracteres, {question?.Attachments?.Count ?? 0} adjuntos" + (thread.TaskId is not null ? ", tarea programada" : string.Empty));
            DiagLog.SetPhase("preparando la vision");
            var telemetry = Services.Mlflow.MlflowTelemetry.Current;
            _trace = telemetry.Enabled
                ? new Services.Mlflow.InteractionTrace("chat", thread.Id, telemetry.InstallId, App.RunConfig(), telemetry.SendText, question?.Content, question?.Content.Length ?? 0, question?.Attachments?.Count ?? 0)
                : null;
            _traceTtft = null; _traceUsage = null; _traceTools = 0;
            if (_trace is not null) DiagLog.Written += OnDiagForTrace;
            var outcome = "ok";
            string? failure = null;
            try
            {
                await EnsureVisionAsync(thread, cancel);
                // El motor se pide dentro de cada vuelta, ya con la burbuja de espera en pantalla.
                await AnswerLoopAsync(thread, cancel);
            }
            catch (OperationCanceledException) { _pendingAnswers.Clear(); outcome = "cancelled"; }
            catch (ChatTemplateException tpl)
            {
                // El modelo rechazo la conversacion por su plantilla de chat: el detalle (Jinja) va al registro,
                // nunca a la interfaz (General 6.9); al usuario, una frase clara en su idioma.
                outcome = "error";
                failure = DiagLog.Describe(tpl);
                DiagLog.Write("chat", "la plantilla del modelo rechazo la conversacion: " + DiagLog.Scrub(tpl.Message));
                var failed = new StoredMessage { Role = "assistant", Content = Loc.Get("AnswerTemplateRejected") };
                thread.Messages.Add(failed);
                if (thread == _current) Messages.Children.Add(Bubble("assistant", failed.Content, null));
            }
            catch (Exception ex)
            {
                outcome = "error";
                failure = DiagLog.Describe(ex);
                var failed = new StoredMessage { Role = "assistant", Content = Loc.Format("AnswerFailed", ex.Message) };
                thread.Messages.Add(failed);
                if (thread == _current) Messages.Children.Add(Bubble("assistant", failed.Content, null));
            }
            finally
            {
                _answering?.Dispose();
                _answering = null;
                StopButton.Visibility = Visibility.Collapsed;
                ThreadStore.Save(thread);
                DiagLog.SetPhase("inactiva");
                if (_trace is { } trace)
                {
                    // La traza de la interaccion, entera, a la cola de MLflow (se manda en segundo plano).
                    DiagLog.Written -= OnDiagForTrace;
                    var last = thread.Messages.LastOrDefault();
                    var answerText = last?.Role == "assistant" ? last.Content : null;
                    trace.Finish(outcome, failure, answerText, _traceTtft, _traceUsage, answerText?.Length ?? 0, _traceTools);
                    telemetry.Trace(trace);
                    _trace = null;
                }
            }
            if (thread.TaskId is not null) FinishedTask(thread);
            if (_pendingAnswers.Count == 0) break;
            thread = _pendingAnswers[0];
            _pendingAnswers.RemoveAt(0);
            if (thread != _current) ShowThread(thread);
        }
        Composer.Focus();
    }

    // ------------------------------------------------------------------ tareas programadas

    private System.Windows.Threading.DispatcherTimer? _scheduleTimer;

    private void StartScheduler()
    {
        _scheduleTimer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(30) };
        _scheduleTimer.Tick += async (_, _) => await RunDueTasksAsync();
        _scheduleTimer.Start();
    }

    /// <summary>Las tareas que tocan se mandan como conversaciones nuevas, una detras de otra, cuando la IA esta libre.</summary>
    private async Task RunDueTasksAsync()
    {
        if (_answering is not null || !AppSettings.Current.HasModel) return;
        foreach (var task in Scheduler.Due(DateTimeOffset.Now))
        {
            var thread = new ChatThread { Title = "\u23F0 " + task.Title, WorkMode = true, Unattended = true, TaskId = task.Id };
            thread.Messages.Add(new StoredMessage { Role = "user", Content = task.Prompt });
            ThreadStore.Save(thread);
            Scheduler.Ran(task, thread.Id);
            _threads.Insert(0, thread);
            RefreshThreadList();
            ShowThread(thread);
            await AnswerAsync(thread);
        }
    }

    private void FinishedTask(ChatThread thread)
    {
        var last = thread.Messages.LastOrDefault(m => m.Role == "assistant")?.Content ?? string.Empty;
        if (Tray is { Hidden: true })
            Tray.Notify(thread.Title, last.Length > 0 ? last : Loc.Get("TaskDone"));
    }

    /// <summary>
    /// Una vuelta de respuesta; en modo trabajo, tantas como herramientas pida el modelo: cada orden
    /// se confirma, se ejecuta, su salida vuelve al modelo y se sigue hasta que conteste sin pedir mas.
    /// </summary>
    private async Task AnswerLoopAsync(ChatThread thread, CancellationToken cancel)
    {
        var settings = AppSettings.Current;
        // Conecta los servidores MCP encendidos para que sus herramientas estén en la lista (no es fatal si uno falla).
        try { await Mcp.McpManager.EnsureAsync(settings, cancel); } catch (OperationCanceledException) { throw; } catch (Exception) { }
        JsonArray? tools = thread.WorkMode ? ToolBox.Definitions(settings) : ToolBox.QuestionDefinitions(settings);
        var question = thread.Messages.LastOrDefault(m => m.Role == "user")?.Content ?? string.Empty;
        string? context = null;
        // El freno del modo agente: tope de acciones y nada de repetir lo que ya fallo igual (Agent/ToolLoopGuard).
        var guard = new ToolLoopGuard(settings.AgentMaxActions);
        for (var round = 0; ; round++)
        {
            if (!guard.Unlimited && round >= guard.MaxCalls + ToolLoopGuard.MaxRepeats + 2)
            {
                // Vueltas sin fin aunque cada una pida poco: tambien se para y se dice.
                StopTurn(thread, guard, GuardStop.Cap);
                return;
            }
            cancel.ThrowIfCancellationRequested();
            // La burbuja de la respuesta sale ya, con su indicador de espera, antes de nada que tarde.
            var live = new PendingAnswer();
            var bubble = Bubble("assistant", string.Empty, null);
            Messages.Children.Add(bubble);
            BeginWait(bubble, live);
            RepaintBubble(bubble, string.Empty, null, streaming: true);
            var answer = new StoredMessage { Role = "assistant", Content = string.Empty };
            IReadOnlyList<ToolCall>? calls;
            string? finish;
            var clock = System.Diagnostics.Stopwatch.StartNew();
            DiagLog.Write("chat", $"vuelta {round + 1} ({thread.Id}): modo {(thread.WorkMode ? "agente" : "preguntas")}, pensar {(settings.Thinking ? "si" : "no")}, tope {TokenLimits.Answer(settings)} tokens");
            DiagLog.SetPhase("esperando el motor");
            try
            {
                // Generar una imagen mata el motor de texto (no caben los dos en la grafica) y al volver
                // el puerto es otro: se vuelve a pedir la direccion en cada vuelta. Si el motor sigue en
                // pie esto no cuesta nada; si no, lo levanta otra vez (fuera del hilo de la interfaz).
                var engineSpan = _trace?.Engine();
                var engineState = App.Engine.State;
                var baseUrl = await EngineReadyAsync(cancel);
                if (engineSpan is not null)
                {
                    engineSpan.Set("lucia.engine_state_before", engineState.ToString()).Set("lucia.engine_ms", clock.Elapsed.TotalMilliseconds)
                        .Set("lucia.engine_started", engineState != EngineState.Ready || clock.ElapsedMilliseconds > 1500);
                    Services.Mlflow.InteractionTrace.End(engineSpan);
                }
                DiagLog.Write("chat", $"motor listo a los {clock.ElapsedMilliseconds} ms");
                DiagLog.SetPhase("preparando la pregunta (documentos y adjuntos)");
                live.Advance(AnswerPhase.Preparing);
                // Los documentos (PDF y .docx se leen y se trocean) y los adjuntos, tambien fuera.
                if (context is null)
                {
                    var docsSpan = _trace?.Child("documents", "RETRIEVER");
                    context = await Task.Run(() => ToolBox.ContextPrompt(settings, question), cancel);
                    if (docsSpan is not null) _trace!.Documents(docsSpan, context);
                }
                // Los ultimos mensajes, siempre con la ultima pregunta dentro: sin ella, la plantilla de algunos
                // modelos (Qwen3) rechaza la conversacion con un 500. Copia: la conversacion puede crecer mientras.
                var history = ConversationWindow.Select(thread.Messages);
                var system = SystemPrompt(settings, thread.WorkMode, context);
                var messages = await Task.Run(() => MessagesFor(system, history), cancel);
                DiagLog.Write("chat", $"pregunta preparada a los {clock.ElapsedMilliseconds} ms: {messages.Count} mensajes, {messages.Sum(m => m.Content.Length)} caracteres, {messages.Sum(m => m.Images?.Count ?? 0)} imagenes");
                DiagLog.SetPhase("esperando el primer trozo de la IA");
                live.Advance(AnswerPhase.Waiting);
                var llm = _trace?.Llm(round + 1, messages.Count, settings.Thinking);
                try
                {
                    await FollowAsync(bubble, live, ChatClient.PumpAsync(baseUrl, messages, settings.Thinking, TokenLimits.Answer(settings), tools, live, cancel));
                }
                catch (ChatTemplateException tplEx) when (tools is { Count: > 0 })
                {
                    // El modelo rechazo la conversacion por su plantilla: red de seguridad: se reintenta una vez
                    // sin herramientas (por si son lo que la confunde). Si vuelve a fallar, sube y se avisa en claro.
                    DiagLog.Write("chat", "la plantilla rechazo la conversacion; se reintenta sin herramientas: " + DiagLog.Scrub(tplEx.Message));
                    live = new PendingAnswer();
                    BeginWait(bubble, live);
                    RepaintBubble(bubble, string.Empty, null, streaming: true);
                    await FollowAsync(bubble, live, ChatClient.PumpAsync(baseUrl, messages, settings.Thinking, TokenLimits.Answer(settings), null, live, cancel));
                }
                var got = live.Read();
                answer.Content = got.Content.Trim();
                answer.Reasoning = got.Reasoning?.Trim();
                calls = got.Calls;
                finish = got.Finish;
                LogPiece(live, clock);
                AddToTrace(live);
                if (llm is not null) _trace!.EndLlm(llm, live.FirstPieceDelay?.TotalMilliseconds, live.Usage, answer.Content, answer.Reasoning?.Length ?? 0, calls?.Count ?? 0, finish, true, null);
                if (answer.Content.Length == 0 && answer.Reasoning is not null && calls is null)
                {
                    // Se ha gastado el tope razonando y no ha llegado a contestar: otra vuelta sin pensar,
                    // para que responda directo. El razonamiento se conserva plegado.
                    Log($"respuesta vacia tras razonar (fin: {got.Finish}); se repite sin pensamiento");
                    DiagLog.Write("chat", $"solo razonamiento ({answer.Reasoning.Length} caracteres, fin {got.Finish}) a los {clock.ElapsedMilliseconds} ms: se repite sin pensar");
                    DiagLog.SetPhase("esperando el primer trozo de la IA (sin pensar)");
                    live = new PendingAnswer(answer.Reasoning);
                    live.Advance(AnswerPhase.Waiting);
                    BeginWait(bubble, live);
                    RepaintBubble(bubble, string.Empty, answer.Reasoning, streaming: true);
                    var retry = _trace?.Llm(round + 1, messages.Count, false);
                    retry?.Set("lucia.retry_without_thinking", true);
                    await FollowAsync(bubble, live, ChatClient.PumpAsync(baseUrl, messages, false, TokenLimits.Answer(settings), tools, live, cancel));
                    var again = live.Read();
                    answer.Content = again.Content.Trim();
                    calls = again.Calls;
                    finish = again.Finish;
                    LogPiece(live, clock);
                    AddToTrace(live);
                    if (retry is not null) _trace!.EndLlm(retry, live.FirstPieceDelay?.TotalMilliseconds, live.Usage, answer.Content, 0, calls?.Count ?? 0, finish, true, null);
                }
            }
            catch (Exception ex) when (ex is OperationCanceledException || cancel.IsCancellationRequested)
            {
                // Parar: lo que ya se habia escrito se queda; si no habia nada, la burbuja se va.
                DiagLog.Write("chat", $"cancelada a los {clock.ElapsedMilliseconds} ms ({live.Read().Content.Length} caracteres escritos; fase {live.Phase})");
                Report(settings, live, clock, "cancelled", null, question, live.Read().Content, 0);
                EndWait();
                answer.Content = live.Read().Content.Trim();
                if (answer.Content.Length > 0) { RepaintBubble(bubble, answer.Content, null, false); thread.Messages.Add(answer); }
                else Messages.Children.Remove(bubble);
                throw new OperationCanceledException(cancel);
            }
            catch (Exception ex)
            {
                // El motor no arranca o el servidor falla: el aviso lo pone AnswerAsync, sin la burbuja de espera.
                DiagLog.Write("chat", $"error a los {clock.ElapsedMilliseconds} ms (fase {live.Phase}): {DiagLog.Describe(ex)}");
                Report(settings, live, clock, "error", DiagLog.Describe(ex), question, null, 0);
                EndWait();
                Messages.Children.Remove(bubble);
                throw;
            }
            EndWait();
            DiagLog.Write("chat", $"respuesta acabada a los {clock.ElapsedMilliseconds} ms: {answer.Content.Length} caracteres, razonamiento {answer.Reasoning?.Length ?? 0}, herramientas {calls?.Count ?? 0}, fin {finish ?? "-"}");
            Report(settings, live, clock, "ok", null, question, answer.Content, calls?.Count ?? 0, answer.Reasoning?.Length ?? 0);
            if (calls is { Count: > 0 })
            {
                answer.ToolCalls = new JsonArray(calls.Select(c => (JsonNode)c.ToJson()).ToArray()).ToJsonString();
                thread.Messages.Add(answer);
                if (answer.Content.Length > 0) RepaintBubble(bubble, answer.Content, answer.Reasoning, false);
                else Messages.Children.Remove(bubble);
                foreach (var call in calls)
                {
                    var verdict = guard.Stopped == GuardStop.None ? guard.Before(call) : GuardVerdict.Stop;
                    if (verdict == GuardVerdict.Run)
                    {
                        var (output, label) = await RunToolAsync(thread, call, cancel);
                        guard.After(call, label, output);
                        // Sin avanzar: a la segunda se le pide otro enfoque; al parar, que conteste con lo que tiene.
                        var note = guard.Stopped == GuardStop.NoProgress ? ToolLoopGuard.NoProgressAnswer : guard.TakeNudge();
                        if (note is not null && thread.Messages.LastOrDefault() is { Role: "tool" } last && last.ToolCallId == call.Id)
                            last.Content += "\n\n" + note;
                    }
                    else
                    {
                        // Sin ejecutar (ni pedir permiso): el modelo recibe por que, y en el chat se ve.
                        var answerToModel = verdict == GuardVerdict.Repeat ? guard.RepeatAnswer(call) : "Not run: the answer was stopped.";
                        thread.Messages.Add(new StoredMessage { Role = "tool", ToolCallId = call.Id, Command = call.Name, Content = answerToModel });
                        if (verdict == GuardVerdict.Repeat)
                        {
                            Messages.Children.Add(ToolBubble(ToolBox.Find(call.Name)?.Resource ?? Resource.Commands, call.Name, Loc.Get("AgentRepeatBlocked")));
                            DiagLog.Write("agente", $"{call.Name}: repeticion de una accion que ya fallo; no se ejecuta");
                            _trace?.Event("agente", $"{call.Name}: repeated failing action not run");
                        }
                    }
                }
                ThreadStore.Save(thread);
                if (guard.Stopped == GuardStop.NoProgress && tools is not null)
                {
                    // Una ultima vuelta sin herramientas: la IA contesta con lo que tiene y dice que no encontro.
                    DiagLog.Write("agente", $"sin avanzar tras {guard.Fruitless} acciones sin resultado; se pide la respuesta sin herramientas");
                    _trace?.Event("agente", "no progress: final answer without tools");
                    Messages.Children.Add(ToolBubble(Resource.Internet, Loc.Get("AgentNoProgressTitle"), Loc.Get("AgentNoProgress")));
                    tools = null;
                    continue;
                }
                if (guard.Stopped != GuardStop.None)
                {
                    StopTurn(thread, guard, guard.Stopped);
                    return;
                }
                continue;   // otra vuelta con las salidas
            }
            RepaintBubble(bubble, answer.Content, answer.Reasoning, streaming: false);
            if (answer.Content.Length > 0 || answer.Reasoning is not null)
                thread.Messages.Add(answer);
            else
                Messages.Children.Remove(bubble);
            return;
        }
    }

    /// <summary>Las instrucciones del sistema: las del usuario, las del modo y el contexto (memoria y documentos).</summary>
    private static string SystemPrompt(AppSettings settings, bool workMode, string context)
    {
        var system = settings.Instructions.Trim();
        if (workMode)
            system = (system.Length > 0 ? system + "\n\n" : string.Empty) + ToolBox.SystemPrompt(settings);
        else
            system = (system.Length > 0 ? system + "\n\n" : string.Empty) + ToolBox.QuestionPrompt(settings) + (settings.InternetAccess ? " " + ToolBox.InternetPrompt : string.Empty);
        if (context.Length > 0)
            system = (system.Length > 0 ? system + "\n\n" : string.Empty) + context;
        return system;
    }

    /// <summary>
    /// Lo que va al modelo: el sistema y las ultimas vueltas (memoria de conversacion sin pasarse del
    /// contexto; las de herramienta, completas). Lee los adjuntos y reduce las imagenes, asi que se
    /// llama fuera del hilo de la interfaz.
    /// </summary>
    private static List<ChatMessage> MessagesFor(string system, IReadOnlyList<StoredMessage> history)
    {
        var messages = new List<ChatMessage>();
        if (system.Length > 0)
            messages.Add(new ChatMessage("system", system));
        foreach (var m in history)
        {
            if (m.Role == "tool")
                messages.Add(new ChatMessage("tool", m.Content, m.ToolCallId));
            else if (m.ToolCalls is { Length: > 0 } storedCalls)
                messages.Add(new ChatMessage(m.Role, m.Content, null, JsonNode.Parse(storedCalls) as JsonArray));
            else if (m.Role == "user" && m.Attachments is { Count: > 0 })
                messages.Add(new ChatMessage(m.Role, TextForModel(m), null, null, ImagesForModel(m)));
            else
                messages.Add(new ChatMessage(m.Role, m.Content));
        }
        return messages;
    }

    /// <summary>
    /// Sigue una respuesta que se lee en otro hilo: cada poco mira si hay trozos nuevos y repinta la
    /// burbuja. El intervalo crece con lo que cuesta pintar (una respuesta larga en Markdown), para
    /// que la ventana se pueda mover, desplazar y usar mientras escribe.
    /// </summary>
    private async Task FollowAsync(Border bubble, PendingAnswer live, Task pump)
    {
        var shown = -1;
        var interval = PendingAnswer.MinRepaint;
        while (true)
        {
            var finished = await Task.WhenAny(pump, Task.Delay(interval)) == pump;
            if (finished)
                break;
            var now = live.Read();
            if (now.Version == shown || (now.Content.Length == 0 && now.Reasoning is null))
                continue;
            var clock = System.Diagnostics.Stopwatch.StartNew();
            DiagLog.SetPhase($"repintando la respuesta ({now.Content.Length} caracteres)");
            RepaintBubble(bubble, now.Content, now.Reasoning, streaming: true);
            bubble.UpdateLayout();   // medir tambien la maquetacion, que es lo que mas pesa
            interval = PendingAnswer.NextRepaint(clock.Elapsed);
            shown = now.Version;
            if (clock.ElapsedMilliseconds > 250)
                DiagLog.Write("chat", $"repintar la respuesta ({now.Content.Length} caracteres) tardo {clock.ElapsedMilliseconds} ms; el siguiente, en {interval.TotalMilliseconds:0} ms");
            DiagLog.SetPhase(now.Content.Length > 0 ? "escribiendo la respuesta" : "razonando");
        }
        await pump;   // errores y cancelacion
    }

    /// <summary>
    /// Pide permiso segun el recurso (Ajustes › Permisos, o lo aprobado en esta conversacion),
    /// ejecuta la herramienta y deja lo hecho en el hilo, en pantalla y en el registro.
    /// </summary>
    /// <summary>Corta la respuesta del modo agente y dice por que, en el idioma del usuario.</summary>
    private void StopTurn(ChatThread thread, ToolLoopGuard guard, GuardStop reason)
    {
        var text = reason switch
        {
            GuardStop.Repeats => Loc.Format("AgentStoppedRepeats", guard.LastFailedLabel ?? "?", guard.LastError ?? string.Empty),
            GuardStop.Failures => Loc.Format("AgentStoppedFailures", guard.LastFailedLabel ?? "?", guard.LastError ?? string.Empty),
            GuardStop.NoProgress => Loc.Format("AgentStoppedNoProgress", guard.Fruitless, guard.LastFailedLabel ?? "?"),
            _ => Loc.Format("AgentStoppedCap", guard.Calls),
        };
        DiagLog.Write("agente", $"respuesta parada: {reason} tras {guard.Calls} acciones");
        _trace?.Event("agente", $"stopped: {reason} after {guard.Calls} actions");
        var message = new StoredMessage { Role = "assistant", Content = text };
        thread.Messages.Add(message);
        ThreadStore.Save(thread);
        if (thread == _current)
        {
            Messages.Children.Add(Bubble("assistant", text, null));
            ScrollToEnd();
        }
    }

    private async Task<(string Output, string Label)> RunToolAsync(ChatThread thread, ToolCall call, CancellationToken cancel)
    {
        var tool = ToolBox.Find(call.Name);
        JsonObject args;
        try { args = JsonNode.Parse(call.Arguments) as JsonObject ?? new JsonObject(); }
        catch (Exception) { args = new JsonObject(); }
        var detail = tool?.Detail(args) ?? string.Empty;
        var label = tool is null ? call.Name : detail.Length > 0 ? $"{tool.Name}  {detail}" : tool.Name;
        var reason = args["reason"]?.GetValue<string>();
        var toolTrace = _trace?.Tool(call.Name, call.Arguments);
        if (toolTrace is not null) _traceTools++;
        string output;
        if (tool is null)
            output = $"Unknown tool: {call.Name}";
        else if (AppSettings.Current.PermissionFor(tool.Resource) == Permission.Deny)
            output = "This resource is turned off in the app settings.";
        else
        {
            var approved = tool.Resource == Resource.Free || thread.AutoApprove || thread.Approved.Contains(tool.Resource.ToString())
                           || AppSettings.Current.PermissionFor(tool.Resource) == Permission.Allow;
            if (!approved && thread.Unattended)
            {
                output = "Unattended run: this action needs your approval; set the resource to Always in Settings > Permissions to allow it in scheduled tasks.";
                ToolBox.Log(tool, detail, "unattended-declined");
                if (toolTrace is not null) _trace!.EndTool(toolTrace, output, "declined");
                thread.Messages.Add(new StoredMessage { Role = "tool", ToolCallId = call.Id, Command = label, Content = output });
                Messages.Children.Add(ToolBubble(tool.Resource, label, output));
                return (output, label);
            }
            if (!approved)
            {
                var answer = PermissionWindow.Ask(this, tool.Resource, detail, reason);
                approved = answer != PermissionWindow.Answer.Deny;
                if (answer == PermissionWindow.Answer.Conversation) { thread.Approved.Add(tool.Resource.ToString()); ThreadStore.Save(thread); }
                if (answer == PermissionWindow.Answer.Always) { AppSettings.Current.SetPermission(tool.Resource, Permission.Allow); AppSettings.Current.Save(); }
            }
            if (!approved)
            {
                output = "The user declined this action.";
                ToolBox.Log(tool, detail, "declined");
            }
            else
            {
                var running = ToolBubble(tool.Resource, label, Loc.Get("CommandRunning"));
                Messages.Children.Add(running);
                ScrollToEnd();
                try
                {
                    // Leer un PDF, buscar ficheros o sacar el texto de una web no congela la ventana; lo
                    // que tiene que ir en el hilo de la interfaz (el portapapeles) ya vuelve a el solo.
                    DiagLog.SetPhase($"herramienta {tool.Name}");
                    var toolClock = System.Diagnostics.Stopwatch.StartNew();
                    try
                    {
                        output = await Task.Run(() => tool.Run(args, cancel), cancel);
                        DiagLog.Write("herramienta", $"{tool.Name}: bien en {toolClock.ElapsedMilliseconds} ms ({output.Length} caracteres)");
                    }
                    catch (OperationCanceledException) { DiagLog.Write("herramienta", $"{tool.Name}: cancelada a los {toolClock.ElapsedMilliseconds} ms"); throw; }
                    catch (Exception ex) { DiagLog.Write("herramienta", $"{tool.Name}: error a los {toolClock.ElapsedMilliseconds} ms: {DiagLog.Describe(ex)}"); throw; }
                    ToolBox.Log(tool, detail, "ok");
                }
                catch (OperationCanceledException)
                {
                    Messages.Children.Remove(running);
                    ToolBox.Log(tool, detail, "cancelled");
                    if (toolTrace is not null) _trace!.EndTool(toolTrace, null, "cancelled");
                    throw;
                }
                catch (Exception ex) { output = "[error] " + ex.Message; ToolBox.Log(tool, detail, "error: " + ex.Message); }
                Messages.Children.Remove(running);
            }
        }
        if (toolTrace is not null)
            _trace!.EndTool(toolTrace, output, output.StartsWith("[error]", StringComparison.Ordinal) || tool is null ? "error"
                : output.StartsWith("The user declined", StringComparison.Ordinal) || output.StartsWith("This resource is turned off", StringComparison.Ordinal) ? "declined" : "ok");
        thread.Messages.Add(new StoredMessage { Role = "tool", ToolCallId = call.Id, Command = label, Content = output });
        Messages.Children.Add(ToolBubble(tool?.Resource ?? Resource.Commands, label, output));
        ScrollToEnd();
        return (output, label);
    }

    private static Resource ResourceOf(string? label)
    {
        var name = (label ?? string.Empty).Split(' ', 2)[0];
        return ToolBox.Find(name)?.Resource ?? Resource.Commands;
    }

    /// <summary>La orden ejecutada y, plegada, su salida.</summary>
    private Border ToolBubble(Resource resource, string command, string output)
    {
        var fontSize = AppSettings.Current.FontSize;
        var panel = new StackPanel();
        var head = new TextBlock { FontSize = fontSize - 1, Foreground = (Brush)FindResource("TextSecondary"), TextWrapping = TextWrapping.Wrap };
        head.Inlines.Add(new System.Windows.Documents.Run(PermissionWindow.Glyph(resource) + "  ") { FontFamily = new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets") });
        head.Inlines.Add(new System.Windows.Documents.Run(command) { FontFamily = new FontFamily("Cascadia Mono, Consolas") });
        panel.Children.Add(head);
        var body = new TextBox
        {
            Text = output.Trim(),
            IsReadOnly = true,
            BorderThickness = new Thickness(0),
            Background = Brushes.Transparent,
            Foreground = (Brush)FindResource("TextPrimary"),
            FontFamily = new FontFamily("Cascadia Mono, Consolas"),
            FontSize = fontSize - 2,
            TextWrapping = TextWrapping.Wrap,
            MaxHeight = 320,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Margin = new Thickness(0, 4, 0, 0),
        };
        if (!output.StartsWith("Image: ", StringComparison.Ordinal))
            panel.Children.Add(new Expander { Header = Loc.Get("CommandOutput"), Foreground = (Brush)FindResource("TextSecondary"), FontSize = fontSize - 2, Content = body, IsExpanded = output.Length < 600 });
        if (output.StartsWith("Image: ", StringComparison.Ordinal) && System.IO.File.Exists(output[7..].Trim()))
        {
            // Una imagen generada: se ve en la conversacion, con abrir y guardar.
            var path = output[7..].Trim();
            try
            {
                var bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.UriSource = new Uri(path);
                bitmap.EndInit();
                var image = new Image { Source = bitmap, MaxWidth = 512, MaxHeight = 512, Margin = new Thickness(0, 8, 0, 4), HorizontalAlignment = HorizontalAlignment.Left, Cursor = System.Windows.Input.Cursors.Hand };
                image.MouseLeftButtonUp += (_, _) => { try { Desktop.Current.Open(path); } catch (Exception) { } };
                panel.Children.Add(image);
            }
            catch (Exception) { }
            var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 0) };
            var open = new Button { Style = (Style)FindResource("OutlineButton"), Content = Loc.Get("OpenImage"), Margin = new Thickness(0, 0, 8, 0) };
            open.Click += (_, _) => { try { Desktop.Current.Open(path); } catch (Exception) { } };
            var save = new Button { Style = (Style)FindResource("OutlineButton"), Content = Loc.Get("SaveImage") };
            save.Click += (_, _) =>
            {
                if (Desktop.Current.PickSave(this, System.IO.Path.GetFileName(path), "PNG|*.png", ".png") is { } target) { try { System.IO.File.Copy(path, target, overwrite: true); } catch (Exception ex) { PromptWindow.Alert(this, Loc.Get("SaveImage"), ex.Message); } }
            };
            row.Children.Add(open); row.Children.Add(save);
            panel.Children.Add(row);
        }
        else if (output.StartsWith("Saved: ", StringComparison.Ordinal) && System.IO.File.Exists(output[7..].Trim()))
        {
            // Un documento recien creado: abrirlo con su programa.
            var path = output[7..].Trim();
            var open = new Button { Style = (Style)FindResource("OutlineButton"), Content = Loc.Format("OpenDocument", System.IO.Path.GetFileName(path)), HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 6, 0, 0) };
            open.Click += (_, _) => { try { Desktop.Current.Open(path); } catch (Exception) { } };
            panel.Children.Add(open);
        }
        return new Border
        {
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(12, 8, 12, 8),
            Margin = new Thickness(0, 4, 80, 4),
            HorizontalAlignment = HorizontalAlignment.Left,
            Background = (Brush)FindResource("PageBackground"),
            BorderBrush = (Brush)FindResource("Separator"),
            BorderThickness = new Thickness(1),
            Child = panel,
        };
    }

    private void OnStop(object sender, RoutedEventArgs e) => _answering?.Cancel();

#if DEBUG
    public void SendText(string text)
    {
        Composer.Text = text;
        OnSend(this, new RoutedEventArgs());
    }

    public SettingsWindow OpenSettingsForTest() { var w = new SettingsWindow { Owner = this }; w.Show(); return w; }

    public void OpenSettingsFromButtonForTest() => OnSettings(this, new RoutedEventArgs());

    public async void RunToolForTest(string name, string json)
    {
        Log($"prueba de herramienta: {name} {json}");
        _current ??= new ChatThread { Title = "prueba de herramientas", WorkMode = true, AutoApprove = _pendingAutoApprove };
        if (!_threads.Contains(_current)) { _threads.Insert(0, _current); RefreshThreadList(); }
        EmptyState.Visibility = Visibility.Collapsed;
        await RunToolAsync(_current, new ToolCall { Id = "call_test", Name = name, Arguments = json }, CancellationToken.None);
    }

    private bool _pendingAutoApprove;
    public void SetPendingWorkMode(bool auto)
    {
        _pendingWorkMode = true; _pendingAutoApprove = auto; PaintMode(true);
        if (_current is not null) { _current.WorkMode = true; _current.AutoApprove = auto; }
    }
#endif

    // ------------------------------------------------------------------ burbujas

    private Border Bubble(string role, string content, string? reasoning, List<Attachment>? attachments = null)
    {
        var user = role == "user";
        var border = new Border
        {
            CornerRadius = new CornerRadius(12),
            Padding = new Thickness(14, 10, 14, 10),
            Margin = new Thickness(user ? 80 : 0, 4, user ? 0 : 80, 4),
            HorizontalAlignment = user ? HorizontalAlignment.Right : HorizontalAlignment.Left,
            Background = user ? (Brush)FindResource("Primary") : (Brush)FindResource("CardBackground"),
        };
        RepaintBubble(border, content, reasoning, streaming: false, user);
        if (attachments is { Count: > 0 } && border.Child is StackPanel panel)
        {
            var wrap = new WrapPanel { Margin = new Thickness(0, 0, 0, 6) };
            foreach (var a in attachments)
                wrap.Children.Add(AttachmentChip(a.Name, a.Path, a.IsImage, removable: false));
            panel.Children.Insert(0, wrap);
        }
        return border;
    }

    private void RepaintBubble(Border bubble, string content, string? reasoning, bool streaming, bool? isUser = null)
    {
        var user = isUser ?? bubble.HorizontalAlignment == HorizontalAlignment.Right;
        bubble.Tag = content;
        var fontSize = AppSettings.Current.FontSize;
        var foreground = user ? (Brush)FindResource("OnPrimary") : (Brush)FindResource("TextPrimary");
        var panel = new StackPanel();
        if (!user && reasoning is { Length: > 0 })
        {
            var expander = new Expander
            {
                Header = Loc.Get("Thinking"),
                Foreground = (Brush)FindResource("TextSecondary"),
                FontSize = fontSize - 2,
                IsExpanded = false,
                Margin = new Thickness(0, 0, 0, 6),
                Content = new TextBox { Text = reasoning, TextWrapping = TextWrapping.Wrap, FontSize = fontSize - 2, Foreground = (Brush)FindResource("TextSecondary"), Margin = new Thickness(0, 4, 0, 0), IsReadOnly = true, BorderThickness = new Thickness(0), Background = Brushes.Transparent, Padding = new Thickness(0) },
            };
            panel.Children.Add(expander);
        }
        if (user)
        {
            panel.Children.Add(new TextBox { Text = content, TextWrapping = TextWrapping.Wrap, FontSize = fontSize, Foreground = foreground, IsReadOnly = true, BorderThickness = new Thickness(0), Background = Brushes.Transparent, Padding = new Thickness(0), SelectionBrush = (Brush)FindResource("OnPrimary"), SelectionOpacity = 0.35 });
            // Lo que preguntaste se puede copiar, retocar en el redactor o volver a enviar tal cual.
            var actions = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 4, -6, -4) };
            actions.Children.Add(UserAction("\uE8C8", Loc.Get("Copy"), () => { try { Desktop.Current.SetClipboardText(content); } catch (Exception) { } }));
            actions.Children.Add(UserAction("\uE70F", Loc.Get("EditQuestion"), () => { Composer.Text = content; Composer.CaretIndex = content.Length; Composer.Focus(); }));
            actions.Children.Add(UserAction("\uE72C", Loc.Get("ResendQuestion"), () => { if (_answering is null) { Composer.Text = content; OnSend(this, new RoutedEventArgs()); } }));
            panel.Children.Add(actions);
        }
        else if (content.Length == 0 && streaming)
            panel.Children.Add(WaitIndicatorFor(bubble));
        else
            panel.Children.Add(Markdown.Render(content, fontSize, foreground, (Brush)FindResource("PageBackground"), (Brush)FindResource("Accent"), Loc.Get("Copy"), Loc.Get("SaveCode")));
        if (!user && !streaming && content.Length > 0)
        {
            var copy = new Button { Content = "", ToolTip = Loc.Get("Copy"), Style = (Style)FindResource("GhostIconButton"), Width = 28, Height = 28, FontSize = 13, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(-6, 4, 0, -4) };
            copy.Click += (_, _) => { try { Desktop.Current.SetClipboardText(content); } catch (Exception) { } };
            panel.Children.Add(copy);
        }
        bubble.Child = panel;
        if (streaming)
            ScrollToEnd();
    }

    // ------------------------------------------------------------------ indicador de espera

    /// <summary>La respuesta que se espera: su burbuja, lo que lleva, desde cuando y el indicador que se le enseña.</summary>
    private sealed class Waiting(Border bubble, PendingAnswer live, FrameworkElement indicator, TextBlock text)
    {
        public Border Bubble { get; } = bubble;
        public PendingAnswer Live { get; set; } = live;
        public FrameworkElement Indicator { get; } = indicator;
        public TextBlock Text { get; } = text;
        public System.Diagnostics.Stopwatch Clock { get; } = System.Diagnostics.Stopwatch.StartNew();
    }

    private Waiting? _waiting;
    private System.Windows.Threading.DispatcherTimer? _waitTimer;

    /// <summary>
    /// Empieza (o sigue, si es la misma burbuja) la espera de una respuesta: el indicador gira y su
    /// texto dice por donde va (cargando la IA, pensando, razonando…) con los segundos que lleva. Que
    /// gire es tambien la prueba de que el hilo de la interfaz esta libre.
    /// </summary>
    private void BeginWait(Border bubble, PendingAnswer live)
    {
        if (_waiting?.Bubble == bubble)
            _waiting.Live = live;
        else
        {
            var indicator = WaitIndicator(out var text);
            _waiting = new Waiting(bubble, live, indicator, text);
        }
        PaintWait();
        if (_waitTimer is null)
        {
            _waitTimer = new System.Windows.Threading.DispatcherTimer(System.Windows.Threading.DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(250) };
            _waitTimer.Tick += (_, _) => PaintWait();
        }
        _waitTimer.Start();
    }

    private void EndWait()
    {
        _waitTimer?.Stop();
        _waiting = null;
    }

    private void PaintWait()
    {
        if (_waiting is not { } w) return;
        var text = PendingAnswer.Status(w.Live.Phase, App.Engine.State, w.Clock.Elapsed);
        if (w.Text.Text != text) w.Text.Text = text;
    }

    /// <summary>El indicador de la burbuja que se espera (el mismo cada vez que se repinta, para que el giro no salte).</summary>
    private FrameworkElement WaitIndicatorFor(Border bubble)
    {
        if (_waiting?.Bubble != bubble)
            return WaitIndicator(out _);
        (_waiting.Indicator.Parent as Panel)?.Children.Remove(_waiting.Indicator);
        return _waiting.Indicator;
    }

    /// <summary>Un arco que gira sobre su pista (colores del tema, claro y oscuro) y el texto de lo que esta haciendo.</summary>
    private static FrameworkElement WaitIndicator(out TextBlock text)
    {
        var fontSize = AppSettings.Current.FontSize;
        var spinner = Controls.BusyIndicator.Spinner(Math.Round(fontSize + 4));   // el mismo arco en toda la aplicacion
        text = new TextBlock { FontSize = fontSize - 1, VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap, Text = Loc.Get("AnswerPreparing") };
        text.SetResourceReference(TextBlock.ForegroundProperty, "TextSecondary");
        var row = new StackPanel { Orientation = Orientation.Horizontal, Name = "WaitIndicator", Margin = new Thickness(0, 2, 0, 2) };
        System.Windows.Automation.AutomationProperties.SetAutomationId(row, "WaitIndicator");
        row.Children.Add(spinner);
        row.Children.Add(text);
        return row;
    }

    private Button UserAction(string glyph, string tooltip, Action action)
    {
        var button = new Button { Content = glyph, ToolTip = tooltip, Style = (Style)FindResource("GhostIconButton"), Width = 28, Height = 28, FontSize = 13, Foreground = (Brush)FindResource("OnPrimary"), Opacity = 0.85 };
        button.Click += (_, _) => action();
        return button;
    }

    private static void Log(string line) => EngineHost.Log(line);

    /// <summary>
    /// Una vuelta acabada, a MLflow si esta activado (en segundo plano: aqui solo se encola). El texto
    /// de la pregunta y de la respuesta solo va si el usuario lo permitio en Ajustes.
    /// </summary>
    private static void Report(AppSettings settings, PendingAnswer live, System.Diagnostics.Stopwatch clock, string outcome, string? error, string question, string? answerText, int toolCalls, int reasoningChars = 0)
    {
        var telemetry = Services.Mlflow.MlflowTelemetry.Current;
        if (!telemetry.Enabled) return;
        var usage = live.Usage;
        var ttft = live.FirstPieceDelay?.TotalMilliseconds;
        var total = clock.Elapsed.TotalMilliseconds;
        var speed = usage?.TokensPerSecond ?? (usage?.CompletionTokens is { } n && ttft is { } t && total > t ? n / ((total - t) / 1000) : null);
        var text = settings.Mlflow.SendText;
        telemetry.Request(App.RunConfig(), new Services.Mlflow.RequestRecord("chat", ttft, total, usage?.PromptTokens, usage?.CompletionTokens, speed,
            answerText?.Length ?? 0, reasoningChars, toolCalls, outcome, error, text ? question : null, text ? answerText : null));
    }

    /// <summary>Cuando llego el primer trozo de la IA (lo que mas tarda con una pregunta larga: leerla entera).</summary>
    private static void LogPiece(PendingAnswer live, System.Diagnostics.Stopwatch clock)
    {
        var first = live.FirstPieceDelay;
        DiagLog.Write("chat", first is { } d
            ? $"primer trozo {d.TotalMilliseconds:0} ms despues de pedirlo; fin del streaming a los {clock.ElapsedMilliseconds} ms"
            : $"el streaming ha acabado sin texto a los {clock.ElapsedMilliseconds} ms");
    }

    private void ScrollToEnd() => Dispatcher.BeginInvoke(() => MessagesScroll.ScrollToEnd(), System.Windows.Threading.DispatcherPriority.Background);

    // ------------------------------------------------------------------ ventanas

    private void OnSettings(object sender, RoutedEventArgs e) => OpenSettings();

    private void OpenSettings()
    {
        new SettingsWindow { Owner = this }.ShowDialog();
        PaintEngineStatus();
    }

    private void OnAbout(object sender, RoutedEventArgs e) => new AboutWindow { Owner = this }.ShowDialog();
}
