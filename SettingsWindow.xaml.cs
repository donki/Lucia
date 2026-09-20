using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using SocLucia.Agent;
using SocLucia.Engine;
using SocLucia.Localization;
using SocLucia.Services;

namespace SocLucia;

public partial class SettingsWindow : Window
{
    public SettingsWindow()
    {
        InitializeComponent();
        SourceInitialized += (_, _) => ThemeManager.ApplyToWindow(this);
        Loc.LanguageChanged += ApplyTexts;
        ModelDownloads.Changed += PaintDownload;
        ModelDownloads.Finished += OnDownloadFinished;
        Closed += (_, _) => { Loc.LanguageChanged -= ApplyTexts; ModelDownloads.Changed -= PaintDownload; ModelDownloads.Finished -= OnDownloadFinished; _search?.Cancel(); };
        ApplyTexts();
        PaintDownload();
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
        ModelsFolderLabel.Text = Loc.Get("ModelsFolder");
        ModelsFolderBox.Text = Paths.Models;
        ModelsFolderButton.ToolTip = Loc.Get("ModelsFolderPick");
        ModelsFolderHint.Text = Loc.Get("ModelsFolderHint");
        CancelDownloadButton.ToolTip = Loc.Get("ModelCancel");
        InstalledLabel.Text = Loc.Get("ModelsInstalled");
        CatalogLabel.Text = Loc.Get("CatalogLabel");
        OnlyGgufCheck.Content = Loc.Get("SearchOnlyGguf");
        SearchLabel.Text = Loc.Get("SearchHf");
        SearchBox.ToolTip = SearchButton.ToolTip = Loc.Get("SearchHfTip");
        if (_hits is null) SearchHint.Text = Loc.Get("SearchHfHint");
        InstructionsTitle.Text = Loc.Get("InstructionsTitle");
        InstructionsHint.Text = Loc.Get("InstructionsHint");
        InstructionsBox.Text = s.Instructions;
        InstructionsBox.ToolTip = Loc.Get("InstructionsPlaceholder");
        ThinkingCheck.Content = Loc.Get("ThinkingTitle");
        ThinkingCheck.IsChecked = s.Thinking;
        ThinkingHint.Text = Loc.Get("ThinkingHint");
        InternetCheck.Content = Loc.Get("InternetTitle");
        InternetCheck.IsChecked = s.InternetAccess;
        InternetHint.Text = Loc.Get("InternetHint");
        FontSizeTitle.Text = Loc.Get("FontSizeTitle");
        FontSizeSlider.Value = s.FontSize;
        FontSizeValue.Text = $"{s.FontSize:0} px";
        FontSizeHint.Text = Loc.Get("FontSizeHint");
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
        PermTitle.Text = Loc.Get("PermSection");
        PermHint.Text = Loc.Get("PermSectionHint");
        PaintPermissions();
        DocsTitle.Text = Loc.Get("DocsTitle");
        DocsHint.Text = Loc.Get("DocsHint");
        DocsFolderButton.ToolTip = Loc.Get("DocsPick");
        DocsOpenButton.ToolTip = Loc.Get("DocsOpen");
        PaintDocs();
        TasksTitle.Text = Loc.Get("TasksTitle");
        TasksHint.Text = Loc.Get("TasksHint");
        PaintTasks();
        MemoryTitle.Text = Loc.Get("MemoryTitle");
        MemoryHint.Text = Loc.Get("MemoryHint");
        MemoryCheck.Content = Loc.Get("MemoryEnable");
        MemoryCheck.IsChecked = s.MemoryEnabled;
        MemoryClearButton.Content = Loc.Get("MemoryClear");
        PaintMemory();
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
        var pc = _pc ??= PcProfile.Detect(App.Engine.Accelerator);
        ModelRam.Text = (pc.HasUsableGpu
            ? Loc.Format("PcWithGpu", pc.Cpu, pc.Cores, Human(pc.RamBytes), pc.Gpu, Human(pc.VramBytes))
            : Loc.Format("PcNoGpu", pc.Cpu, pc.Cores, Human(pc.RamBytes)))
            + Environment.NewLine + Loc.Get("PcStarHint");
        CatalogList.Children.Clear();
        var installed = ModelCatalog.InstalledFiles();
        var recommended = ModelCatalog.Recommended(pc);
        foreach (var model in ModelCatalog.Fitting(pc.RamBytes))
            CatalogList.Children.Add(CatalogRow(model, ModelCatalog.FitOf(model, pc), model == recommended, s.ModelName == model.Name || installed.Any(i => i.Name == model.Name)));
        if (_hits is not null) PaintSearch();
        InstalledList.Children.Clear();
        InstalledLabel.Visibility = installed.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        foreach (var model in installed)
            InstalledList.Children.Add(InstalledRow(model, string.Equals(s.ModelPath, model.Path, StringComparison.OrdinalIgnoreCase)));
    }

    // ------------------------------------------------------------------ buscador de Hugging Face

    private List<HuggingFace.Hit>? _hits;
    private CancellationTokenSource? _search;

    private void OnSearchKey(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == System.Windows.Input.Key.Enter) { e.Handled = true; OnSearchModels(sender, e); }
    }

    private async void OnSearchModels(object sender, RoutedEventArgs e) => await SearchAsync();

    private async Task SearchAsync()
    {
        var query = SearchBox.Text.Trim();
        if (query.Length == 0) return;
        _search?.Cancel();
        _search = new CancellationTokenSource();
        var token = _search.Token;
        SearchButton.IsEnabled = false;
        SearchHint.Text = Loc.Get("SearchHfSearching");
        SearchList.Children.Clear();
        try
        {
            _hits = await HuggingFace.SearchAsync(query, _pc ??= PcProfile.Detect(App.Engine.Accelerator), OnlyGgufCheck.IsChecked == true, token);
            PaintSearch();
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            SearchHint.Text = Loc.Format("SearchHfError", ex.Message);
        }
        finally
        {
            if (!token.IsCancellationRequested) SearchButton.IsEnabled = true;
        }
    }

    public async void SearchForTest(string query)
    {
        if (query.StartsWith('*')) { OnlyGgufCheck.IsChecked = false; query = query[1..]; }
        SearchBox.Text = query;
        await SearchAsync();
        foreach (var h in _hits ?? [])
            EngineHost.Log($"HF {(h.Runnable ? ModelCatalog.FitOf(h.Model, _pc!).ToString() : "no:" + h.Kind)} {h.Model.Name} | {h.Model.Repo} | {h.FileName} | {Human(h.Model.ApproxBytes)} | {h.Model.License} | {h.Downloads} desc" + (h.Gated ? " | gated" : string.Empty));
        SearchLabel.BringIntoView();
    }

    /// <summary>Los resultados, con la misma fila que el catalogo: estrella si es optima, aviso si ira lenta.</summary>
    private void PaintSearch()
    {
        SearchList.Children.Clear();
        if (_hits is null) return;
        var pc = _pc ??= PcProfile.Detect(App.Engine.Accelerator);
        var runnable = _hits.Count(h => h.Runnable);
        var optimal = _hits.Count(h => h.Runnable && ModelCatalog.FitOf(h.Model, pc) == ModelFit.Optimal);
        SearchHint.Text = _hits.Count == 0 ? Loc.Get("SearchHfNone") : Loc.Format("SearchHfFound", _hits.Count, optimal, runnable);
        var installed = ModelCatalog.InstalledFiles();
        var s = AppSettings.Current;
        foreach (var hit in _hits)
        {
            var stats = Loc.Format("SearchHfStats", HumanCount(hit.Downloads), hit.Likes) + (hit.Gated ? " · " + Loc.Get("SearchHfGated") : string.Empty);
            if (hit.Runnable)
            {
                SearchList.Children.Add(CatalogRow(hit.Model, ModelCatalog.FitOf(hit.Model, pc), false, s.ModelName == hit.Model.Name || installed.Any(i => i.Name == hit.Model.Name), $"{hit.Model.Repo} · {hit.FileName} · {stats}"));
                continue;
            }
            // No es un GGUF de un fichero: se enseña que es y se puede abrir en el navegador, pero no se instala.
            var grid = new Grid { Margin = new Thickness(0, 4, 0, 4) };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var text = new StackPanel();
            text.Children.Add(new TextBlock { Style = (Style)FindResource("BodyText"), FontWeight = FontWeights.SemiBold, Text = hit.Model.Name, Opacity = 0.8 });
            text.Children.Add(new TextBlock { Style = (Style)FindResource("HintText"), Text = $"{hit.Model.Repo} · {stats}", TextTrimming = TextTrimming.CharacterEllipsis });
            var why = hit.Kind switch
            {
                "gguf" or "gguf-sharded" => Loc.Get("SearchNotSingleGguf"),
                "" => Loc.Get("SearchNotGguf"),
                _ => Loc.Format("SearchNotGgufKind", hit.Kind),
            };
            text.Children.Add(new TextBlock { Style = (Style)FindResource("HintText"), Text = why, TextWrapping = TextWrapping.Wrap });
            grid.Children.Add(text);
            var open = new Button { Style = (Style)FindResource("GhostIconButton"), Content = "\uE774", ToolTip = Loc.Get("SearchOpenHf"), VerticalAlignment = VerticalAlignment.Center };
            var url = hit.Url;
            open.Click += (_, _) => Open(url);
            Grid.SetColumn(open, 1);
            grid.Children.Add(open);
            SearchList.Children.Add(grid);
        }
    }

    private static string HumanCount(long n) => n switch
    {
        >= 1_000_000 => $"{n / 1_000_000.0:0.#} M",
        >= 1_000 => $"{n / 1_000.0:0.#} k",
        _ => n.ToString(),
    };

    /// <summary>Una IA que ya esta en la carpeta: se puede poner en uso o borrar (los GGUF pesan gigas).</summary>
    private UIElement InstalledRow(InstalledModel model, bool active)
    {
        var grid = new Grid { Margin = new Thickness(0, 4, 0, 4) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var text = new StackPanel();
        var title = new TextBlock { Style = (Style)FindResource("BodyText"), FontWeight = FontWeights.SemiBold };
        title.Inlines.Add(model.Name);
        if (active)
            title.Inlines.Add(new System.Windows.Documents.Run("  " + Loc.Get("ModelActive")) { Foreground = (Brush)FindResource("Success"), FontWeight = FontWeights.Normal, FontSize = 11 });
        text.Children.Add(title);
        long size = 0;
        try { size = new FileInfo(model.Path).Length; } catch (Exception) { }
        text.Children.Add(new TextBlock { Style = (Style)FindResource("HintText"), Text = $"{Path.GetFileName(model.Path)} · {Human(size)}" + (model.License is { Length: > 0 } l ? " · " + l : string.Empty), TextTrimming = TextTrimming.CharacterEllipsis });
        grid.Children.Add(text);
        var use = new Button
        {
            Style = (Style)FindResource("GhostIconButton"),
            Content = "\uE73E",
            ToolTip = Loc.Get("ModelUse"),
            IsEnabled = !active,
            VerticalAlignment = VerticalAlignment.Center,
        };
        use.Click += (_, _) => { Activate(model.Path, model.Name, model.License); PaintModel(); };
        Grid.SetColumn(use, 1);
        grid.Children.Add(use);
        var delete = new Button
        {
            Style = (Style)FindResource("DangerIconButton"),
            Content = "\uE74D",
            ToolTip = Loc.Get("ModelDelete"),
            IsEnabled = !ModelDownloads.Busy,
            VerticalAlignment = VerticalAlignment.Center,
        };
        delete.Click += (_, _) => DeleteModel(model);
        Grid.SetColumn(delete, 2);
        grid.Children.Add(delete);
        return grid;
    }

    private void DeleteModel(InstalledModel model)
    {
        if (!PromptWindow.Confirm(this, Loc.Get("ModelDelete"), Loc.Format("ModelDeleteConfirm", model.Name)))
            return;
        try
        {
            ModelCatalog.Delete(model);
            (Owner as MainWindow)?.ModelChanged();
        }
        catch (Exception ex)
        {
            PromptWindow.Alert(this, Loc.Get("Error"), ex.Message);
        }
        PaintModel();
    }

    private PcProfile? _pc;

    private UIElement CatalogRow(CatalogModel model, ModelFit fit, bool recommended, bool installed, string? extra = null)
    {
        var grid = new Grid { Margin = new Thickness(0, 4, 0, 4) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var text = new StackPanel();
        var title = new TextBlock { Style = (Style)FindResource("BodyText"), FontWeight = FontWeights.SemiBold };
        if (fit == ModelFit.Optimal)
            title.Inlines.Add(new System.Windows.Documents.Run("★ ") { Foreground = (Brush)FindResource("Star") });
        title.Inlines.Add(model.Name);
        if (recommended)
            title.Inlines.Add(new System.Windows.Documents.Run("  " + Loc.Get("ModelRecommended")) { Foreground = (Brush)FindResource("Success"), FontWeight = FontWeights.Normal, FontSize = 11 });
        text.Children.Add(title);
        var fitText = fit switch
        {
            ModelFit.Optimal => Loc.Get(_pc!.HasUsableGpu ? "FitOptimalGpu" : "FitOptimalCpu"),
            ModelFit.Ok => Loc.Get(_pc!.HasUsableGpu ? "FitOkGpu" : "FitOkCpu"),
            ModelFit.TooBig => Loc.Get("FitTooBig"),
            _ => Loc.Get("FitSlow"),
        };
        if (extra is not null)
            text.Children.Add(new TextBlock { Style = (Style)FindResource("HintText"), Text = extra, TextTrimming = TextTrimming.CharacterEllipsis });
        text.Children.Add(new TextBlock { Style = (Style)FindResource("HintText"), Text = $"{Loc.Get(model.Blurb)} · {Loc.Format("ModelSize", Human(model.ApproxBytes))} · {model.License}", TextWrapping = TextWrapping.Wrap });
        text.Children.Add(new TextBlock { Style = (Style)FindResource("HintText"), Text = fitText, Foreground = fit is ModelFit.Slow or ModelFit.TooBig ? (Brush)FindResource("Danger") : fit == ModelFit.Optimal ? (Brush)FindResource("Success") : (Brush)FindResource("TextSecondary"), TextWrapping = TextWrapping.Wrap });
        grid.Children.Add(text);
        Button button;
        if (installed)
        {
            // Ya esta en el disco: el boton es borrarla (los GGUF pesan gigas).
            button = new Button { Style = (Style)FindResource("DangerIconButton"), Content = "\uE74D", ToolTip = Loc.Get("ModelDelete"), IsEnabled = !ModelDownloads.Busy, VerticalAlignment = VerticalAlignment.Center };
            button.Click += (_, _) =>
            {
                var file = ModelCatalog.InstalledFiles().FirstOrDefault(i => i.Name == model.Name);
                if (file is not null) DeleteModel(file);
            };
        }
        else
        {
            button = new Button { Style = (Style)FindResource("IconButton"), Content = "\uE896", ToolTip = fit == ModelFit.TooBig ? Loc.Get("FitTooBig") : Loc.Get("ModelInstall"), IsEnabled = fit != ModelFit.TooBig && !ModelDownloads.Busy, VerticalAlignment = VerticalAlignment.Center };
            button.Click += (_, _) => { ModelDownloads.Start(model); PaintModel(); };
        }
        Grid.SetColumn(button, 1);
        grid.Children.Add(button);
        return grid;
    }

    /// <summary>La descarga vive en ModelDownloads (sigue con esta ventana cerrada); aqui solo se pinta.</summary>
    private void PaintDownload()
    {
        var download = ModelDownloads.Current;
        if (download is null)
        {
            if (DownloadBox.Visibility == Visibility.Visible && !_downloadJustFinished)
                DownloadBox.Visibility = Visibility.Collapsed;
            return;
        }
        _downloadJustFinished = false;
        DownloadBox.Visibility = Visibility.Visible;
        CancelDownloadButton.Visibility = Visibility.Visible;
        DownloadBar.Visibility = Visibility.Visible;
        DownloadBar.IsIndeterminate = download.Fraction is null;
        DownloadBar.Value = (download.Fraction ?? 0) * 100;
        DownloadText.Text = Loc.Format("ModelInstalling", download.Model.Name, download.Total is { } t ? $"{Human(download.Received)} / {Human(t)}" : Human(download.Received));
    }

    private bool _downloadJustFinished;

    private void OnDownloadFinished(CatalogModel model, Exception? error)
    {
        DownloadBar.IsIndeterminate = false;
        if (error is null)
        {
            _downloadJustFinished = true;
            DownloadBox.Visibility = Visibility.Visible;
            DownloadBar.Visibility = Visibility.Collapsed;
            CancelDownloadButton.Visibility = Visibility.Collapsed;
            DownloadText.Text = Loc.Get("ModelInstalled");
        }
        else
        {
            DownloadBox.Visibility = Visibility.Collapsed;
            if (error is not OperationCanceledException)
                PromptWindow.Alert(this, Loc.Get("Error"), error.Message);
        }
        PaintModel();
    }

    private void OnCancelDownload(object sender, RoutedEventArgs e) => ModelDownloads.Cancel();

    /// <summary>Cambiar la carpeta de modelos: se elige, se mueven los GGUF que haya y se apunta el activo a su nueva ruta.</summary>
    private async void OnPickModelsFolder(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFolderDialog { InitialDirectory = Paths.Models, Title = Loc.Get("ModelsFolderPick") };
        if (dialog.ShowDialog(this) != true) return;
        await MoveModelsAsync(dialog.FolderName);
    }

    public async Task MoveModelsAsync(string target)
    {
        if (string.Equals(Path.GetFullPath(target), Path.GetFullPath(Paths.Models), StringComparison.OrdinalIgnoreCase)) return;
        var count = Directory.Exists(Paths.Models) ? Directory.GetFiles(Paths.Models, "*.gguf").Length : 0;
        if (count > 0 && !PromptWindow.Confirm(this, Loc.Get("ModelsFolder"), Loc.Format("ModelsFolderMoveConfirm", count, target))) return;
        ModelsFolderButton.IsEnabled = false;
        MoveStatus.Visibility = Visibility.Visible;
        MoveStatus.Text = Loc.Get("ModelsFolderMoving");
        try
        {
            var progress = new Progress<ModelLibrary.MoveProgress>(p => MoveStatus.Text = Loc.Format("ModelsFolderMovingFile", p.File, p.Total > 0 ? $"{p.Done * 100 / p.Total}%" : "…"));
            await ModelLibrary.MoveAsync(target, progress, CancellationToken.None);
            MoveStatus.Text = Loc.Get("ModelsFolderMoved");
            ModelsFolderBox.Text = Paths.Models;
            PaintModel();
            (Owner as MainWindow)?.ModelChanged();
        }
        catch (Exception ex)
        {
            MoveStatus.Text = string.Empty;
            PromptWindow.Alert(this, Loc.Get("Error"), ex.Message);
        }
        finally
        {
            ModelsFolderButton.IsEnabled = true;
        }
    }

    private void OnImport(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog { Filter = "GGUF (*.gguf)|*.gguf", CheckFileExists = true };
        if (dialog.ShowDialog(this) != true)
            return;
        var name = ModelCatalog.NameFromFile(dialog.FileName);
        if (string.Equals(Path.GetDirectoryName(Path.GetFullPath(dialog.FileName)), Path.GetFullPath(Paths.Models).TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase))
            AppSettings.Current.Remember(new InstalledModel { Name = name, Path = dialog.FileName });
        Activate(dialog.FileName, name, null);
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

    private void OnInternetChanged(object sender, RoutedEventArgs e)
    {
        AppSettings.Current.InternetAccess = InternetCheck.IsChecked == true;
        AppSettings.Current.Save();
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
        FontSizeValue.Text = $"{FontSizeSlider.Value:0} px";
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

    private void OnOpenCommandLog(object sender, RoutedEventArgs e) => Open(Path.Combine(Paths.Logs, "actions.log"));

    /// <summary>Una fila por recurso: preguntar, permitir siempre o no ofrecerselo a la IA.</summary>
    private void PaintPermissions()
    {
        PermList.Children.Clear();
        var s = AppSettings.Current;
        foreach (var resource in Enum.GetValues<Resource>())
        {
            if (resource == Resource.Internet) continue;   // lo manda la casilla «acceder a internet» de arriba
            var grid = new Grid { Margin = new Thickness(0, 3, 0, 3) };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var text = new StackPanel();
            var title = new TextBlock { Style = (Style)FindResource("BodyText") };
            title.Inlines.Add(new System.Windows.Documents.Run(PermissionWindow.Glyph(resource) + "  ") { FontFamily = new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets"), Foreground = (Brush)FindResource("Primary") });
            title.Inlines.Add(Loc.Get("Perm_" + resource));
            text.Children.Add(title);
            text.Children.Add(new TextBlock { Style = (Style)FindResource("HintText"), Text = Loc.Get("PermHint_" + resource), TextWrapping = TextWrapping.Wrap });
            grid.Children.Add(text);
            var choices = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 0, 0) };
            var current = s.PermissionFor(resource);
            foreach (var permission in new[] { Permission.Ask, Permission.Allow, Permission.Deny })
            {
                var radio = new RadioButton
                {
                    Content = Loc.Get(permission == Permission.Allow ? "PermAllowAlways" : "Perm" + permission),
                    GroupName = "perm_" + resource,
                    IsChecked = permission == current,
                    Margin = new Thickness(10, 0, 0, 0),
                    Foreground = (Brush)FindResource("TextPrimary"),
                    VerticalContentAlignment = VerticalAlignment.Center,
                    Cursor = System.Windows.Input.Cursors.Hand,
                };
                var chosen = permission;
                radio.Checked += (_, _) => { s.SetPermission(resource, chosen); s.Save(); };
                choices.Children.Add(radio);
            }
            Grid.SetColumn(choices, 1);
            grid.Children.Add(choices);
            PermList.Children.Add(grid);
        }
    }

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

    // ------------------------------------------------------------------ documentos y memoria

    private void PaintDocs()
    {
        DocsFolderBox.Text = DocumentIndex.Folder;
        var n = DocumentIndex.Files().Count;
        DocsCount.Text = Directory.Exists(DocumentIndex.Folder) ? Loc.Format("DocsCount", n) : Loc.Get("DocsMissing");
    }

    private void OnPickDocsFolder(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFolderDialog { InitialDirectory = Directory.Exists(DocumentIndex.Folder) ? DocumentIndex.Folder : Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), Title = Loc.Get("DocsPick") };
        if (dialog.ShowDialog(this) != true) return;
        AppSettings.Current.DocumentsFolder = string.Equals(Path.GetFullPath(dialog.FolderName), Path.GetFullPath(DocumentIndex.DefaultFolder), StringComparison.OrdinalIgnoreCase) ? null : dialog.FolderName;
        AppSettings.Current.Save();
        PaintDocs();
    }

    private void OnOpenDocs(object sender, RoutedEventArgs e)
    {
        try { Directory.CreateDirectory(DocumentIndex.Folder); } catch (Exception) { }
        Open(DocumentIndex.Folder);
        PaintDocs();
    }

    private void PaintTasks()
    {
        TasksList.Children.Clear();
        var tasks = Scheduler.All;
        TasksEmpty.Text = tasks.Count == 0 ? Loc.Get("TasksEmpty") : string.Empty;
        TasksEmpty.Visibility = tasks.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        foreach (var task in tasks)
        {
            var grid = new Grid { Margin = new Thickness(0, 3, 0, 3) };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var on = new CheckBox { Style = (Style)FindResource("Check"), IsChecked = task.Enabled, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0), ToolTip = Loc.Get("TaskEnabled") };
            var id = task.Id;
            on.Click += (_, _) => { Scheduler.SetEnabled(id, on.IsChecked == true); PaintTasks(); };
            grid.Children.Add(on);
            var text = new StackPanel();
            text.Children.Add(new TextBlock { Style = (Style)FindResource("BodyText"), Text = task.Title, FontWeight = FontWeights.SemiBold });
            text.Children.Add(new TextBlock { Style = (Style)FindResource("HintText"), Text = task.Prompt, TextTrimming = TextTrimming.CharacterEllipsis, MaxHeight = 36 });
            text.Children.Add(new TextBlock { Style = (Style)FindResource("HintText"), Text = task.Describe(System.Globalization.CultureInfo.CurrentUICulture) + (task.Enabled ? " · " + Loc.Format("TaskNext", task.NextRun.LocalDateTime.ToString("g")) : string.Empty) + (task.LastRun is { } l ? " · " + Loc.Format("TaskLast", l.LocalDateTime.ToString("g")) : string.Empty) });
            Grid.SetColumn(text, 1);
            grid.Children.Add(text);
            var delete = new Button { Style = (Style)FindResource("DangerIconButton"), Content = "\uE74D", ToolTip = Loc.Get("TaskDelete"), VerticalAlignment = VerticalAlignment.Center };
            delete.Click += (_, _) => { Scheduler.Remove(id); PaintTasks(); };
            Grid.SetColumn(delete, 2);
            grid.Children.Add(delete);
            TasksList.Children.Add(grid);
        }
    }

    private void PaintMemory()
    {
        MemoryList.Children.Clear();
        var facts = UserMemory.All;
        MemoryEmpty.Text = facts.Count == 0 ? Loc.Get("MemoryEmpty") : string.Empty;
        MemoryEmpty.Visibility = facts.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        MemoryClearButton.IsEnabled = facts.Count > 0;
        foreach (var fact in facts)
        {
            var grid = new Grid { Margin = new Thickness(0, 2, 0, 2) };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var text = new StackPanel();
            text.Children.Add(new TextBlock { Style = (Style)FindResource("BodyText"), Text = fact.Text, TextWrapping = TextWrapping.Wrap });
            text.Children.Add(new TextBlock { Style = (Style)FindResource("HintText"), Text = fact.At.LocalDateTime.ToString("g") });
            grid.Children.Add(text);
            var delete = new Button { Style = (Style)FindResource("GhostIconButton"), Content = "\uE74D", ToolTip = Loc.Get("MemoryForget"), VerticalAlignment = VerticalAlignment.Center };
            var id = fact.Id;
            delete.Click += (_, _) => { UserMemory.Remove(id); PaintMemory(); };
            Grid.SetColumn(delete, 1);
            grid.Children.Add(delete);
            MemoryList.Children.Add(grid);
        }
    }

    private void OnMemoryChanged(object sender, RoutedEventArgs e)
    {
        AppSettings.Current.MemoryEnabled = MemoryCheck.IsChecked == true;
        AppSettings.Current.Save();
    }

    private void OnClearMemory(object sender, RoutedEventArgs e)
    {
        if (!PromptWindow.Confirm(this, Loc.Get("MemoryTitle"), Loc.Get("MemoryClearConfirm"))) return;
        UserMemory.Clear();
        PaintMemory();
    }

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
