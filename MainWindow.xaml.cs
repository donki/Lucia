using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using SocAiChat.Chat;
using SocAiChat.Engine;
using SocAiChat.Localization;
using SocAiChat.Services;

namespace SocAiChat;

public partial class MainWindow : Window
{
    private readonly List<ChatThread> _threads;
    private ChatThread? _current;
    private CancellationTokenSource? _answering;

    public MainWindow()
    {
        InitializeComponent();
        SourceInitialized += (_, _) => ThemeManager.ApplyToWindow(this);
        EmptyLogo.Source = new BitmapImage(new Uri("pack://application:,,,/Assets/logo.png"));
        _threads = ThreadStore.LoadAll();
        ApplyTexts();
        Loc.LanguageChanged += ApplyTexts;
        App.Engine.StateChanged += () => Dispatcher.BeginInvoke(PaintEngineStatus);
        RefreshThreadList();
        if (_threads.Count > 0)
            ThreadList.SelectedIndex = 0;
        else
            ShowThread(null);
        Loaded += (_, _) =>
        {
            Composer.Focus();
            if (!AppSettings.Current.HasModel)
                OpenSettings();
            else
                _ = WarmUpAsync();
        };
        Closing += (_, _) => _answering?.Cancel();
    }

    private void ApplyTexts()
    {
        Title = Loc.Get("AppTitle");
        ConversationsTitle.Text = Loc.Get("Conversations");
        NewChatButton.ToolTip = Loc.Get("NewChat");
        SettingsButton.ToolTip = Loc.Get("Settings");
        AboutButton.ToolTip = Loc.Get("AboutTooltip");
        RenameMenu.Header = Loc.Get("RenameChat");
        DeleteMenu.Header = Loc.Get("DeleteChat");
        SendButton.ToolTip = Loc.Get("Send");
        StopButton.ToolTip = Loc.Get("Stop");
        EmptyTitle.Text = Loc.Get("EmptyTitle");
        EmptyHint.Text = Loc.Get("EmptyHint");
        Composer.ToolTip = Loc.Get("ComposerHint");
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

    private void PaintEngineStatus()
    {
        var engine = App.Engine;
        var settings = AppSettings.Current;
        var model = settings.ModelName is { Length: > 0 } name ? name : null;
        EngineStatus.Text = engine.State switch
        {
            EngineState.NoModel => Loc.Get("EngineNoModel"),
            EngineState.DownloadingEngine => Loc.Get("EngineDownloading"),
            EngineState.Starting => Loc.Get("EngineStarting"),
            EngineState.Ready => $"{Loc.Get("EngineReady")} · {model}",
            EngineState.Stopped => Loc.Get("EngineStopped"),
            _ => $"{Loc.Get("EngineError")}: {engine.Detail}",
        };
        EngineProgress.Visibility = engine.State is EngineState.DownloadingEngine or EngineState.Starting ? Visibility.Visible : Visibility.Collapsed;
        EngineProgress.IsIndeterminate = engine.State == EngineState.Starting;
    }

    /// <summary>Tras cambiar de modelo en Ajustes: el motor se reinicia con el nuevo en la siguiente pregunta, y de paso se calienta.</summary>
    public void ModelChanged()
    {
        PaintEngineStatus();
        _ = WarmUpAsync();
    }

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
        public override string ToString() => Thread.Title.Length > 0 ? Thread.Title : "…";
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
        Messages.Children.Clear();
        EmptyState.Visibility = thread is null || thread.Messages.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        if (thread is null)
            return;
        foreach (var message in thread.Messages)
            Messages.Children.Add(Bubble(message.Role, message.Content, message.Reasoning));
        ScrollToEnd();
    }

    private async void OnRenameThread(object sender, RoutedEventArgs e)
    {
        if (ThreadList.SelectedItem is not ThreadRow row) return;
        var title = PromptWindow.Ask(this, Loc.Get("RenameChat"), Loc.Get("RenamePrompt"), row.Thread.Title);
        if (string.IsNullOrWhiteSpace(title)) return;
        row.Thread.Title = title.Trim();
        ThreadStore.Save(row.Thread);
        RefreshThreadList();
        await Task.CompletedTask;
    }

    private void OnDeleteThread(object sender, RoutedEventArgs e)
    {
        if (ThreadList.SelectedItem is not ThreadRow row) return;
        if (!PromptWindow.Confirm(this, Loc.Get("DeleteChat"), Loc.Get("DeleteChatConfirm"))) return;
        if (row.Thread == _current)
            _answering?.Cancel();
        ThreadStore.Delete(row.Thread);
        _threads.Remove(row.Thread);
        RefreshThreadList();
        ShowThread(null);
    }

    // ------------------------------------------------------------------ enviar y responder

    private void OnComposerKey(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && !Keyboard.Modifiers.HasFlag(ModifierKeys.Shift))
        {
            e.Handled = true;
            OnSend(sender, e);
        }
    }

    private async void OnSend(object sender, RoutedEventArgs e)
    {
        var text = Composer.Text.Trim();
        if (text.Length == 0 || _answering is not null)
            return;
        if (!AppSettings.Current.HasModel)
        {
            OpenSettings();
            return;
        }
        Composer.Clear();
        if (_current is null)
        {
            _current = new ChatThread { Title = ThreadStore.TitleFrom(text) };
            _threads.Insert(0, _current);
            RefreshThreadList();
        }
        var thread = _current;
        thread.Messages.Add(new StoredMessage { Role = "user", Content = text });
        ThreadStore.Save(thread);
        EmptyState.Visibility = Visibility.Collapsed;
        Messages.Children.Add(Bubble("user", text, null));
        var answer = new StoredMessage { Role = "assistant", Content = string.Empty };
        var bubble = Bubble("assistant", string.Empty, null);
        Messages.Children.Add(bubble);
        ScrollToEnd();

        _answering = new CancellationTokenSource();
        SendButton.Visibility = Visibility.Collapsed;
        StopButton.Visibility = Visibility.Visible;
        var cancel = _answering.Token;
        var settings = AppSettings.Current;
        try
        {
            var baseUrl = await App.Engine.EnsureReadyAsync(new Progress<Downloader.Progress>(PaintDownload), cancel);
            var messages = new List<ChatMessage>();
            if (settings.Instructions.Trim().Length > 0)
                messages.Add(new ChatMessage("system", settings.Instructions.Trim()));
            // Las ultimas 20 vueltas: suficiente memoria de conversacion sin pasarse del contexto.
            foreach (var m in thread.Messages.TakeLast(20))
                messages.Add(new ChatMessage(m.Role, m.Content));
            var content = new System.Text.StringBuilder();
            var reasoning = new System.Text.StringBuilder();
            var lastPaint = DateTime.UtcNow;
            await foreach (var delta in ChatClient.StreamAsync(baseUrl, messages, settings.Thinking, settings.MaxAnswerTokens, cancel))
            {
                if (delta.Content is not null) content.Append(delta.Content);
                if (delta.Reasoning is not null) reasoning.Append(delta.Reasoning);
                if ((DateTime.UtcNow - lastPaint).TotalMilliseconds > 120)
                {
                    RepaintBubble(bubble, content.ToString(), reasoning.Length > 0 ? reasoning.ToString() : null, streaming: true);
                    lastPaint = DateTime.UtcNow;
                }
            }
            answer.Content = content.ToString().Trim();
            answer.Reasoning = reasoning.Length > 0 ? reasoning.ToString().Trim() : null;
        }
        catch (OperationCanceledException)
        {
            answer.Content = (bubble.Tag as string ?? string.Empty).Trim();
        }
        catch (Exception ex)
        {
            answer.Content = Loc.Format("AnswerFailed", ex.Message);
        }
        finally
        {
            _answering?.Dispose();
            _answering = null;
            SendButton.Visibility = Visibility.Visible;
            StopButton.Visibility = Visibility.Collapsed;
        }
        RepaintBubble(bubble, answer.Content, answer.Reasoning, streaming: false);
        if (answer.Content.Length > 0 || answer.Reasoning is not null)
        {
            thread.Messages.Add(answer);
            ThreadStore.Save(thread);
        }
        Composer.Focus();
    }

    private void OnStop(object sender, RoutedEventArgs e) => _answering?.Cancel();

#if DEBUG
    public void SendText(string text)
    {
        Composer.Text = text;
        OnSend(this, new RoutedEventArgs());
    }

    public void OpenSettingsForTest() => new SettingsWindow { Owner = this }.Show();
#endif

    // ------------------------------------------------------------------ burbujas

    private Border Bubble(string role, string content, string? reasoning)
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
                Content = new TextBlock { Text = reasoning, TextWrapping = TextWrapping.Wrap, FontSize = fontSize - 2, Foreground = (Brush)FindResource("TextSecondary"), Margin = new Thickness(0, 4, 0, 0) },
            };
            panel.Children.Add(expander);
        }
        if (user)
            panel.Children.Add(new TextBlock { Text = content, TextWrapping = TextWrapping.Wrap, FontSize = fontSize, Foreground = foreground });
        else if (content.Length == 0 && streaming)
            panel.Children.Add(new TextBlock { Text = "…", FontSize = fontSize, Foreground = foreground });
        else
            panel.Children.Add(Markdown.Render(content, fontSize, foreground, (Brush)FindResource("PageBackground"), (Brush)FindResource("Accent"), Loc.Get("Copy")));
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
