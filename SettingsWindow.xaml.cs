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
#if DEBUG
        DiagLog.Write("ventana", "Ajustes creado desde: " + Environment.StackTrace.Replace(Environment.NewLine, " | "));
#endif
        var building = System.Diagnostics.Stopwatch.StartNew();
        Loaded += (_, _) => DiagLog.Write("ventana", $"Ajustes listo en {building.ElapsedMilliseconds} ms");
        InitializeComponent();
        SourceInitialized += (_, _) => ThemeManager.ApplyToWindow(this);
        Loc.LanguageChanged += ApplyTexts;
        ModelDownloads.Changed += PaintDownload;
        ModelDownloads.Finished += OnDownloadFinished;
        Closed += (_, _) => { Loc.LanguageChanged -= ApplyTexts; ModelDownloads.Changed -= PaintDownload; ModelDownloads.Finished -= OnDownloadFinished; _search?.Cancel(); };
        ApplyTexts();
        PaintDownload();
    }

    /// <summary>
    /// Ejecuta un trabajo fuera del hilo de la interfaz con un indicador de espera (el que tarda mas de
    /// ~300 ms se ve) y, si hay boton, lo desactiva mientras tanto y lo reactiva al acabar aunque falle.
    /// El resultado del trabajo se pinta de vuelta en el hilo de la interfaz.
    /// </summary>
    private async Task RunBusyAsync<T>(Controls.BusyIndicator busy, string text, Func<T> work, Action<T> paint, System.Windows.Controls.Primitives.ButtonBase? button = null)
    {
        busy.Start(text);
        if (button is not null) button.IsEnabled = false;
        try
        {
            var result = await Task.Run(work);
            paint(result);
        }
        catch (Exception ex)
        {
            DiagLog.Write("ajustes", "tarea de Ajustes fallida: " + DiagLog.Describe(ex));
        }
        finally
        {
            busy.Stop();
            if (button is not null) button.IsEnabled = true;
        }
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
        LimitsTitle.Text = Loc.Get("LimitsTitle");
        LimitsHint.Text = Loc.Get("LimitsHint");
        AnswerTokensLabel.Text = Loc.Get("AnswerTokensLabel");
        ContextTokensLabel.Text = Loc.Get("ContextTokensLabel");
        PaintLimits();
        InstructionsTitle.Text = Loc.Get("InstructionsTitle");
        InstructionsHint.Text = Loc.Get("InstructionsHint");
        InstructionsBox.Text = s.Instructions;
        InstructionsBox.ToolTip = Loc.Get("InstructionsPlaceholder");
        PaintImages();
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
        PaintMlflow();
        WorkTitle.Text = Loc.Get("WorkTitle");
        WorkHint.Text = Loc.Get("WorkHint");
        WorkFolderLabel.Text = Loc.Get("WorkFolder");
        WorkFolderBox.Text = s.WorkFolder;
        WorkFolderButton.ToolTip = Loc.Get("WorkFolderPick");
        OpenCommandLogButton.Content = Loc.Get("OpenCommandLog");
        AgentMaxLabel.Text = Loc.Get("AgentMaxActions");
        AgentMaxBox.Text = s.AgentMaxActions.ToString(System.Globalization.CultureInfo.InvariantCulture);
        AgentMaxHint.Text = Loc.Format("AgentMaxActionsHint", ToolLoopGuard.MinMaxCalls, ToolLoopGuard.MaxMaxCalls, ToolLoopGuard.DefaultMaxCalls);
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
        OpenLogButton.Content = IconLabel("\uE9F9", Loc.Get("OpenEngineLog"));
        OpenDataButton.Content = IconLabel("\uE8B7", Loc.Get("OpenDataFolder"));
        LogsHint.Text = Loc.Get("LogsHint");
        CopyLogsButton.Content = IconLabel("\uE8C8", Loc.Get("CopyLogs"));
        OpenLogsFolderButton.Content = IconLabel("\uE838", Loc.Get("OpenLogsFolder"));
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
        // El PC (registro/VRAM) y las IA instaladas (disco) se leen fuera del hilo: con un disco lento la ventana no se queda muerta.
        _ = RunBusyAsync(ModelBusy, Loc.Get("ModelLoading"),
            () => { var pc = _pc ??= PcProfile.Detect(App.Engine.Accelerator); return (pc, installed: ModelCatalog.InstalledFiles(), recommended: ModelCatalog.Recommended(pc)); },
            r =>
            {
                ModelRam.Text = (r.pc.HasUsableGpu
                    ? Loc.Format("PcWithGpu", r.pc.Cpu, r.pc.Cores, Human(r.pc.RamBytes), r.pc.Gpu, Human(r.pc.VramBytes))
                    : Loc.Format("PcNoGpu", r.pc.Cpu, r.pc.Cores, Human(r.pc.RamBytes)))
                    + Environment.NewLine + Loc.Get("PcStarHint");
                CatalogList.Children.Clear();
                foreach (var model in ModelCatalog.Fitting(r.pc.RamBytes))
                    CatalogList.Children.Add(CatalogRow(model, ModelCatalog.FitOf(model, r.pc), model == r.recommended, s.ModelName == model.Name || r.installed.Any(i => i.Name == model.Name)));
                if (_hits is not null) PaintSearch();
                InstalledList.Children.Clear();
                InstalledLabel.Visibility = r.installed.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
                foreach (var model in r.installed)
                    InstalledList.Children.Add(InstalledRow(model, string.Equals(s.ModelPath, model.Path, StringComparison.OrdinalIgnoreCase)));
            });
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
        SearchHint.Text = string.Empty;
        SearchList.Children.Clear();
        SearchBusy.Start(Loc.Get("SearchHfSearching"));
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
            SearchBusy.Stop();
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
        if (Desktop.Current.PickFolder(this, Paths.Models, Loc.Get("ModelsFolderPick")) is not { } folder) return;
        await MoveModelsAsync(folder);
    }

    public async Task MoveModelsAsync(string target)
    {
        if (string.Equals(Path.GetFullPath(target), Path.GetFullPath(Paths.Models), StringComparison.OrdinalIgnoreCase)) return;
        var count = Directory.Exists(Paths.Models) ? Directory.GetFiles(Paths.Models, "*.gguf").Length : 0;
        if (count > 0 && !PromptWindow.Confirm(this, Loc.Get("ModelsFolder"), Loc.Format("ModelsFolderMoveConfirm", count, target))) return;
        ModelsFolderButton.IsEnabled = false;
        MoveStatus.Visibility = Visibility.Collapsed;
        MoveBusy.Start(Loc.Get("ModelsFolderMoving"));
        try
        {
            var progress = new Progress<ModelLibrary.MoveProgress>(p => MoveBusy.Update(Loc.Format("ModelsFolderMovingFile", p.File, p.Total > 0 ? $"{p.Done * 100 / p.Total}%" : "…")));
            await ModelLibrary.MoveAsync(target, progress, CancellationToken.None);
            MoveBusy.Update(Loc.Get("ModelsFolderMoved"));
            ModelsFolderBox.Text = Paths.Models;
            PaintModel();
            (Owner as MainWindow)?.ModelChanged();
        }
        catch (Exception ex)
        {
            PromptWindow.Alert(this, Loc.Get("Error"), ex.Message);
        }
        finally
        {
            ModelsFolderButton.IsEnabled = true;
        }
    }

    private void OnImport(object sender, RoutedEventArgs e)
    {
        if (Desktop.Current.PickFiles(this, null, "GGUF (*.gguf)|*.gguf", multiselect: false) is not [var file])
            return;
        var name = ModelCatalog.NameFromFile(file);
        if (string.Equals(Path.GetDirectoryName(Path.GetFullPath(file)), Path.GetFullPath(Paths.Models).TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase))
            AppSettings.Current.Remember(new InstalledModel { Name = name, Path = file });
        Activate(file, name, null);
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

    // ------------------------------------------------------------------ imagenes

    private CancellationTokenSource? _imagesInstall;

    private void PaintImages()
    {
        ImagesTitle.Text = Loc.Get("ImagesTitle");
        ImagesHint.Text = Loc.Get("ImagesHint");
        var installed = ImageEngine.ModelInstalled && ImageEngine.EngineInstalled;
        ImagesStatus.Text = _imagesInstall is not null ? Loc.Get("ImagesInstalling")
            : installed ? Loc.Format("ImagesInstalled", ImageEngine.Default.Name, ImageEngine.Default.License, ImageEngine.InstalledBytes() / (1024.0 * 1024 * 1024))
            : Loc.Format("ImagesNotInstalled", ImageEngine.Default.Name, ImageEngine.Default.ApproxBytes / (1024.0 * 1024 * 1024));
        ImagesInstallButton.Content = _imagesInstall is not null ? Loc.Get("Cancel") : Loc.Get("ImagesInstall");
        ImagesInstallButton.Visibility = installed && _imagesInstall is null ? Visibility.Collapsed : Visibility.Visible;
        ImagesFolderButton.Content = Loc.Get("ImagesFolder");
        ImagesFolderButton.Visibility = Directory.Exists(ImageEngine.ImagesFolder) ? Visibility.Visible : Visibility.Collapsed;
        ImagesRemoveButton.Content = Loc.Get("ImagesRemove");
        ImagesRemoveButton.Visibility = installed && _imagesInstall is null ? Visibility.Visible : Visibility.Collapsed;
    }

    private async void OnImagesInstall(object sender, RoutedEventArgs e)
    {
        if (_imagesInstall is not null) { _imagesInstall.Cancel(); return; }
        _imagesInstall = new CancellationTokenSource();
        PaintImages();
        ImagesBusy.Start(Loc.Get("ImagesInstalling"));
        try
        {
            await ImageEngine.EnsureInstalledAsync(new Progress<Downloader.Progress>(p => ImagesBusy.Update(Loc.Get("ImagesInstalling") + (p.Fraction is { } f ? $" {f:P0}" : string.Empty))), _imagesInstall.Token);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { PromptWindow.Alert(this, Loc.Get("ImagesTitle"), ex.Message); }
        finally { ImagesBusy.Stop(); _imagesInstall.Dispose(); _imagesInstall = null; PaintImages(); }
    }

    private void OnImagesFolder(object sender, RoutedEventArgs e)
    {
        try { Desktop.Current.Open(ImageEngine.ImagesFolder); } catch (Exception) { }
    }

    private void OnImagesRemove(object sender, RoutedEventArgs e)
    {
        if (!PromptWindow.Confirm(this, Loc.Get("ImagesRemove"), Loc.Get("ImagesRemoveConfirm"))) return;
        ImageEngine.Uninstall();
        PaintImages();
    }

    private void OnInternetChanged(object sender, RoutedEventArgs e)
    {
        AppSettings.Current.InternetAccess = InternetCheck.IsChecked == true;
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

    private void OnCopyUrl(object sender, RoutedEventArgs e) { try { Desktop.Current.SetClipboardText(DoorUrl.Text); } catch (Exception) { } }

    private void OnCopyToken(object sender, RoutedEventArgs e) { try { Desktop.Current.SetClipboardText(DoorToken.Text); } catch (Exception) { } }

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
        if (Desktop.Current.PickFolder(this, AppSettings.Current.WorkFolder, null) is not { } folder) return;
        WorkFolderBox.Text = folder;
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
            if (resource == Resource.Free)
            {
                // Lo que no sale de Lucia siempre esta permitido (AppSettings.PermissionFor): se explica, no se elige.
                PermList.Children.Add(grid);
                continue;
            }
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

    private void OnOpenLogsFolder(object sender, RoutedEventArgs e)
    {
        Directory.CreateDirectory(Paths.Logs);
        Open(Paths.Logs);
    }

    /// <summary>
    /// Un zip con los registros y un resumen del PC y de la configuracion, copiado al portapapeles
    /// como fichero: se pega con Ctrl+V en un correo o un chat. Se queda tambien en la carpeta de registros.
    /// </summary>
    private async void OnCopyLogs(object sender, RoutedEventArgs e)
    {
        CopyLogsButton.IsEnabled = false;
        LogsBusy.Start(Loc.Get("LogsZipping"));
        try
        {
            DiagLog.Write("app", "se copian los registros");
            var summary = DiagnosticsSummary();
            var zip = await Task.Run(() => DiagLog.Bundle(summary));
            Desktop.Current.SetClipboardFiles([zip]);
            PromptWindow.Alert(this, Loc.Get("CopyLogs"), Loc.Format("LogsCopied", Path.GetFileName(zip)));
        }
        catch (Exception ex)
        {
            DiagLog.Write("app", "no se han podido copiar los registros: " + DiagLog.Describe(ex));
            PromptWindow.Alert(this, Loc.Get("Error"), Loc.Format("LogsCopyFailed", ex.Message));
        }
        finally
        {
            LogsBusy.Stop();
            CopyLogsButton.IsEnabled = true;
        }
    }

    /// <summary>Lo que hace falta para entender un registro: version, PC, IA y ajustes de longitud (sin rutas ni nada personal).</summary>
    internal static string DiagnosticsSummary()
    {
        var s = AppSettings.Current;
        var pc = TokenLimits.Pc;
        var range = TokenLimits.RangeFor(s);
        long size = 0;
        try { if (s.HasModel) size = new FileInfo(s.ModelPath!).Length; } catch (Exception) { }
        var gguf = GgufInfo.Read(s.ModelPath);
        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"sOC Lucia {App.Version} — {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        sb.AppendLine($"Windows {Environment.OSVersion.Version}, .NET {Environment.Version}");
        sb.AppendLine($"CPU: {pc.Cpu} ({pc.Cores} hilos), RAM {Human(pc.RamBytes)}; grafica: {pc.Gpu ?? "-"} ({Human(pc.VramBytes)})");
        sb.AppendLine($"Motor: llama.cpp {EnginePin.Build}, {App.Engine.Accelerator}, estado {App.Engine.State}");
        sb.AppendLine($"IA: {s.ModelName ?? "-"} ({(s.HasModel ? Path.GetFileName(s.ModelPath) : "-")}, {Human(size)}), vision {(EngineHost.VisionAvailable(s) ? "si" : "no")}");
        sb.AppendLine($"GGUF: {(gguf is null ? "no se puede leer" : $"{gguf.Architecture}, contexto de entrenamiento {gguf.ContextLength}, capas {gguf.Layers}, cabezas KV {gguf.HeadsKv}, {gguf.KvBytesPerToken} bytes de cache por token")}");
        sb.AppendLine($"Contexto: ajustado {s.ContextTokens}, en uso {TokenLimits.Context(s)}, maximo {range.Max}, rapido hasta {range.Fast?.ToString() ?? "?"}");
        sb.AppendLine($"Tokens por respuesta: {TokenLimits.Answer(s)}; pensar {(s.Thinking ? "si" : "no")}; puerta de editores {(s.EditorDoor.Enabled ? "activa" : "apagada")}");
        sb.AppendLine($"Congelaciones vistas en esta sesion: {App.Watchdog?.Freezes ?? 0}");
        return sb.ToString();
    }

    /// <summary>Un boton con su icono plano (Segoe Fluent Icons, el mismo en toda la aplicacion) y su texto.</summary>
    private static StackPanel IconLabel(string glyph, string text)
    {
        var panel = new StackPanel { Orientation = Orientation.Horizontal };
        panel.Children.Add(new TextBlock { Text = glyph, FontFamily = new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets"), FontSize = 14, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) });
        panel.Children.Add(new TextBlock { Text = text, VerticalAlignment = VerticalAlignment.Center });
        return panel;
    }

    // ------------------------------------------------------------------ MLflow

    private static MlflowSettings Ml => AppSettings.Current.Mlflow;

    private void PaintMlflow()
    {
        var m = Ml;
        MlflowTitle.Text = Loc.Get("MlflowTitle");
        MlflowWhat.Text = Loc.Format("MlflowWhat", Services.Mlflow.MlflowApi.TryUri(m.TrackingUri, out var uri) ? uri.Host : Loc.Get("MlflowAnyServer"));
        MlflowCheck.Content = Loc.Get("MlflowEnable");
        MlflowCheck.IsChecked = m.Enabled;
        MlflowUriLabel.Text = Loc.Get("MlflowUri");
        MlflowUriBox.Text = m.TrackingUri;
        MlflowUriHint.Text = Loc.Get("MlflowUriHint");
        MlflowExperimentLabel.Text = Loc.Get("MlflowExperiment");
        MlflowExperimentBox.Text = m.Experiment;
        MlflowUserLabel.Text = Loc.Get("MlflowUser");
        MlflowUserBox.Text = m.Username;
        MlflowPasswordLabel.Text = Loc.Get("MlflowPassword");
        MlflowTokenLabel.Text = Loc.Get("MlflowToken");
        // En las casillas, el texto descifrado (para poder verlo con el ojo); en el fichero, solo cifrado.
        if (MlflowPasswordBox.Password.Length == 0) MlflowPasswordBox.Password = Secrets.Unprotect(m.ProtectedPassword);
        if (MlflowTokenBox.Password.Length == 0) MlflowTokenBox.Password = Secrets.Unprotect(m.ProtectedToken);
        MlflowAuthHint.Text = Loc.Get("MlflowAuthHint");
        MlflowTextCheck.Content = Loc.Get("MlflowSendText");
        MlflowTextCheck.IsChecked = m.SendText;
        MlflowTextWarning.Text = Loc.Get("MlflowSendTextWarning");
        MlflowTextNote.Text = Loc.Get("MlflowTextNote");
        MlflowTestButton.Content = IconLabel("\uE703", Loc.Get("MlflowTest"));
    }

    /// <summary>Guarda y aplica: el envio arranca, cambia o se para (en segundo plano).</summary>
    private void SaveMlflow(string what)
    {
        AppSettings.Current.Save();
        Services.Mlflow.MlflowTelemetry.Current.Configure(Ml);
        DiagLog.Write("ajustes", "MLflow: " + what);   // nunca el valor de un secreto
        PaintMlflow();
    }

    private void OnMlflowEnabled(object sender, RoutedEventArgs e)
    {
        var on = MlflowCheck.IsChecked == true;
        if (on && !Services.Mlflow.MlflowApi.TryUri(MlflowUriBox.Text, out _))
        {
            PromptWindow.Alert(this, Loc.Get("MlflowTitle"), Loc.Get("MlflowNeedsUri"));
            MlflowCheck.IsChecked = false;
            return;
        }
        if (on) OnMlflowUriChanged(sender, e);
        if (on && Ml.InstallId.Length == 0) Ml.InstallId = MlflowSettings.NewInstallId();
        Ml.Enabled = on;
        SaveMlflow(on ? "activado" : "apagado");
    }

    private void OnMlflowUriChanged(object sender, RoutedEventArgs e)
    {
        var text = MlflowUriBox.Text.Trim();
        if (text == Ml.TrackingUri) return;
        if (text.Length > 0 && !Services.Mlflow.MlflowApi.TryUri(text, out _))
        {
            PromptWindow.Alert(this, Loc.Get("MlflowUri"), Loc.Format("MlflowBadUri", text));
            MlflowUriBox.Text = Ml.TrackingUri;
            return;
        }
        if (text.Length == 0 && Ml.Enabled)
        {
            PromptWindow.Alert(this, Loc.Get("MlflowUri"), Loc.Get("MlflowNeedsUri"));
            MlflowUriBox.Text = Ml.TrackingUri;
            return;
        }
        Ml.TrackingUri = text;
        SaveMlflow("direccion cambiada");
    }

    private void OnMlflowExperimentChanged(object sender, RoutedEventArgs e)
    {
        var text = MlflowExperimentBox.Text.Trim();
        if (text.Length == 0) text = MlflowSettings.DefaultExperiment;
        if (text == Ml.Experiment) { MlflowExperimentBox.Text = text; return; }
        Ml.Experiment = text;
        SaveMlflow("experimento cambiado");
    }

    private void OnMlflowUserChanged(object sender, RoutedEventArgs e)
    {
        var text = MlflowUserBox.Text.Trim();
        if (text == Ml.Username) return;
        Ml.Username = text;
        SaveMlflow("usuario cambiado");
    }

    private void OnMlflowPasswordChanged(object sender, RoutedEventArgs e)
    {
        if (MlflowPasswordBox.Password == Secrets.Unprotect(Ml.ProtectedPassword)) return;
        Ml.ProtectedPassword = Secrets.Protect(MlflowPasswordBox.Password);
        SaveMlflow("contraseña cambiada");
    }

    private void OnMlflowTokenChanged(object sender, RoutedEventArgs e)
    {
        if (MlflowTokenBox.Password.Trim() == Secrets.Unprotect(Ml.ProtectedToken)) return;
        Ml.ProtectedToken = Secrets.Protect(MlflowTokenBox.Password.Trim());
        SaveMlflow("token cambiado");
    }

    /// <summary>Mandar el texto pide confirmacion: es lo unico que puede llevar datos personales.</summary>
    private void OnMlflowTextChanged(object sender, RoutedEventArgs e)
    {
        var on = MlflowTextCheck.IsChecked == true;
        if (on && !PromptWindow.Confirm(this, Loc.Get("MlflowSendText"), Loc.Get("MlflowSendTextConfirm")))
        {
            MlflowTextCheck.IsChecked = false;
            return;
        }
        Ml.SendText = on;
        SaveMlflow(on ? "texto de preguntas y respuestas: si" : "texto de preguntas y respuestas: no");
    }

    /// <summary>Prueba la conexion con lo que hay escrito (aunque no se haya guardado) y dice en palabras que pasa.</summary>
    private async void OnMlflowTest(object sender, RoutedEventArgs e)
    {
        MlflowTestButton.IsEnabled = false;
        MlflowTestStatus.Text = string.Empty;
        MlflowBusy.Start(Loc.Get("MlflowTesting"));
        try
        {
            var uri = MlflowUriBox.Text;
            var experiment = MlflowExperimentBox.Text.Trim().Length > 0 ? MlflowExperimentBox.Text.Trim() : MlflowSettings.DefaultExperiment;
            var (user, password, token) = (MlflowUserBox.Text.Trim(), MlflowPasswordBox.Password, MlflowTokenBox.Password.Trim());
            var http = Services.Mlflow.MlflowTelemetry.Current.Http;
            var (result, detail, version, mode) = await Task.Run(() => Services.Mlflow.MlflowApi.CheckFullAsync(http, uri, experiment, user, password, token, CancellationToken.None));
            var text = Loc.Format("MlflowCheck_" + result, experiment, detail ?? string.Empty);
            if (result is Services.Mlflow.MlflowCheck.Ok or Services.Mlflow.MlflowCheck.OkWillCreate)
                text += " " + Loc.Format("MlflowMode_" + mode, version?.ToString() ?? "?");
            DiagLog.Write("mlflow", $"prueba de conexion: {result}{(detail is null || result is Services.Mlflow.MlflowCheck.Ok ? string.Empty : " " + detail)}, version {version?.ToString() ?? "?"}, modo {mode}");
            MlflowTestStatus.Text = text;
            var ok = result is Services.Mlflow.MlflowCheck.Ok or Services.Mlflow.MlflowCheck.OkWillCreate;
            MlflowTestStatus.SetResourceReference(TextBlock.ForegroundProperty, ok ? "Success" : "Danger");
            if (!ok)
                PromptWindow.Alert(this, Loc.Get("MlflowTest"), text);
        }
        finally
        {
            MlflowBusy.Stop();
            MlflowTestButton.IsEnabled = true;
        }
    }

    // ------------------------------------------------------------------ respuestas y contexto

    private void PaintLimits()
    {
        var s = AppSettings.Current;
        var culture = System.Globalization.CultureInfo.InvariantCulture;
        AnswerTokensBox.Text = TokenLimits.Answer(s).ToString(culture);
        AnswerTokensHint.Text = Loc.Format("AnswerTokensHint", TokenLimits.AnswerMin, TokenLimits.AnswerMax, TokenLimits.AnswerDefault);
        ContextTokensBox.Text = s.ContextTokens.ToString(culture);
        // El maximo depende de la cabecera del GGUF (se lee del disco): fuera del hilo de la interfaz.
        _ = RunBusyAsync(LimitsBusy, Loc.Get("LimitsLoading"), () => TokenLimits.RangeFor(s), range =>
        {
            var hint = Loc.Format("ContextTokensHint", TokenLimits.ContextMin, range.Max, TokenLimits.ContextDefault);
            if (range.Fast is { } fast) hint += " " + Loc.Format("ContextFast", fast);
            if (range.Trained is { } trained) hint += " " + Loc.Format("ContextTrained", trained);
            if (s.ContextTokens > range.Max) hint += " " + Loc.Format("ContextCapped", range.Max);
            ContextTokensHint.Text = hint + " " + Loc.Get("ContextRestart");
        });
    }

    private void OnLimitKey(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key is not (System.Windows.Input.Key.Enter or System.Windows.Input.Key.Return)) return;
        e.Handled = true;
        if (sender == AnswerTokensBox) OnAnswerTokensChanged(sender, e);
        else if (sender == AgentMaxBox) OnAgentMaxChanged(sender, e);
        else OnContextTokensChanged(sender, e);
    }

    /// <summary>Se aplica al salir de la casilla (y con Intro); si no vale, se dice por que y se deja el valor de antes (General 6.8).</summary>
    private void OnAnswerTokensChanged(object sender, RoutedEventArgs e)
    {
        var s = AppSettings.Current;
        if (AnswerTokensBox.Text.Trim() == TokenLimits.Answer(s).ToString(System.Globalization.CultureInfo.InvariantCulture)) return;
        if (TokenLimits.Parse(AnswerTokensBox.Text, TokenLimits.AnswerMin, TokenLimits.AnswerMax) is not { } value)
        {
            PromptWindow.Alert(this, Loc.Get("AnswerTokensLabel"), Loc.Format("NumberOutOfRange", AnswerTokensBox.Text.Trim(), TokenLimits.AnswerMin, TokenLimits.AnswerMax));
            PaintLimits();
            return;
        }
        if (value != s.MaxAnswerTokens)
        {
            s.MaxAnswerTokens = value;
            s.Save();
            DiagLog.Write("ajustes", $"tokens por respuesta: {value}");
        }
        PaintLimits();
    }

    /// <summary>El tope de acciones del modo agente: al salir de la casilla o con Intro; si no vale, se dice y se deja el de antes.</summary>
    private void OnAgentMaxChanged(object sender, RoutedEventArgs e)
    {
        var s = AppSettings.Current;
        var culture = System.Globalization.CultureInfo.InvariantCulture;
        if (AgentMaxBox.Text.Trim() == s.AgentMaxActions.ToString(culture)) return;
        if (TokenLimits.Parse(AgentMaxBox.Text, ToolLoopGuard.MinMaxCalls, ToolLoopGuard.MaxMaxCalls) is not { } value)
        {
            PromptWindow.Alert(this, Loc.Get("AgentMaxActions"), Loc.Format("NumberOutOfRange", AgentMaxBox.Text.Trim(), ToolLoopGuard.MinMaxCalls, ToolLoopGuard.MaxMaxCalls));
            AgentMaxBox.Text = s.AgentMaxActions.ToString(culture);
            return;
        }
        s.AgentMaxActions = value;
        s.Save();
        AgentMaxBox.Text = value.ToString(culture);
        DiagLog.Write("ajustes", $"acciones como mucho por respuesta: {value}");
    }

    private void OnContextTokensChanged(object sender, RoutedEventArgs e)
    {
        var s = AppSettings.Current;
        if (ContextTokensBox.Text.Trim() == s.ContextTokens.ToString(System.Globalization.CultureInfo.InvariantCulture)) return;
        var range = TokenLimits.RangeFor(s);
        if (TokenLimits.Parse(ContextTokensBox.Text, TokenLimits.ContextMin, range.Max) is not { } value)
        {
            PromptWindow.Alert(this, Loc.Get("ContextTokensLabel"), Loc.Format("NumberOutOfRange", ContextTokensBox.Text.Trim(), TokenLimits.ContextMin, range.Max));
            PaintLimits();
            return;
        }
        if (value != s.ContextTokens)
        {
            s.ContextTokens = value;
            s.Save();
            // El motor no se corta aqui (podria estar contestando): se reinicia con el nuevo contexto en la siguiente pregunta.
            DiagLog.Write("ajustes", $"contexto: {value} (en uso al reiniciar el motor: {TokenLimits.Context(s)})");
        }
        PaintLimits();
    }

    private void OnOpenData(object sender, RoutedEventArgs e) => Open(Paths.Root);

    // ------------------------------------------------------------------ documentos y memoria

    private void PaintDocs()
    {
        DocsFolderBox.Text = DocumentIndex.Folder;
        if (!Directory.Exists(DocumentIndex.Folder)) { DocsCount.Text = Loc.Get("DocsMissing"); return; }
        // Contar los documentos recorre la carpeta (puede ser grande): fuera del hilo.
        _ = RunBusyAsync(DocsBusy, Loc.Get("DocsCounting"), () => DocumentIndex.Files().Count, n => DocsCount.Text = Loc.Format("DocsCount", n));
    }

    private void OnPickDocsFolder(object sender, RoutedEventArgs e)
    {
        if (Desktop.Current.PickFolder(this, Directory.Exists(DocumentIndex.Folder) ? DocumentIndex.Folder : Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), Loc.Get("DocsPick")) is not { } folder) return;
        AppSettings.Current.DocumentsFolder = string.Equals(Path.GetFullPath(folder), Path.GetFullPath(DocumentIndex.DefaultFolder), StringComparison.OrdinalIgnoreCase) ? null : folder;
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
                Desktop.Current.Open(path);
        }
        catch (Exception ex)
        {
            PromptWindow.Alert(this, Loc.Get("Error"), ex.Message);
        }
    }

    private void OnClose(object sender, RoutedEventArgs e)
    {
        OnInstructionsChanged(sender, e);
        OnAnswerTokensChanged(sender, e);
        OnContextTokensChanged(sender, e);
        OnAgentMaxChanged(sender, e);
        OnMlflowUriChanged(sender, e);
        OnMlflowExperimentChanged(sender, e);
        OnMlflowUserChanged(sender, e);
        OnMlflowPasswordChanged(sender, e);
        OnMlflowTokenChanged(sender, e);
        Close();
    }

    private static string Human(long bytes) => bytes switch
    {
        >= 1L << 30 => $"{bytes / (double)(1L << 30):0.#} GB",
        >= 1L << 20 => $"{bytes / (double)(1L << 20):0} MB",
        _ => $"{bytes / 1024.0:0} KB",
    };
}
