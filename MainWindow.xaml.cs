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
        InitializeComponent();
        SourceInitialized += (_, _) =>
        {
            ThemeManager.ApplyToWindow(this);
            Tray = new TrayIcon(this, Loc.Get, () => Application.Current.Shutdown()) { MinimizeToTray = AppSettings.Current.TrayOnMinimize };
        };
        EmptyLogo.Source = new BitmapImage(new Uri("pack://application:,,,/Assets/logo.png"));
        _threads = ThreadStore.LoadAll();
        ApplyTexts();
        Loc.LanguageChanged += ApplyTexts;
        App.Engine.StateChanged += () => Dispatcher.BeginInvoke(PaintEngineStatus);
        // La IA de imagenes: su estado (descargando, generando) y el progreso van a la misma barra.
        ImageEngine.Status += s => Dispatcher.BeginInvoke(() => { _imageStatus = s; PaintEngineStatus(); });
        ToolBox.ImageProgress = new Progress<Downloader.Progress>(PaintDownload);
        // Pegar una imagen o ficheros en el redactor los adjunta.
        DataObject.AddPastingHandler(Composer, OnComposerPaste);
        ModelDownloads.Changed += PaintEngineStatus;
        ModelDownloads.Finished += (_, error) => { if (error is null) ModelChanged(); else PaintEngineStatus(); };
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

    private void ApplyTexts()
    {
        Title = Loc.Get("AppTitle");
        ConversationsTitle.Text = Loc.Get("Conversations");
        NewChatButton.ToolTip = Loc.Get("NewChat");
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
        PaintAttachments();
        PaintEngineStatus();
        if (_current is not null)
            ShowThread(_current);
    }

    // ------------------------------------------------------------------ motor

    private async Task WarmUpAsync()
    {
        try { await App.Engine.EnsureReadyAsync(new Progress<Downloader.Progress>(PaintDownload), CancellationToken.None); }
        catch (Exception) { /* el estado ya lo enseña la barra */ }
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
        if (_imageStatus is { Length: > 0 })
        {
            EngineStatus.Text = _imageStatus;
            return;
        }
        if (ModelDownloads.Current is { } download)
        {
            // Una IA bajando en segundo plano: se ve aqui aunque Ajustes este cerrado.
            EngineStatus.Text = Loc.Format("ModelInstalling", download.Model.Name, download.Total is { } t ? $"{Human(download.Received)} / {Human(t)}" : Human(download.Received));
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

    private void OnNewChat(object sender, RoutedEventArgs e)
    {
        _answering?.Cancel();
        ThreadList.SelectedItem = null;
        ShowThread(null);
        Composer.Focus();
    }

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

    // ------------------------------------------------------------------ adjuntos

    /// <summary>Lo que se va a adjuntar a la siguiente pregunta: ficheros elegidos, arrastrados o pegados (las imagenes pegadas van a un PNG temporal).</summary>
    private readonly List<(string Name, string Path, bool IsImage, bool Temp)> _pendingFiles = [];

    private static readonly HashSet<string> TextExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".txt", ".md", ".markdown", ".csv", ".tsv", ".json", ".xml", ".yaml", ".yml", ".toml", ".ini", ".cfg", ".conf", ".log",
        ".cs", ".xaml", ".js", ".ts", ".jsx", ".tsx", ".py", ".java", ".kt", ".c", ".h", ".cpp", ".hpp", ".rs", ".go", ".rb", ".php", ".swift",
        ".html", ".htm", ".css", ".scss", ".sql", ".ps1", ".psm1", ".bat", ".cmd", ".sh", ".gradle", ".csproj", ".sln", ".props", ".targets", ".tex", ".rtf", ".docx",
    };

    private void OnAttach(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog { Multiselect = true, Title = Loc.Get("Attach"), Filter = Loc.Get("AttachFilter") };
        if (dialog.ShowDialog(this) != true) return;
        foreach (var file in dialog.FileNames) AddPendingFile(file);
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
            else if (!e.DataObject.GetDataPresent(DataFormats.UnicodeText) && Clipboard.ContainsImage() && Clipboard.GetImage() is { } image)
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
            action.Click += (_, _) => { try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(path) { UseShellExecute = true }); } catch (Exception) { } };
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
        if (e.Key == Key.V && Keyboard.Modifiers == ModifierKeys.Control)
        {
            try
            {
                if (Clipboard.ContainsFileDropList())
                {
                    foreach (var file in Clipboard.GetFileDropList()) if (file is not null) AddPendingFile(file);
                    e.Handled = true;
                }
                else if (!Clipboard.ContainsText() && Clipboard.ContainsImage() && Clipboard.GetImage() is { } image)
                {
                    AddPendingImage(image);
                    e.Handled = true;
                }
            }
            catch (Exception) { }
        }
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

    /// <summary>Responde a la conversacion y, si mientras tanto se enviaron mas preguntas, las va contestando.</summary>
    private async Task AnswerAsync(ChatThread thread)
    {
        while (true)
        {
            _answering = new CancellationTokenSource();
            StopButton.Visibility = Visibility.Visible;
            var cancel = _answering.Token;
            try
            {
                await EnsureVisionAsync(thread, cancel);
                var baseUrl = await App.Engine.EnsureReadyAsync(new Progress<Downloader.Progress>(PaintDownload), cancel);
                await AnswerLoopAsync(thread, baseUrl, cancel);
            }
            catch (OperationCanceledException) { _pendingAnswers.Clear(); }
            catch (Exception ex)
            {
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
    private async Task AnswerLoopAsync(ChatThread thread, string baseUrl, CancellationToken cancel)
    {
        var settings = AppSettings.Current;
        var tools = thread.WorkMode ? ToolBox.Definitions(settings) : ToolBox.QuestionDefinitions(settings);
        var question = thread.Messages.LastOrDefault(m => m.Role == "user")?.Content ?? string.Empty;
        var context = ToolBox.ContextPrompt(settings, question);
        for (var round = 0; round < 12; round++)
        {
            cancel.ThrowIfCancellationRequested();
            var messages = new List<ChatMessage>();
            var system = settings.Instructions.Trim();
            if (thread.WorkMode)
                system = (system.Length > 0 ? system + "\n\n" : string.Empty) + ToolBox.SystemPrompt(settings);
            else
                system = (system.Length > 0 ? system + "\n\n" : string.Empty) + ToolBox.QuestionPrompt(settings) + (settings.InternetAccess ? " " + ToolBox.InternetPrompt : string.Empty);
            if (context.Length > 0)
                system = (system.Length > 0 ? system + "\n\n" : string.Empty) + context;
            if (system.Length > 0)
                messages.Add(new ChatMessage("system", system));
            // Las ultimas vueltas: memoria de conversacion sin pasarse del contexto. Las de herramienta van completas.
            foreach (var m in thread.Messages.TakeLast(30))
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

            var answer = new StoredMessage { Role = "assistant", Content = string.Empty };
            var bubble = Bubble("assistant", string.Empty, null);
            Messages.Children.Add(bubble);
            ScrollToEnd();
            var content = new System.Text.StringBuilder();
            var reasoning = new System.Text.StringBuilder();
            IReadOnlyList<ToolCall>? calls = null;
            string? finish = null;
            var lastPaint = DateTime.UtcNow;
            try
            {
                await foreach (var delta in ChatClient.StreamAsync(baseUrl, messages, settings.Thinking, settings.MaxAnswerTokens, tools, cancel))
                {
                    if (delta.Content is not null) content.Append(delta.Content);
                    if (delta.Reasoning is not null) reasoning.Append(delta.Reasoning);
                    if (delta.ToolCalls is not null) calls = delta.ToolCalls;
                    if (delta.FinishReason is not null) finish = delta.FinishReason;
                    if ((DateTime.UtcNow - lastPaint).TotalMilliseconds > 120)
                    {
                        RepaintBubble(bubble, content.ToString(), reasoning.Length > 0 ? reasoning.ToString() : null, streaming: true);
                        lastPaint = DateTime.UtcNow;
                    }
                }
            }
            catch (OperationCanceledException)
            {
                answer.Content = content.ToString().Trim();
                if (answer.Content.Length > 0) { RepaintBubble(bubble, answer.Content, null, false); thread.Messages.Add(answer); }
                else Messages.Children.Remove(bubble);
                throw;
            }
            answer.Content = content.ToString().Trim();
            answer.Reasoning = reasoning.Length > 0 ? reasoning.ToString().Trim() : null;
            if (answer.Content.Length == 0 && answer.Reasoning is not null && calls is null)
            {
                // Se ha gastado el tope razonando y no ha llegado a contestar: otra vuelta sin pensar,
                // para que responda directo. El razonamiento se conserva plegado.
                Log($"respuesta vacia tras razonar (fin: {finish}); se repite sin pensamiento");
                RepaintBubble(bubble, string.Empty, answer.Reasoning, streaming: true);
                await foreach (var delta in ChatClient.StreamAsync(baseUrl, messages, false, settings.MaxAnswerTokens, tools, cancel))
                {
                    if (delta.Content is not null) content.Append(delta.Content);
                    if (delta.ToolCalls is not null) calls = delta.ToolCalls;
                    if ((DateTime.UtcNow - lastPaint).TotalMilliseconds > 120)
                    {
                        RepaintBubble(bubble, content.ToString(), answer.Reasoning, streaming: true);
                        lastPaint = DateTime.UtcNow;
                    }
                }
                answer.Content = content.ToString().Trim();
            }
            if (calls is { Count: > 0 })
            {
                answer.ToolCalls = new JsonArray(calls.Select(c => (JsonNode)c.ToJson()).ToArray()).ToJsonString();
                thread.Messages.Add(answer);
                if (answer.Content.Length > 0) RepaintBubble(bubble, answer.Content, answer.Reasoning, false);
                else Messages.Children.Remove(bubble);
                foreach (var call in calls)
                    await RunToolAsync(thread, call, cancel);
                ThreadStore.Save(thread);
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

    /// <summary>
    /// Pide permiso segun el recurso (Ajustes › Permisos, o lo aprobado en esta conversacion),
    /// ejecuta la herramienta y deja lo hecho en el hilo, en pantalla y en el registro.
    /// </summary>
    private async Task RunToolAsync(ChatThread thread, ToolCall call, CancellationToken cancel)
    {
        var tool = ToolBox.Find(call.Name);
        JsonObject args;
        try { args = JsonNode.Parse(call.Arguments) as JsonObject ?? new JsonObject(); }
        catch (Exception) { args = new JsonObject(); }
        var detail = tool?.Detail(args) ?? string.Empty;
        var label = tool is null ? call.Name : detail.Length > 0 ? $"{tool.Name}  {detail}" : tool.Name;
        var reason = args["reason"]?.GetValue<string>();
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
                thread.Messages.Add(new StoredMessage { Role = "tool", ToolCallId = call.Id, Command = label, Content = output });
                Messages.Children.Add(ToolBubble(tool.Resource, label, output));
                return;
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
                    output = await tool.Run(args, cancel);
                    ToolBox.Log(tool, detail, "ok");
                }
                catch (OperationCanceledException) { Messages.Children.Remove(running); ToolBox.Log(tool, detail, "cancelled"); throw; }
                catch (Exception ex) { output = "[error] " + ex.Message; ToolBox.Log(tool, detail, "error: " + ex.Message); }
                Messages.Children.Remove(running);
            }
        }
        thread.Messages.Add(new StoredMessage { Role = "tool", ToolCallId = call.Id, Command = label, Content = output });
        Messages.Children.Add(ToolBubble(tool?.Resource ?? Resource.Commands, label, output));
        ScrollToEnd();
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
                image.MouseLeftButtonUp += (_, _) => { try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(path) { UseShellExecute = true }); } catch (Exception) { } };
                panel.Children.Add(image);
            }
            catch (Exception) { }
            var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 0) };
            var open = new Button { Style = (Style)FindResource("OutlineButton"), Content = Loc.Get("OpenImage"), Margin = new Thickness(0, 0, 8, 0) };
            open.Click += (_, _) => { try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(path) { UseShellExecute = true }); } catch (Exception) { } };
            var save = new Button { Style = (Style)FindResource("OutlineButton"), Content = Loc.Get("SaveImage") };
            save.Click += (_, _) =>
            {
                var dialog = new Microsoft.Win32.SaveFileDialog { FileName = System.IO.Path.GetFileName(path), Filter = "PNG|*.png", DefaultExt = ".png" };
                if (dialog.ShowDialog(this) == true) { try { System.IO.File.Copy(path, dialog.FileName, overwrite: true); } catch (Exception ex) { PromptWindow.Alert(this, Loc.Get("SaveImage"), ex.Message); } }
            };
            row.Children.Add(open); row.Children.Add(save);
            panel.Children.Add(row);
        }
        else if (output.StartsWith("Saved: ", StringComparison.Ordinal) && System.IO.File.Exists(output[7..].Trim()))
        {
            // Un documento recien creado: abrirlo con su programa.
            var path = output[7..].Trim();
            var open = new Button { Style = (Style)FindResource("OutlineButton"), Content = Loc.Format("OpenDocument", System.IO.Path.GetFileName(path)), HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 6, 0, 0) };
            open.Click += (_, _) => { try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(path) { UseShellExecute = true }); } catch (Exception) { } };
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
            actions.Children.Add(UserAction("\uE8C8", Loc.Get("Copy"), () => { try { Clipboard.SetText(content); } catch (Exception) { } }));
            actions.Children.Add(UserAction("\uE70F", Loc.Get("EditQuestion"), () => { Composer.Text = content; Composer.CaretIndex = content.Length; Composer.Focus(); }));
            actions.Children.Add(UserAction("\uE72C", Loc.Get("ResendQuestion"), () => { if (_answering is null) { Composer.Text = content; OnSend(this, new RoutedEventArgs()); } }));
            panel.Children.Add(actions);
        }
        else if (content.Length == 0 && streaming)
            panel.Children.Add(new TextBlock { Text = "…", FontSize = fontSize, Foreground = foreground });
        else
            panel.Children.Add(Markdown.Render(content, fontSize, foreground, (Brush)FindResource("PageBackground"), (Brush)FindResource("Accent"), Loc.Get("Copy"), Loc.Get("SaveCode")));
        if (!user && !streaming && content.Length > 0)
        {
            var copy = new Button { Content = "", ToolTip = Loc.Get("Copy"), Style = (Style)FindResource("GhostIconButton"), Width = 28, Height = 28, FontSize = 13, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(-6, 4, 0, -4) };
            copy.Click += (_, _) => { try { Clipboard.SetText(content); } catch (Exception) { } };
            panel.Children.Add(copy);
        }
        bubble.Child = panel;
        if (streaming)
            ScrollToEnd();
    }

    private Button UserAction(string glyph, string tooltip, Action action)
    {
        var button = new Button { Content = glyph, ToolTip = tooltip, Style = (Style)FindResource("GhostIconButton"), Width = 28, Height = 28, FontSize = 13, Foreground = (Brush)FindResource("OnPrimary"), Opacity = 0.85 };
        button.Click += (_, _) => action();
        return button;
    }

    private static void Log(string line) => EngineHost.Log(line);

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
