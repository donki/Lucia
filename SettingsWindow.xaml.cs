using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using SocAiChat.Engine;
using SocAiChat.Localization;
using SocAiChat.Services;

namespace SocAiChat;

public partial class SettingsWindow : Window
{
    private CancellationTokenSource? _download;

    public SettingsWindow()
    {
        InitializeComponent();
        SourceInitialized += (_, _) => ThemeManager.ApplyToWindow(this);
        Loc.LanguageChanged += ApplyTexts;
        Closed += (_, _) => { Loc.LanguageChanged -= ApplyTexts; _download?.Cancel(); };
        ApplyTexts();
    }

    private void ApplyTexts()
    {
        var s = AppSettings.Current;
        Title = Loc.Get("SettingsTitle");
        CloseButton.ToolTip = Loc.Get("Close");
        ModelTitle.Text = Loc.Get("ModelTitle");
        PaintModel();
        ImportButton.Content = Loc.Get("ModelImport");
        ImportHint.Text = Loc.Get("ModelImportHint");
        CancelDownloadButton.ToolTip = Loc.Get("ModelCancel");
        InstructionsTitle.Text = Loc.Get("InstructionsTitle");
        InstructionsHint.Text = Loc.Get("InstructionsHint");
        InstructionsBox.Text = s.Instructions;
        InstructionsBox.ToolTip = Loc.Get("InstructionsPlaceholder");
        ThinkingCheck.Content = Loc.Get("ThinkingTitle");
        ThinkingCheck.IsChecked = s.Thinking;
        ThinkingHint.Text = Loc.Get("ThinkingHint");
        FontSizeTitle.Text = Loc.Get("FontSizeTitle");
        FontSizeSlider.Value = s.FontSize;
        DoorTitle.Text = Loc.Get("DoorTitle");
        DoorHint.Text = Loc.Get("DoorHint");
        DoorCheck.Content = Loc.Get("DoorEnable");
        DoorUrlLabel.Text = Loc.Get("DoorUrl");
        DoorTokenLabel.Text = Loc.Get("DoorToken");
        DoorPortLabel.Text = Loc.Get("DoorPort");
        CopyUrlButton.ToolTip = CopyTokenButton.ToolTip = Loc.Get("Copy");
        NewTokenButton.ToolTip = Loc.Get("DoorNewToken");
        DoorHow.Text = Loc.Get("DoorHow");
        PaintDoor();
        WorkTitle.Text = Loc.Get("WorkTitle");
        WorkHint.Text = Loc.Get("WorkHint");
        WorkFolderLabel.Text = Loc.Get("WorkFolder");
        WorkFolderBox.Text = s.WorkFolder;
        WorkFolderButton.ToolTip = Loc.Get("WorkFolderPick");
        OpenCommandLogButton.Content = Loc.Get("OpenCommandLog");
        WindowsTitle.Text = Loc.Get("WindowsSection");
        TrayCheck.Content = Loc.Get("TrayOnMinimize");
        TrayCheck.IsChecked = s.TrayOnMinimize;
        TrayHint.Text = Loc.Get("TrayOnMinimizeHint");
        StartupCheck.Content = Loc.Get("StartWithWindows");
        StartupCheck.IsChecked = WindowsStartup.IsEnabled();
        StartupHint.Text = Loc.Get("StartWithWindowsHint");
        LanguageTitle.Text = Loc.Get("LanguageTitle");
        PaintLanguageButtons();
        DiagnosticsTitle.Text = Loc.Get("DiagnosticsTitle");
        DiagnosticsText.Text = Loc.Format("Accelerator", EnginePin.Build, App.Engine.Accelerator) + Environment.NewLine + Paths.Root;
        OpenLogButton.Content = Loc.Get("OpenEngineLog");
        OpenDataButton.Content = Loc.Get("OpenDataFolder");
    }

    // ------------------------------------------------------------------ la IA

    private void PaintModel()
    {
        var s = AppSettings.Current;
        if (s.HasModel)
        {
            ModelCurrent.Text = Loc.Format("ModelCurrent", s.ModelName ?? Path.GetFileName(s.ModelPath!));
            ModelDetail.Text = Loc.Format("ModelFile", s.ModelPath!) + (s.ModelLicense is { Length: > 0 } l ? " · " + Loc.Format("ModelLicenseLine", l) : string.Empty);
        }
        else
        {
            ModelCurrent.Text = Loc.Get("ModelNone");
            ModelDetail.Text = string.Empty;
        }
        var ram = ModelCatalog.TotalRamBytes();
        ModelRam.Text = Loc.Format("ModelRam", Human(ram));
        CatalogList.Children.Clear();
        var recommended = ModelCatalog.Recommended(ram);
        foreach (var model in ModelCatalog.Fitting(ram))
            CatalogList.Children.Add(CatalogRow(model, model == recommended, s.ModelName == model.Name));
    }

    private UIElement CatalogRow(CatalogModel model, bool recommended, bool installed)
    {
        var grid = new Grid { Margin = new Thickness(0, 4, 0, 4) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var text = new StackPanel();
        var title = new TextBlock { Style = (Style)FindResource("BodyText"), FontWeight = FontWeights.SemiBold };
        title.Inlines.Add(model.Name);
        if (recommended)
            title.Inlines.Add(new System.Windows.Documents.Run("  " + Loc.Get("ModelRecommended")) { Foreground = (Brush)FindResource("Success"), FontWeight = FontWeights.Normal, FontSize = 11 });
        text.Children.Add(title);
        text.Children.Add(new TextBlock { Style = (Style)FindResource("HintText"), Text = $"{Loc.Get(model.Blurb)} · {Loc.Format("ModelSize", Human(model.ApproxBytes))} · {model.License}" });
        grid.Children.Add(text);
        var button = new Button
        {
            Style = (Style)FindResource(installed ? "GhostIconButton" : "IconButton"),
            Content = installed ? "" : "",
            ToolTip = installed ? Loc.Get("ModelInstalled") : Loc.Get("ModelInstall"),
            IsEnabled = !installed && _download is null,
            VerticalAlignment = VerticalAlignment.Center,
        };
        button.Click += async (_, _) => await InstallAsync(model);
        Grid.SetColumn(button, 1);
        grid.Children.Add(button);
        return grid;
    }

    private async Task InstallAsync(CatalogModel model)
    {
        _download = new CancellationTokenSource();
        DownloadBox.Visibility = Visibility.Visible;
        DownloadBar.IsIndeterminate = true;
        DownloadText.Text = Loc.Format("ModelInstalling", model.Name, "…");
        PaintModel();
        try
        {
            var file = await ModelCatalog.PickFileAsync(model, _download.Token);
            var progress = new Progress<Downloader.Progress>(p =>
            {
                DownloadBar.IsIndeterminate = p.Fraction is null;
                DownloadBar.Value = (p.Fraction ?? 0) * 100;
                DownloadText.Text = Loc.Format("ModelInstalling", model.Name, p.Total is { } t ? $"{Human(p.Received)} / {Human(t)}" : Human(p.Received));
            });
            var path = await ModelCatalog.DownloadAsync(model, file, progress, _download.Token);
            Activate(path, model.Name, model.License);
            DownloadText.Text = Loc.Get("ModelInstalled");
        }
        catch (OperationCanceledException)
        {
            DownloadBox.Visibility = Visibility.Collapsed;
        }
        catch (Exception ex)
        {
            DownloadBox.Visibility = Visibility.Collapsed;
            PromptWindow.Alert(this, Loc.Get("Error"), ex.Message);
        }
        finally
        {
            _download?.Dispose();
            _download = null;
            DownloadBar.IsIndeterminate = false;
            PaintModel();
        }
    }

    private void OnCancelDownload(object sender, RoutedEventArgs e) => _download?.Cancel();

    private void OnImport(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog { Filter = "GGUF (*.gguf)|*.gguf", CheckFileExists = true };
        if (dialog.ShowDialog(this) != true)
            return;
        Activate(dialog.FileName, ModelCatalog.NameFromFile(dialog.FileName), null);
        PaintModel();
    }

    private void Activate(string path, string name, string? license)
    {
        var s = AppSettings.Current;
        s.ModelPath = path;
        s.ModelName = name;
        s.ModelLicense = license;
        s.Save();
        (Owner as MainWindow)?.ModelChanged();
    }

    // ------------------------------------------------------------------ instrucciones, pensamiento, letra

    private void OnInstructionsChanged(object sender, RoutedEventArgs e)
    {
        var s = AppSettings.Current;
        if (s.Instructions == InstructionsBox.Text) return;
        s.Instructions = InstructionsBox.Text;
        s.Save();
    }

    private void OnThinkingChanged(object sender, RoutedEventArgs e)
    {
        AppSettings.Current.Thinking = ThinkingCheck.IsChecked == true;
        AppSettings.Current.Save();
    }

    private void OnFontSizeChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (!IsLoaded) return;
        AppSettings.Current.FontSize = FontSizeSlider.Value;
        AppSettings.Current.Save();
    }

    // ------------------------------------------------------------------ puerta de editores

    private void PaintDoor()
    {
        var d = AppSettings.Current.EditorDoor;
        DoorCheck.IsChecked = d.Enabled;
        DoorDetails.Visibility = d.Enabled ? Visibility.Visible : Visibility.Collapsed;
        DoorUrl.Text = App.Door.BaseUrl;
        DoorToken.Text = d.Token;
        DoorPort.Text = d.Port.ToString();
        DoorState.Text = App.Door.LastError ?? (App.Door.Running ? Loc.Get("DoorListening") : Loc.Get("DoorStopped"));
        DoorState.Foreground = App.Door.LastError is null ? (Brush)FindResource("TextSecondary") : (Brush)FindResource("Danger");
    }

    private void OnDoorChanged(object sender, RoutedEventArgs e)
    {
        AppSettings.Current.EditorDoor.Enabled = DoorCheck.IsChecked == true;
        AppSettings.Current.Save();
        App.Door.Apply();
        PaintDoor();
    }

    private void OnPortChanged(object sender, RoutedEventArgs e)
    {
        var d = AppSettings.Current.EditorDoor;
        if (!int.TryParse(DoorPort.Text, out var port) || port < 1024 || port > 65535 || port == d.Port)
        {
            DoorPort.Text = d.Port.ToString();
            return;
        }
        d.Port = port;
        AppSettings.Current.Save();
        App.Door.Apply();
        PaintDoor();
    }

    private void OnNewToken(object sender, RoutedEventArgs e)
    {
        AppSettings.Current.EditorDoor.Token = EditorDoorSettings.NewToken();
        AppSettings.Current.Save();
        PaintDoor();
    }

    private void OnCopyUrl(object sender, RoutedEventArgs e) { try { Clipboard.SetText(DoorUrl.Text); } catch (Exception) { } }

    private void OnCopyToken(object sender, RoutedEventArgs e) { try { Clipboard.SetText(DoorToken.Text); } catch (Exception) { } }

    // ------------------------------------------------------------------ idioma y diagnostico

    private void PaintLanguageButtons()
    {
        var spanish = Loc.Language == "es";
        SpanishButton.Background = spanish ? (Brush)FindResource("Primary") : Brushes.Transparent;
        SpanishButton.Foreground = spanish ? (Brush)FindResource("OnPrimary") : (Brush)FindResource("Primary");
        EnglishButton.Background = spanish ? Brushes.Transparent : (Brush)FindResource("Primary");
        EnglishButton.Foreground = spanish ? (Brush)FindResource("Primary") : (Brush)FindResource("OnPrimary");
    }

    private void OnSpanish(object sender, RoutedEventArgs e) => UseLanguage("es");

    private void OnEnglish(object sender, RoutedEventArgs e) => UseLanguage("en");

    private static void UseLanguage(string code)
    {
        AppSettings.Current.Language = code;
        AppSettings.Current.Save();
        Loc.Use(code);
    }

    // ------------------------------------------------------------------ modo trabajo y Windows

    private void OnWorkFolderChanged(object sender, RoutedEventArgs e)
    {
        var folder = WorkFolderBox.Text.Trim();
        if (folder.Length == 0 || !Directory.Exists(folder))
        {
            WorkFolderBox.Text = AppSettings.Current.WorkFolder;
            return;
        }
        AppSettings.Current.WorkFolder = folder;
        AppSettings.Current.Save();
    }

    private void OnPickWorkFolder(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFolderDialog { InitialDirectory = AppSettings.Current.WorkFolder };
        if (dialog.ShowDialog(this) != true) return;
        WorkFolderBox.Text = dialog.FolderName;
        OnWorkFolderChanged(sender, e);
    }

    private void OnOpenCommandLog(object sender, RoutedEventArgs e) => Open(Path.Combine(Paths.Logs, "commands.log"));

    private void OnTrayChanged(object sender, RoutedEventArgs e)
    {
        AppSettings.Current.TrayOnMinimize = TrayCheck.IsChecked == true;
        AppSettings.Current.Save();
        if ((Owner as MainWindow)?.Tray is { } tray)
            tray.MinimizeToTray = AppSettings.Current.TrayOnMinimize;
    }

    private void OnStartupChanged(object sender, RoutedEventArgs e) => WindowsStartup.Set(StartupCheck.IsChecked == true);

    private void OnOpenLog(object sender, RoutedEventArgs e) => Open(Paths.EngineLog);

    private void OnOpenData(object sender, RoutedEventArgs e) => Open(Paths.Root);

    private void Open(string path)
    {
        try
        {
            if (File.Exists(path) || Directory.Exists(path))
                Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            PromptWindow.Alert(this, Loc.Get("Error"), ex.Message);
        }
    }

    private void OnClose(object sender, RoutedEventArgs e)
    {
        OnInstructionsChanged(sender, e);
        Close();
    }

    private static string Human(long bytes) => bytes switch
    {
        >= 1L << 30 => $"{bytes / (double)(1L << 30):0.#} GB",
        >= 1L << 20 => $"{bytes / (double)(1L << 20):0} MB",
        _ => $"{bytes / 1024.0:0} KB",
    };
}
