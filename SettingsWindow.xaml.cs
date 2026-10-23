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
        Closed += (_, _) => { Loc.LanguageChanged -= ApplyTexts; ModelDownloads.Changed -= PaintDownload; ModelDownloads.Finished -= OnDownloadFinished; _search?.Cancel(); _cloud?.Dispose(); };
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
        PaintDownload();
        InstalledLabel.Text = Loc.Get("ModelsInstalled");
        CatalogLabel.Text = Loc.Get("CatalogLabel");
        PaintFilters();
        PaintTeach();
        PaintOnline();
        PaintMcp();
        PaintSkills();
        PaintFrameworks();
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
        PaintAgentMax();
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
        PaintAccount();
        PaintTabs();
    }

    // ------------------------------------------------------------------ cuenta / sincronizaci\u00F3n en la nube

    private CloudSync? _cloud;
    private CloudSync Cloud => _cloud ??= new CloudSync(AppSettings.Current);
    private System.Threading.CancellationTokenSource? _cloudCancel;
    private IReadOnlyList<CloudModelEntry> _missingModels = [];

    private void PaintAccount()
    {
        var s = AppSettings.Current;
        AccountTitle.Text = Loc.Get("AccountTitle");
        AccountHint.Text = Loc.Get("AccountHint");
        var signedIn = s.CloudStorage != CloudStorageMode.Local;
        AccountStatus.Text = signedIn
            ? Loc.Format("AccountSignedIn", s.CloudAccountEmail, s.CloudStorage == CloudStorageMode.GoogleDrive ? "Google Drive" : "OneDrive")
            : Loc.Get("AccountNotSignedIn");
        GoogleButton.Content = IconLabel("\uE774", Loc.Get("AccountGoogle"));
        MicrosoftButton.Content = IconLabel("\uE774", Loc.Get("AccountMicrosoft"));
        GoogleButton.IsEnabled = CloudSync.IsAvailable(CloudStorageMode.GoogleDrive);
        MicrosoftButton.IsEnabled = CloudSync.IsAvailable(CloudStorageMode.OneDrive);
        SignOutButton.Content = IconLabel("\uE7E8", Loc.Get("AccountSignOut"));
        SignOutButton.Visibility = signedIn ? Visibility.Visible : Visibility.Collapsed;
        AccountPassphraseLabel.Text = Loc.Get("AccountPassphrase");
        AccountPassphraseBox.Password = s.CloudPassphrase;
        AccountIncludeKeys.Content = Loc.Get("AccountIncludeKeys");
        AccountIncludeKeys.IsChecked = s.CloudIncludeKeys;
        CloudSaveButton.Content = IconLabel("\uE898", Loc.Get("AccountSave"));
        CloudRestoreButton.Content = IconLabel("\uE896", Loc.Get("AccountRestore"));
        CloudSaveButton.IsEnabled = CloudRestoreButton.IsEnabled = signedIn;
        DownloadMissingButton.Content = IconLabel("\uE896", Loc.Get("AccountDownloadMissing"));
    }

    private void OnPassphraseChanged(object sender, RoutedEventArgs e)
    {
        AppSettings.Current.CloudPassphrase = AccountPassphraseBox.Password;
        AppSettings.Current.Save();
    }

    private void OnIncludeKeysChanged(object sender, RoutedEventArgs e)
    {
        AppSettings.Current.CloudIncludeKeys = AccountIncludeKeys.IsChecked == true;
        AppSettings.Current.Save();
    }

    private async void OnSignInGoogle(object sender, RoutedEventArgs e) => await SignInAsync(CloudStorageMode.GoogleDrive);
    private async void OnSignInMicrosoft(object sender, RoutedEventArgs e) => await SignInAsync(CloudStorageMode.OneDrive);

    private async Task SignInAsync(CloudStorageMode mode)
    {
        CloudStatus.Visibility = Visibility.Collapsed;
        CloudBusy.Start(Loc.Get("AccountSigningIn"));
        _cloudCancel?.Cancel();
        _cloudCancel = new System.Threading.CancellationTokenSource();
        try
        {
            var email = await Cloud.SignInAsync(mode, _cloudCancel.Token);
            CloudSay(Loc.Format("AccountSignedIn", email, mode == CloudStorageMode.GoogleDrive ? "Google Drive" : "OneDrive"));
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { CloudSay(Loc.Format("AccountFailed", ex.Message)); }
        finally { CloudBusy.Stop(); PaintAccount(); }
    }

    private void OnSignOut(object sender, RoutedEventArgs e)
    {
        Cloud.SignOut();
        PaintAccount();
    }

    private async void OnCloudSave(object sender, RoutedEventArgs e)
    {
        var s = AppSettings.Current;
        var includeKeys = AccountIncludeKeys.IsChecked == true;
        if (includeKeys && AccountPassphraseBox.Password.Length == 0) { CloudSay(Loc.Get("CloudNeedKeysPassphrase")); return; }
        CloudBusy.Start(Loc.Get("AccountSaving"));
        _cloudCancel?.Cancel();
        _cloudCancel = new System.Threading.CancellationTokenSource();
        try
        {
            await Cloud.SaveAsync(includeKeys, AccountPassphraseBox.Password, _cloudCancel.Token);
            CloudSay(Loc.Get("AccountSaved"));
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { CloudSay(Loc.Format("AccountFailed", ex.Message)); }
        finally { CloudBusy.Stop(); }
    }

    private async void OnCloudRestore(object sender, RoutedEventArgs e)
    {
        CloudBusy.Start(Loc.Get("AccountRestoring"));
        _cloudCancel?.Cancel();
        _cloudCancel = new System.Threading.CancellationTokenSource();
        MissingModelsList.Children.Clear();
        DownloadMissingButton.Visibility = Visibility.Collapsed;
        try
        {
            var result = await Cloud.RestoreAsync(AccountPassphraseBox.Password, _cloudCancel.Token);
            switch (result.Kind)
            {
                case "none": CloudSay(Loc.Get("AccountRestoreNone")); break;
                case "needs_passphrase": CloudSay(Loc.Get("CloudNeedKeysPassphrase")); break;
                case "wrong_passphrase": CloudSay(Loc.Get("AccountWrongPassphrase")); break;
                case "ok":
                    _missingModels = result.Missing ?? [];
                    CloudSay(Loc.Format("AccountRestored", (result.Profile?.Models.Count ?? 0), _missingModels.Count));
                    foreach (var m in _missingModels)
                        MissingModelsList.Children.Add(new TextBlock { Style = (Style)FindResource("HintText"), Text = $"\u00B7 {m.Name} ({m.Repo})" });
                    DownloadMissingButton.Visibility = _missingModels.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
                    Loc.Use(AppSettings.Current.Language ?? Loc.Language);
                    ApplyTexts();
                    (Owner as MainWindow)?.ModelChanged();
                    break;
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { CloudSay(Loc.Format("AccountFailed", ex.Message)); }
        finally { CloudBusy.Stop(); }
    }

    private void OnDownloadMissing(object sender, RoutedEventArgs e)
    {
        var n = CloudSync.DownloadMissing(_missingModels);
        CloudSay(Loc.Format("AccountDownloading", n));
        DownloadMissingButton.Visibility = Visibility.Collapsed;
        (Owner as MainWindow)?.ModelChanged();
    }

    private void CloudSay(string text)
    {
        CloudStatus.Text = text;
        CloudStatus.Visibility = Visibility.Visible;
    }

    // ------------------------------------------------------------------ pesta\u00F1as y buscador de ajustes

    private readonly List<(string Terms, System.Windows.Controls.TabItem Tab, FrameworkElement Anchor)> _settingsIndex = new();

    private void PaintTabs()
    {
        TabLocal.Header = Loc.Get("TabLocalModels");
        TabOnline.Header = Loc.Get("TabOnlineModels");
        TabFrameworks.Header = Loc.Get("TabFrameworks");
        TabSkills.Header = Loc.Get("TabSkills");
        TabRag.Header = Loc.Get("TabRag");
        TabConfig.Header = Loc.Get("TabConfig");
        TabMore.Header = Loc.Get("TabMore");
        SettingsSearchBox.ToolTip = Loc.Get("SettingsSearchPlaceholder");
        SettingsSearchButton.ToolTip = Loc.Get("SettingsSearchDo");

        _settingsIndex.Clear();
        void Add(TextBlock title, System.Windows.Controls.TabItem tab, FrameworkElement anchor, string extra = "")
            => _settingsIndex.Add(((title.Text + " " + extra).ToLowerInvariant(), tab, anchor));
        Add(ModelTitle, TabLocal, CardLocalModels, "modelo local gguf llama");
        Add(TeachTitle, TabLocal, TeachCard, "fine-tuning entrenar");
        Add(OnlineTitle, TabOnline, OnlineCard, "api openai groq mistral clave key");
        Add(FrameworksTitle, TabFrameworks, FrameworksCard, "rea node python uv");
        Add(McpTitle, TabFrameworks, McpCard, "mcp servidor server");
        Add(SkillsTitle, TabSkills, SkillsCard, "habilidad skill");
        Add(DocsTitle, TabRag, CardDocs, "rag conocimiento documentos knowledge");
        Add(InstructionsTitle, TabConfig, CardInstructions, "instrucciones internet letra font");
        Add(DoorTitle, TabConfig, CardDoor, "vs code editor puerta");
        Add(ImagesTitle, TabConfig, CardImages, "imagen image");
        Add(TasksTitle, TabConfig, CardTasks, "tarea programada schedule");
        Add(MemoryTitle, TabConfig, CardMemory, "memoria memory");
        Add(WindowsTitle, TabConfig, CardWindows, "bandeja arranque tray startup");
        Add(AccountTitle, TabMore, CardAccount, "cuenta sincronizacion sync nube cloud google microsoft account");
        Add(LimitsTitle, TabMore, CardContext, "contexto context tokens respuesta");
        Add(WorkTitle, TabMore, CardWork, "permisos permissions agente trabajo");
        Add(MlflowTitle, TabMore, MlflowCard, "mlflow trazas");
        Add(LanguageTitle, TabMore, CardLanguage, "idioma language");
        Add(DiagnosticsTitle, TabMore, CardDiagnostics, "diagnostico registro logs");
    }

    private void OnSettingsSearch(object sender, RoutedEventArgs e) => JumpToSetting(SettingsSearchBox.Text);

    private void OnSettingsSearchKey(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == System.Windows.Input.Key.Enter) { JumpToSetting(SettingsSearchBox.Text); e.Handled = true; }
    }

    /// <summary>Busca el ajuste por su texto, salta a su pesta\u00F1a y lo trae a la vista.</summary>
    private void JumpToSetting(string query)
    {
        query = (query ?? string.Empty).Trim().ToLowerInvariant();
        if (query.Length == 0) return;
        var hit = _settingsIndex.FirstOrDefault(entry => entry.Terms.Contains(query));
        if (hit.Anchor is null) return;
        Tabs.SelectedItem = hit.Tab;
        // La pesta\u00F1a se conecta al \u00E1rbol visual al seleccionarla; se desplaza en la siguiente vuelta.
        Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Background, () => { try { hit.Anchor.BringIntoView(); } catch (Exception) { } });
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
        if (query.Length == 0 && CurrentFilters() == new HuggingFace.Filters()) return;   // sin texto ni filtros no hay nada que buscar
        _search?.Cancel();
        _search = new CancellationTokenSource();
        var token = _search.Token;
        SearchButton.IsEnabled = false;
        SearchHint.Text = string.Empty;
        SearchList.Children.Clear();
        SearchBusy.Start(Loc.Get("SearchHfSearching"));
        try
        {
            _hits = await HuggingFace.SearchAsync(query, _pc ??= PcProfile.Detect(App.Engine.Accelerator), CurrentFilters(), token);
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
        if (query.StartsWith('*')) { FormatCombo.SelectedIndex = 2; query = query[1..]; }
        if (query.StartsWith('+')) { FormatCombo.SelectedIndex = 1; query = query[1..]; }
        SearchBox.Text = query;
        await SearchAsync();
        foreach (var h in _hits ?? [])
            EngineHost.Log($"HF {(h.Runnable ? ModelCatalog.FitOf(h.Model, _pc!).ToString() : "no:" + h.Kind)} {h.Model.Name} | {h.Model.Repo} | {h.FileName} | {Human(h.Model.ApproxBytes)} | {h.Model.License} | {h.Downloads} desc" + (h.Gated ? " | gated" : string.Empty));
        // Despues de pintar (si no, el desplazamiento se calcula con la lista vacia).
        _ = Dispatcher.BeginInvoke(() => SearchLabel.BringIntoView(new Rect(0, 0, 1, 600)), System.Windows.Threading.DispatcherPriority.ContextIdle);
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
            if (hit.Convertible)
            {
                SearchList.Children.Add(ConvertibleRow(hit, stats));
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

    // ------------------------------------------------------------------ filtros del buscador

    private sealed record Choice(string Label, object? Value)
    {
        public override string ToString() => Label;
    }

    private static readonly (string Key, string? Kind)[] KindChoices =
    [
        ("FilterKindAny", null), ("FilterKindText", "text-generation"), ("FilterKindVision", "image-text-to-text"),
        ("FilterKindImage", "text-to-image"), ("FilterKindSpeech", "text-to-speech"), ("FilterKindTranscribe", "automatic-speech-recognition"),
        ("FilterKindTranslate", "translation"), ("FilterKindEmbeddings", "feature-extraction"),
    ];

    private static readonly double?[] SizeChoices = [null, 1, 2, 4, 8, 14, 32, 70];

    private static readonly (string Key, string Sort)[] SortChoices =
        [("FilterSortDownloads", "downloads"), ("FilterSortLikes", "likes"), ("FilterSortTrending", "trendingScore"), ("FilterSortRecent", "lastModified")];

    /// <summary>Rellena los desplegables en el idioma activo sin perder lo elegido.</summary>
    private void PaintFilters()
    {
        static void Fill(ComboBox combo, IEnumerable<Choice> items, int fallback)
        {
            var index = combo.SelectedIndex < 0 ? fallback : combo.SelectedIndex;
            combo.ItemsSource = items.ToList();
            combo.SelectedIndex = index;
        }
        KindLabel.Text = Loc.Get("FilterKind");
        SizeLabel.Text = Loc.Get("FilterSize");
        FormatLabel.Text = Loc.Get("FilterFormat");
        SortLabel.Text = Loc.Get("FilterSort");
        CommercialCheck.Content = Loc.Get("FilterCommercial");
        CommercialCheck.ToolTip = Loc.Get("FilterCommercialTip");
        Fill(KindCombo, KindChoices.Select(k => new Choice(Loc.Get(k.Key), k.Kind)), 0);
        Fill(SizeCombo, SizeChoices.Select(v => new Choice(v is { } b ? Loc.Format("FilterSizeMax", b) : Loc.Get("FilterSizeAny"), v)), 0);
        Fill(FormatCombo, new[] { "FilterFormatGguf", "FilterFormatConvertible", "FilterFormatAll" }.Select((k, i) => new Choice(Loc.Get(k), (HuggingFace.Formats)i)), 0);
        Fill(SortCombo, SortChoices.Select(s => new Choice(Loc.Get(s.Key), s.Sort)), 0);
        FormatCombo.ToolTip = Loc.Get("FilterFormatTip");
    }

    private HuggingFace.Filters CurrentFilters() => new(
        (KindCombo.SelectedItem as Choice)?.Value as string,
        (SizeCombo.SelectedItem as Choice)?.Value as double?,
        (FormatCombo.SelectedItem as Choice)?.Value is HuggingFace.Formats f ? f : HuggingFace.Formats.Gguf,
        CommercialCheck.IsChecked == true,
        (SortCombo.SelectedItem as Choice)?.Value as string ?? "downloads");

    /// <summary>Un modelo de texto en safetensors: se puede convertir a GGUF e instalar (con el entorno de Enseñar a Lucia).</summary>
    private UIElement ConvertibleRow(HuggingFace.Hit hit, string stats)
    {
        var grid = new Grid { Margin = new Thickness(0, 4, 0, 4) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var text = new StackPanel();
        text.Children.Add(new TextBlock { Style = (Style)FindResource("BodyText"), FontWeight = FontWeights.SemiBold, Text = hit.Model.Name });
        text.Children.Add(new TextBlock { Style = (Style)FindResource("HintText"), Text = $"{hit.Model.Repo} · {stats}", TextTrimming = TextTrimming.CharacterEllipsis });
        // Lo que ocupara en GGUF Q4_K_M (~0,6 bytes por parametro) y como ira en este PC.
        var gguf = hit.Model;   // ApproxBytes ya es lo que ocupara en GGUF Q4_K_M
        var fit = ModelCatalog.FitOf(gguf, _pc!);
        text.Children.Add(new TextBlock { Style = (Style)FindResource("HintText"), Text = Loc.Format("SearchConvertible", $"{hit.Parameters / 1e9:0.#}", Human(gguf.ApproxBytes), hit.Model.License), TextWrapping = TextWrapping.Wrap });
        text.Children.Add(new TextBlock { Style = (Style)FindResource("HintText"), Text = fit switch
        {
            ModelFit.Optimal => Loc.Get(_pc!.HasUsableGpu ? "FitOptimalGpu" : "FitOptimalCpu"),
            ModelFit.Ok => Loc.Get(_pc!.HasUsableGpu ? "FitOkGpu" : "FitOkCpu"),
            ModelFit.TooBig => Loc.Get("FitTooBig"),
            _ => Loc.Get("FitSlow"),
        }, Foreground = (Brush)FindResource(fit is ModelFit.Slow or ModelFit.TooBig ? "Danger" : fit == ModelFit.Optimal ? "Success" : "TextSecondary"), TextWrapping = TextWrapping.Wrap });
        grid.Children.Add(text);
        var open = new Button { Style = (Style)FindResource("GhostIconButton"), Content = "\uE774", ToolTip = Loc.Get("SearchOpenHf"), VerticalAlignment = VerticalAlignment.Center };
        var url = hit.Url;
        open.Click += (_, _) => Open(url);
        Grid.SetColumn(open, 1);
        grid.Children.Add(open);
        var convert = new Button { Style = (Style)FindResource("IconButton"), Content = "\uE8AB", ToolTip = Loc.Get("SearchConvert"), IsEnabled = fit != ModelFit.TooBig && _convert is null, VerticalAlignment = VerticalAlignment.Center };
        convert.Click += async (_, _) => await ConvertAsync(hit);
        Grid.SetColumn(convert, 2);
        grid.Children.Add(convert);
        return grid;
    }

    // ------------------------------------------------------------------ Enseñar a Lucia y convertir modelos

    private CancellationTokenSource? _convert;

    // ------------------------------------------------------------------ MCP (herramientas externas)

    private System.Threading.CancellationTokenSource? _mcp;

    private void PaintMcp()
    {
        McpTitle.Text = Loc.Get("McpTitle");
        McpHint.Text = Loc.Get("McpHint");
        McpAddLabel.Text = Loc.Get("McpAddLabel");
        McpNameBox.Text = McpNameBox.Text;   // se conserva
        if (string.IsNullOrEmpty(McpNameBox.Tag as string)) { McpNameBox.ToolTip = Loc.Get("McpNamePlaceholder"); McpCommandBox.ToolTip = Loc.Get("McpCommandPlaceholder"); }
        McpAddButton.ToolTip = Loc.Get("McpAdd");
        McpExamples.Text = Loc.Get("McpExamples");
        McpLogButton.Content = IconLabel("", Loc.Get("McpOpenLog"));
        var servers = AppSettings.Current.McpServers;
        McpList.Children.Clear();
        if (servers.Count == 0)
            McpList.Children.Add(new TextBlock { Style = (Style)FindResource("HintText"), Text = Loc.Get("McpNoServers") });
        foreach (var server in servers.ToList())
            McpList.Children.Add(McpRow(server));
    }

    private UIElement McpRow(Mcp.McpServerConfig server)
    {
        var grid = new Grid { Margin = new Thickness(0, 4, 0, 4) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var text = new StackPanel();
        text.Children.Add(new TextBlock { Style = (Style)FindResource("BodyText"), FontWeight = FontWeights.SemiBold, Text = server.Name });
        text.Children.Add(new TextBlock { Style = (Style)FindResource("HintText"), Text = server.CommandLine, FontFamily = new FontFamily("Cascadia Mono, Consolas"), FontSize = 11, TextTrimming = TextTrimming.CharacterEllipsis });
        var conn = Mcp.McpManager.ConnectionOf(server.Name);
        if (conn is not null)
            text.Children.Add(new TextBlock { Style = (Style)FindResource("HintText"), Text = Loc.Format("McpConnected", conn.Tools.Count) + ": " + string.Join(", ", conn.Tools.Select(t => t.Name)), Foreground = (Brush)FindResource("Success"), TextWrapping = TextWrapping.Wrap });
        else if (server.Enabled)
            text.Children.Add(new TextBlock { Style = (Style)FindResource("HintText"), Text = Loc.Get("McpNotConnected"), Foreground = (Brush)FindResource("Danger") });
        grid.Children.Add(text);
        var on = new CheckBox { Style = (Style)FindResource("Check"), Content = Loc.Get("McpEnabled"), IsChecked = server.Enabled, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 8, 0) };
        on.Click += async (_, _) => { server.Enabled = on.IsChecked == true; AppSettings.Current.Save(); if (!server.Enabled) Mcp.McpManager.Disconnect(server.Name); await ConnectMcpAsync(); };
        Grid.SetColumn(on, 1);
        grid.Children.Add(on);
        var delete = new Button { Style = (Style)FindResource("DangerIconButton"), Content = "", ToolTip = Loc.Get("McpDelete"), VerticalAlignment = VerticalAlignment.Center };
        delete.Click += (_, _) =>
        {
            if (!PromptWindow.Confirm(this, Loc.Get("McpDelete"), Loc.Format("McpDeleteConfirm", server.Name))) return;
            AppSettings.Current.McpServers.RemoveAll(x => x.Name == server.Name);
            AppSettings.Current.Save();
            Mcp.McpManager.Disconnect(server.Name);
            PaintMcp();
        };
        Grid.SetColumn(delete, 2);
        grid.Children.Add(delete);
        return grid;
    }

    private async void OnAddMcp(object sender, RoutedEventArgs e)
    {
        var name = McpNameBox.Text.Trim();
        var command = McpCommandBox.Text.Trim();
        if (name.Length == 0 || command.Length == 0) { McpStatus.Text = Loc.Get("McpNeedNameCommand"); McpStatus.Visibility = Visibility.Visible; return; }
        var (cmd, args) = Mcp.McpServerConfig.ParseCommandLine(command);
        var config = new Mcp.McpServerConfig { Name = name, Command = cmd, Args = args, Enabled = true };
        McpStatus.Visibility = Visibility.Collapsed;
        McpBusy.Start(Loc.Format("McpConnecting", name));
        McpAddButton.IsEnabled = false;
        _mcp?.Cancel();
        _mcp = new System.Threading.CancellationTokenSource();
        try
        {
            var (info, tools) = await Mcp.McpManager.TestAsync(config, _mcp.Token);
            AppSettings.Current.McpServers.RemoveAll(x => string.Equals(x.Name, name, StringComparison.OrdinalIgnoreCase));
            AppSettings.Current.McpServers.Add(config);
            AppSettings.Current.Save();
            await Mcp.McpManager.EnsureAsync(AppSettings.Current, System.Threading.CancellationToken.None);
            McpNameBox.Clear();
            McpCommandBox.Clear();
            McpStatus.Text = Loc.Format("McpAddOk", info.Name, tools.Count);
            McpStatus.Visibility = Visibility.Visible;
            (Owner as MainWindow)?.ModelChanged();
        }
        catch (Exception ex)
        {
            McpStatus.Text = Loc.Format("McpAddFail", ex.Message);
            McpStatus.Visibility = Visibility.Visible;
        }
        finally
        {
            McpBusy.Stop();
            McpAddButton.IsEnabled = true;
            PaintMcp();
        }
    }

    private async Task ConnectMcpAsync()
    {
        McpBusy.Start(Loc.Get("McpConnectingAll"));
        try { await Mcp.McpManager.EnsureAsync(AppSettings.Current, System.Threading.CancellationToken.None); }
        catch (Exception) { }
        finally { McpBusy.Stop(); PaintMcp(); }
    }

    private void OnOpenMcpLog(object sender, RoutedEventArgs e)
    {
        if (File.Exists(Mcp.McpClient.LogPath)) Desktop.Current.Open(Mcp.McpClient.LogPath);
        else PromptWindow.Alert(this, Loc.Get("McpTitle"), Loc.Get("McpNoLog"));
    }

    // ------------------------------------------------------------------ Habilidades (skills)

    private System.Threading.CancellationTokenSource? _skill;

    private void PaintSkills()
    {
        SkillsTitle.Text = Loc.Get("SkillsTitle");
        SkillsHint.Text = Loc.Get("SkillsHint");
        SkillsAddLabel.Text = Loc.Get("SkillsAddLabel");
        SkillSourceBox.ToolTip = Loc.Get("SkillsAddPlaceholder");
        SkillAddButton.ToolTip = Loc.Get("SkillsAdd");
        SkillBrowseButton.ToolTip = Loc.Get("SkillsBrowse");
        SkillsList.Children.Clear();
        var skills = SkillStore.All();
        if (skills.Count == 0)
            SkillsList.Children.Add(new TextBlock { Style = (Style)FindResource("HintText"), Text = Loc.Get("SkillsNone") });
        foreach (var skill in skills)
            SkillsList.Children.Add(SkillRow(skill));
    }

    private UIElement SkillRow(Skill skill)
    {
        var grid = new Grid { Margin = new Thickness(0, 4, 0, 4) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var text = new StackPanel();
        text.Children.Add(new TextBlock { Style = (Style)FindResource("BodyText"), FontWeight = FontWeights.SemiBold, Text = skill.Name });
        text.Children.Add(new TextBlock { Style = (Style)FindResource("HintText"), Text = skill.Description, TextWrapping = TextWrapping.Wrap });
        grid.Children.Add(text);

        var on = new CheckBox { Style = (Style)FindResource("Check"), Content = Loc.Get("McpEnabled"), IsChecked = skill.Enabled, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 8, 0) };
        on.Click += (_, _) => { SkillStore.SetEnabled(skill.Name, on.IsChecked == true); };
        Grid.SetColumn(on, 1);
        grid.Children.Add(on);

        var view = new Button { Style = (Style)FindResource("GhostIconButton"), Content = "", ToolTip = Loc.Get("SkillView"), VerticalAlignment = VerticalAlignment.Center };
        view.Click += (_, _) => { if (Directory.Exists(skill.Folder)) Desktop.Current.Open(skill.Folder); };
        Grid.SetColumn(view, 2);
        grid.Children.Add(view);

        var delete = new Button { Style = (Style)FindResource("DangerIconButton"), Content = "", ToolTip = Loc.Get("SkillDelete"), VerticalAlignment = VerticalAlignment.Center };
        delete.Click += (_, _) =>
        {
            if (!PromptWindow.Confirm(this, Loc.Get("SkillDelete"), Loc.Format("SkillDeleteConfirm", skill.Name))) return;
            SkillStore.Remove(skill.Name);
            PaintSkills();
        };
        Grid.SetColumn(delete, 3);
        grid.Children.Add(delete);
        return grid;
    }

    private void OnBrowseSkill(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFolderDialog { Title = Loc.Get("SkillsBrowse") };
        if (dialog.ShowDialog(this) == true) SkillSourceBox.Text = dialog.FolderName;
    }

    private async void OnAddSkill(object sender, RoutedEventArgs e)
    {
        var source = SkillSourceBox.Text.Trim();
        if (source.Length == 0) return;
        SkillStatus.Visibility = Visibility.Collapsed;
        SkillBusy.Start(Loc.Get("SkillAdding"));
        SkillAddButton.IsEnabled = false;
        _skill?.Cancel();
        _skill = new System.Threading.CancellationTokenSource();
        try
        {
            var (skill, message) = await SkillStore.InstallAsync(source, _skill.Token);
            if (skill is not null) SkillSourceBox.Clear();
            SkillStatus.Text = message;
            SkillStatus.Visibility = Visibility.Visible;
        }
        catch (Exception ex)
        {
            SkillStatus.Text = ex.Message;
            SkillStatus.Visibility = Visibility.Visible;
        }
        finally
        {
            SkillBusy.Stop();
            SkillAddButton.IsEnabled = true;
            PaintSkills();
        }
    }

    // ------------------------------------------------------------------ Frameworks para IA

    private System.Threading.CancellationTokenSource? _framework;

    private void PaintFrameworks()
    {
        FrameworksTitle.Text = Loc.Get("FrameworksTitle");
        FrameworksHint.Text = Loc.Get("FrameworksHint");
        FrameworksList.Children.Clear();
        foreach (var framework in Frameworks.Catalog)
            FrameworksList.Children.Add(FrameworkRow(framework));
    }

    private UIElement FrameworkRow(LlmFramework framework)
    {
        var grid = new Grid { Margin = new Thickness(0, 4, 0, 4) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var installed = AppSettings.Current.McpServers.Any(s => string.Equals(s.Name, framework.McpName, StringComparison.OrdinalIgnoreCase));
        var text = new StackPanel();
        text.Children.Add(new TextBlock { Style = (Style)FindResource("BodyText"), FontWeight = FontWeights.SemiBold, Text = framework.Name });
        text.Children.Add(new TextBlock { Style = (Style)FindResource("HintText"), Text = framework.Description, TextWrapping = TextWrapping.Wrap });
        text.Children.Add(new TextBlock { Style = (Style)FindResource("HintText"), Text = $"{framework.License} · {Frameworks.Binary(framework.Runtime)}" + (installed ? " · " + Loc.Get("FrameworkInstalled") : string.Empty), Foreground = installed ? (Brush)FindResource("Success") : (Brush)FindResource("TextSecondary") });
        grid.Children.Add(text);

        var open = new Button { Style = (Style)FindResource("GhostIconButton"), Content = "", ToolTip = Loc.Get("FrameworkOpen"), VerticalAlignment = VerticalAlignment.Center };
        open.Click += (_, _) => Desktop.Current.Open(framework.Url);
        Grid.SetColumn(open, 1);
        grid.Children.Add(open);

        var install = new Button { Style = (Style)FindResource("OutlineButton"), Content = IconLabel("", Loc.Get("FrameworkInstall")), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 0, 0) };
        install.Click += (_, _) => InstallFrameworkAsync(framework, install);
        Grid.SetColumn(install, 2);
        grid.Children.Add(install);
        return grid;
    }

    private async void InstallFrameworkAsync(LlmFramework framework, System.Windows.Controls.Primitives.ButtonBase button)
    {
        FrameworkStatus.Visibility = Visibility.Collapsed;
        FrameworkBusy.Start(Loc.Format("FrameworkInstalling", framework.Name));
        button.IsEnabled = false;
        _framework?.Cancel();
        _framework = new System.Threading.CancellationTokenSource();
        try
        {
            var message = await Frameworks.InstallAsync(framework.Id, _framework.Token);
            FrameworkStatus.Text = message;
            FrameworkStatus.Visibility = Visibility.Visible;
            (Owner as MainWindow)?.ModelChanged();
        }
        catch (Exception ex)
        {
            FrameworkStatus.Text = ex.Message;
            FrameworkStatus.Visibility = Visibility.Visible;
        }
        finally
        {
            FrameworkBusy.Stop();
            button.IsEnabled = true;
            PaintSkills();
            PaintFrameworks();
        }
    }

    // ------------------------------------------------------------------ Modelos en línea (APIs compatibles con OpenAI)

    private System.Threading.CancellationTokenSource? _online;

    private static string SignupText(RemoteSignup signup) => signup switch
    {
        RemoteSignup.NoCard => Loc.Get("SignupNoCard"),
        RemoteSignup.Registration => Loc.Get("SignupRegistration"),
        RemoteSignup.Phone => Loc.Get("SignupPhone"),
        _ => Loc.Get("SignupCard"),
    };

    private void PaintOnline()
    {
        var s = AppSettings.Current;
        OnlineTitle.Text = Loc.Get("OnlineTitle");
        OnlineHint.Text = Loc.Get("OnlineHint");
        OnlineOptIn.Content = Loc.Get("OnlineOptIn");
        OnlineOptIn.IsChecked = s.RemoteOptIn;
        OnlineModelsLabel.Text = Loc.Get("OnlineModelsLabel");
        OnlineProvidersLabel.Text = Loc.Get("OnlineProvidersLabel");

        OnlineModelsList.Children.Clear();
        if (s.RemoteModels.Count == 0)
            OnlineModelsList.Children.Add(new TextBlock { Style = (Style)FindResource("HintText"), Text = Loc.Get("OnlineNoModels") });
        foreach (var model in s.RemoteModels.ToList())
            OnlineModelsList.Children.Add(OnlineModelRow(model));

        OnlineProvidersList.Children.Clear();
        foreach (var provider in RemoteModels.Catalog)
            OnlineProvidersList.Children.Add(ProviderRow(provider));
    }

    private UIElement OnlineModelRow(RemoteModelRef model)
    {
        var s = AppSettings.Current;
        var grid = new Grid { Margin = new Thickness(0, 4, 0, 4) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var provider = RemoteModels.Provider(model.ProviderId);
        var text = new StackPanel();
        text.Children.Add(new TextBlock { Style = (Style)FindResource("BodyText"), FontWeight = FontWeights.SemiBold, Text = model.Display });
        text.Children.Add(new TextBlock { Style = (Style)FindResource("HintText"), Text = $"{provider?.Name ?? model.ProviderId} · {model.ModelId}" });
        grid.Children.Add(text);

        var active = s.ActiveRemoteModel is { } a && a.ProviderId == model.ProviderId && a.ModelId == model.ModelId;
        var use = new CheckBox { Style = (Style)FindResource("Check"), Content = Loc.Get("OnlineUse"), IsChecked = active, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 8, 0) };
        use.Click += (_, _) =>
        {
            if (use.IsChecked == true)
            {
                if (!s.RemoteOptIn) { use.IsChecked = false; OnlineSay(Loc.Get("OnlineNeedOptIn")); return; }
                if (!RemoteModels.HasKey(model.ProviderId)) { use.IsChecked = false; OnlineSay(Loc.Get("OnlineNeedKey")); return; }
                s.ActiveRemoteModel = model;
            }
            else s.ActiveRemoteModel = null;
            s.Save();
            (Owner as MainWindow)?.ModelChanged();
            PaintOnline();
        };
        Grid.SetColumn(use, 1);
        grid.Children.Add(use);

        var delete = new Button { Style = (Style)FindResource("DangerIconButton"), Content = "", ToolTip = Loc.Get("OnlineRemove"), VerticalAlignment = VerticalAlignment.Center };
        delete.Click += (_, _) =>
        {
            if (!PromptWindow.Confirm(this, Loc.Get("OnlineRemove"), Loc.Format("OnlineRemoveConfirm", model.Display))) return;
            s.RemoteModels.RemoveAll(x => x.ProviderId == model.ProviderId && x.ModelId == model.ModelId);
            if (active) s.ActiveRemoteModel = null;
            s.Save();
            if (active) (Owner as MainWindow)?.ModelChanged();
            PaintOnline();
        };
        Grid.SetColumn(delete, 2);
        grid.Children.Add(delete);
        return grid;
    }

    private UIElement ProviderRow(RemoteProvider provider)
    {
        var panel = new StackPanel { Margin = new Thickness(0, 6, 0, 10) };
        panel.Children.Add(new TextBlock { Style = (Style)FindResource("BodyText"), FontWeight = FontWeights.SemiBold, Text = provider.Name });
        panel.Children.Add(new TextBlock { Style = (Style)FindResource("HintText"), Text = $"{SignupText(provider.Signup)} · {Loc.Get("OnlineCaveat")}", TextWrapping = TextWrapping.Wrap });

        // Clave: botón para abrir la página de la clave + campo (cifrado) que se guarda al salir.
        var keyRow = new DockPanel { Margin = new Thickness(0, 4, 0, 0) };
        var getKey = new Button { Style = (Style)FindResource("GhostIconButton"), Content = "", ToolTip = Loc.Get("OnlineGetKey") };
        getKey.Click += (_, _) => Desktop.Current.Open(provider.KeyUrl);
        DockPanel.SetDock(getKey, Dock.Left);
        keyRow.Children.Add(getKey);
        var key = new Controls.RevealPasswordBox { Password = RemoteModels.Key(provider.Id), Margin = new Thickness(6, 0, 0, 0) };
        key.Left += (_, _) => RemoteModels.SetKey(provider.Id, key.Password);
        keyRow.Children.Add(key);
        panel.Children.Add(keyRow);

        if (provider.NeedsAccountId)
        {
            var account = new TextBox { Style = (Style)FindResource("Field"), Text = RemoteModels.AccountId(provider.Id), ToolTip = Loc.Get("OnlineAccountId"), Margin = new Thickness(0, 6, 0, 0) };
            account.LostFocus += (_, _) => RemoteModels.SetAccountId(provider.Id, account.Text);
            panel.Children.Add(account);
        }

        // Modelo: combo editable (se llena con «ver modelos» o se escribe el id) + añadir + probar.
        var modelBox = new ComboBox { IsEditable = true, Margin = new Thickness(0, 6, 0, 0), ToolTip = Loc.Get("OnlineModelId") };
        var row = new DockPanel { Margin = new Thickness(0, 6, 0, 0) };
        var add = new Button { Style = (Style)FindResource("IconButton"), Content = "", ToolTip = Loc.Get("OnlineAddModel") };
        add.Click += (_, _) =>
        {
            var id = (modelBox.Text ?? string.Empty).Trim();
            if (id.Length == 0) { OnlineSay(Loc.Get("OnlineNeedModelId")); return; }
            var s = AppSettings.Current;
            if (!s.RemoteModels.Any(m => m.ProviderId == provider.Id && m.ModelId == id))
                s.RemoteModels.Add(new RemoteModelRef { ProviderId = provider.Id, ModelId = id, Name = id });
            s.Save();
            PaintOnline();
        };
        DockPanel.SetDock(add, Dock.Right);
        row.Children.Add(add);
        var fetch = new Button { Style = (Style)FindResource("GhostIconButton"), Content = "", ToolTip = Loc.Get("OnlineFetchModels"), Margin = new Thickness(6, 0, 0, 0) };
        DockPanel.SetDock(fetch, Dock.Right);
        var test = new Button { Style = (Style)FindResource("OutlineButton"), Content = IconLabel("", Loc.Get("OnlineTest")), Margin = new Thickness(6, 0, 0, 0) };
        DockPanel.SetDock(test, Dock.Right);
        fetch.Click += async (_, _) => await FetchOnlineModelsAsync(provider, modelBox, fetch);
        test.Click += async (_, _) => await TestOnlineAsync(provider, (modelBox.Text ?? string.Empty).Trim(), test);
        row.Children.Add(fetch);
        row.Children.Add(test);
        row.Children.Add(modelBox);
        panel.Children.Add(row);
        return panel;
    }

    private async Task FetchOnlineModelsAsync(RemoteProvider provider, ComboBox box, System.Windows.Controls.Primitives.ButtonBase button)
    {
        _online?.Cancel();
        _online = new System.Threading.CancellationTokenSource();
        await RunBusyAsync(OnlineBusy, Loc.Get("OnlineFetching"),
            () => RemoteModels.ModelsAsync(provider, _online.Token).GetAwaiter().GetResult(),
            ids =>
            {
                box.ItemsSource = ids;
                OnlineSay(ids.Count == 0 ? Loc.Get("OnlineNoModelList") : Loc.Format("OnlineModelsFound", ids.Count));
            }, button);
    }

    private async Task TestOnlineAsync(RemoteProvider provider, string modelId, System.Windows.Controls.Primitives.ButtonBase button)
    {
        _online?.Cancel();
        _online = new System.Threading.CancellationTokenSource();
        await RunBusyAsync(OnlineBusy, Loc.Get("OnlineTesting"),
            () => RemoteModels.TestAsync(provider, modelId, _online.Token).GetAwaiter().GetResult(),
            result => OnlineSay(Loc.Get(result.Kind switch
            {
                "ok" => "OnlineTestOk",
                "auth" => "OnlineTestAuth",
                "limit" => "OnlineTestLimit",
                "model" => "OnlineTestModel",
                "account" => "OnlineTestAccount",
                _ => "OnlineTestNetwork",
            })), button);
    }

    private void OnlineSay(string text)
    {
        OnlineStatus.Text = text;
        OnlineStatus.Visibility = Visibility.Visible;
    }

    private void OnOnlineOptIn(object sender, RoutedEventArgs e)
    {
        var s = AppSettings.Current;
        s.RemoteOptIn = OnlineOptIn.IsChecked == true;
        if (!s.RemoteOptIn && s.ActiveRemoteModel is not null) { s.ActiveRemoteModel = null; (Owner as MainWindow)?.ModelChanged(); }
        s.Save();
        PaintOnline();
    }

    private void OnTeach(object sender, RoutedEventArgs e)
    {
        new FineTuneWindow { Owner = this }.ShowDialog();
        PaintTeach();
    }

    /// <summary>Los adaptadores entrenados (en uso o no) y lo que ocupa en disco lo de entrenar.</summary>
    private void PaintTeach()
    {
        TeachTitle.Text = Loc.Get("TeachTitle");
        TeachHint.Text = Loc.Get("TeachHint");
        TeachButton.Content = IconLabel("\uE82D", Loc.Get("TeachOpen"));
        AdaptersLabel.Text = Loc.Get("TeachAdapters");
        RemoveEnvButton.Content = IconLabel("\uE74D", Loc.Get("TeachRemoveEnv"));
        RemoveBasesButton.Content = IconLabel("\uE74D", Loc.Get("TeachRemoveBases"));
        CancelConvertButton.ToolTip = Loc.Get("Cancel");
        var s = AppSettings.Current;
        AdapterList.Children.Clear();
        s.Adapters.RemoveAll(a => !File.Exists(a.Path));
        foreach (var adapter in s.Adapters.OrderByDescending(a => a.CreatedAt))
            AdapterList.Children.Add(AdapterRow(adapter));
        if (s.Adapters.Count == 0)
            AdapterList.Children.Add(new TextBlock { Style = (Style)FindResource("HintText"), Text = Loc.Get("TeachNoAdapters") });
        _ = RunBusyAsync(TeachBusy, Loc.Get("TeachMeasuring"),
            () => (env: FineTune.PythonEnv.SizeOnDisk() - FineTune.PythonEnv.FolderSize(FineTune.BaseModels.Folder) - FineTune.PythonEnv.FolderSize(FineTune.Adapters.Folder), bases: FineTune.PythonEnv.FolderSize(FineTune.BaseModels.Folder)),
            r =>
            {
                TeachDisk.Text = Loc.Format("TeachDisk", Human(Math.Max(0, r.env)), Human(r.bases));
                RemoveEnvButton.IsEnabled = r.env > 0 && _convert is null;
                RemoveBasesButton.IsEnabled = r.bases > 0 && _convert is null;
            });
    }

    private UIElement AdapterRow(FineTune.LoraAdapter adapter)
    {
        var grid = new Grid { Margin = new Thickness(0, 4, 0, 4) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var text = new StackPanel();
        text.Children.Add(new TextBlock { Style = (Style)FindResource("BodyText"), FontWeight = FontWeights.SemiBold, Text = adapter.Name });
        var model = AppSettings.Current.Installed.FirstOrDefault(i => string.Equals(i.Path, adapter.ModelPath, StringComparison.OrdinalIgnoreCase))?.Name ?? Path.GetFileName(adapter.ModelPath);
        text.Children.Add(new TextBlock { Style = (Style)FindResource("HintText"), Text = Loc.Format("TeachAdapterLine", model, adapter.Examples, adapter.CreatedAt.LocalDateTime.ToString("g")), TextTrimming = TextTrimming.CharacterEllipsis });
        grid.Children.Add(text);
        var use = new CheckBox { Style = (Style)FindResource("Check"), Content = Loc.Get("TeachInUse"), IsChecked = adapter.Active, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 8, 0) };
        use.Click += (_, _) =>
        {
            FineTune.Adapters.SetActive(AppSettings.Current, use.IsChecked == true ? adapter : null, adapter.ModelPath);
            PaintTeach();
            (Owner as MainWindow)?.ModelChanged();
        };
        Grid.SetColumn(use, 1);
        grid.Children.Add(use);
        var delete = new Button { Style = (Style)FindResource("DangerIconButton"), Content = "\uE74D", ToolTip = Loc.Get("TeachDeleteAdapter"), VerticalAlignment = VerticalAlignment.Center };
        delete.Click += (_, _) =>
        {
            if (!PromptWindow.Confirm(this, Loc.Get("TeachDeleteAdapter"), Loc.Format("TeachDeleteAdapterConfirm", adapter.Name))) return;
            var wasActive = adapter.Active;
            FineTune.Adapters.Delete(AppSettings.Current, adapter);
            PaintTeach();
            if (wasActive) (Owner as MainWindow)?.ModelChanged();
        };
        Grid.SetColumn(delete, 2);
        grid.Children.Add(delete);
        return grid;
    }

    private void OnRemoveEnv(object sender, RoutedEventArgs e)
    {
        if (!PromptWindow.Confirm(this, Loc.Get("TeachRemoveEnv"), Loc.Get("TeachRemoveEnvConfirm"))) return;
        try { FineTune.PythonEnv.Remove(); }
        catch (Exception ex) { PromptWindow.Alert(this, Loc.Get("Error"), ex.Message); }
        PaintTeach();
    }

    private void OnRemoveBases(object sender, RoutedEventArgs e)
    {
        if (!PromptWindow.Confirm(this, Loc.Get("TeachRemoveBases"), Loc.Get("TeachRemoveBasesConfirm"))) return;
        try { if (Directory.Exists(FineTune.BaseModels.Folder)) Directory.Delete(FineTune.BaseModels.Folder, recursive: true); }
        catch (Exception ex) { PromptWindow.Alert(this, Loc.Get("Error"), ex.Message); }
        PaintTeach();
    }

    private void OnCancelConvert(object sender, RoutedEventArgs e) => _convert?.Cancel();

    /// <summary>
    /// Convierte un modelo de Hugging Face en safetensors a GGUF y lo instala: monta el entorno si
    /// falta, baja los pesos (se guardan, sirven tambien para enseñarle), convierte, cuantiza y apunta su ficha.
    /// </summary>
    public async Task ConvertAsync(HuggingFace.Hit hit)
    {
        if (_convert is not null) return;
        var download = FineTune.BaseModels.IsDownloaded(hit.Model.Repo) ? 0 : hit.Parameters * 2;
        if (!PromptWindow.Confirm(this, Loc.Get("SearchConvert"), Loc.Format("SearchConvertConfirm", hit.Model.Name, Human(download),
                FineTune.PythonEnv.Installed ? Loc.Get("SearchConvertEnvReady") : Loc.Format("SearchConvertEnv", FineTune.PythonEnv.UseCuda ? "≈ 4 GB" : "≈ 1 GB"))))
            return;
        var cts = _convert = new CancellationTokenSource();
        ConvertBox.Visibility = Visibility.Visible;
        TeachCard.BringIntoView();
        RemoveEnvButton.IsEnabled = RemoveBasesButton.IsEnabled = false;
        void Status(string text) { ConvertText.Text = text; ConvertBar.IsIndeterminate = true; }
        var bytes = new Progress<Downloader.Progress>(p =>
        {
            ConvertBar.IsIndeterminate = p.Fraction is null;
            ConvertBar.Value = (p.Fraction ?? 0) * 100;
        });
        try
        {
            await FineTune.PythonEnv.EnsureAsync(new Progress<string>(Status), bytes, cts.Token);
            Status(Loc.Format("SearchConvertDownloading", hit.Model.Repo));
            var model = await FineTune.BaseModels.ResolveAsync(hit.Model.Repo, cts.Token)
                        ?? throw new InvalidOperationException(Loc.Get("SearchConvertNoFiles"));
            var folder = await FineTune.BaseModels.DownloadAsync(model, bytes, cts.Token);
            Status(Loc.Get("FtStageConverting"));
            var events = new Progress<FineTune.TrainEvent>(ev => { if (ev.Stage == "quantizing") Status(Loc.Get("FtStageQuantizing")); });
            var path = await FineTune.FineTuneRunner.ConvertAsync(folder, FineTune.Adapters.SafeName(hit.Model.Repo[(hit.Model.Repo.IndexOf('/') + 1)..]), events, cts.Token);
            var name = HuggingFace.NameFor(hit.Model.Repo);
            AppSettings.Current.Remember(new InstalledModel { Name = name, Path = path, License = hit.Model.License, Repo = hit.Model.Repo });
            AppSettings.Current.Save();
            DiagLog.Write("convertir", $"{hit.Model.Repo} convertido a {Path.GetFileName(path)}");
            ConvertText.Text = Loc.Format("SearchConverted", name);
            ConvertBar.IsIndeterminate = false;
            ConvertBar.Value = 100;
            PaintModel();
            (Owner as MainWindow)?.ModelChanged();
        }
        catch (OperationCanceledException)
        {
            ConvertBox.Visibility = Visibility.Collapsed;
        }
        catch (Exception ex)
        {
            DiagLog.Write("convertir", $"{hit.Model.Repo} no se ha podido convertir: " + DiagLog.Describe(ex));
            ConvertBox.Visibility = Visibility.Collapsed;
            PromptWindow.Alert(this, Loc.Get("Error"), Loc.Format("SearchConvertFailed", ex.Message));
        }
        finally
        {
            _convert = null;
            PaintTeach();
            if (_hits is not null) PaintSearch();
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
        // Lo que se sabe de por que no sirve: no es una IA (auxiliar, sin arquitectura) o ya fallo al arrancar.
        var problem = ModelCatalog.IsHelperFile(model.Path) ? Loc.Get("ModelHelperFile") : AppSettings.Current.BrokenModels.GetValueOrDefault(model.Path);
        if (problem is not null)
            text.Children.Add(new TextBlock { Style = (Style)FindResource("HintText"), Text = "⚠ " + problem, Foreground = (Brush)FindResource("Danger"), TextWrapping = TextWrapping.Wrap });
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
            IsEnabled = !ModelDownloads.IsDownloading(model.Repo ?? ModelCatalog.All.FirstOrDefault(m => m.Name == model.Name)?.Repo ?? string.Empty),
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
            button = new Button { Style = (Style)FindResource("DangerIconButton"), Content = "\uE74D", ToolTip = Loc.Get("ModelDelete"), IsEnabled = !ModelDownloads.IsDownloading(model.Repo), VerticalAlignment = VerticalAlignment.Center };
            button.Click += (_, _) =>
            {
                var file = ModelCatalog.InstalledFiles().FirstOrDefault(i => i.Name == model.Name);
                if (file is not null) DeleteModel(file);
            };
        }
        else
        {
            button = new Button { Style = (Style)FindResource("IconButton"), Content = "\uE896", ToolTip = fit == ModelFit.TooBig ? Loc.Get("FitTooBig") : ModelDownloads.IsDownloading(model.Repo) ? Loc.Get("ModelDownloadingNow") : Loc.Get("ModelInstall"), IsEnabled = fit != ModelFit.TooBig && !ModelDownloads.IsDownloading(model.Repo), VerticalAlignment = VerticalAlignment.Center };
            button.Click += (_, _) => { ModelDownloads.Start(model); PaintModel(); };
        }
        Grid.SetColumn(button, 1);
        grid.Children.Add(button);
        return grid;
    }

    /// <summary>Las descargas viven en ModelDownloads (siguen con esta ventana cerrada); aqui solo se pintan, una fila por descarga.</summary>
    private void PaintDownload()
    {
        var downloads = ModelDownloads.All;
        DownloadList.Children.Clear();
        foreach (var download in downloads)
        {
            var row = new StackPanel { Margin = new Thickness(0, 0, 0, 6) };
            row.Children.Add(new TextBlock { Style = (Style)FindResource("HintText"), Text = Loc.Format("ModelInstalling", download.Model.Name, download.Total is { } t ? $"{Human(download.Received)} / {Human(t)}" : Human(download.Received)) });
            var line = new DockPanel { Margin = new Thickness(0, 4, 0, 0) };
            var cancel = new Button { Style = (Style)FindResource("DangerIconButton"), Content = "", Width = 30, Height = 30, Margin = new Thickness(8, 0, 0, 0), ToolTip = Loc.Get("ModelCancel") };
            var repo = download.Model.Repo;
            cancel.Click += (_, _) => ModelDownloads.Cancel(repo);
            DockPanel.SetDock(cancel, Dock.Right);
            line.Children.Add(cancel);
            line.Children.Add(new ProgressBar { Height = 8, Foreground = (Brush)FindResource("Primary"), Background = (Brush)FindResource("Separator"), BorderThickness = new Thickness(0), IsIndeterminate = download.Fraction is null, Value = (download.Fraction ?? 0) * 100 });
            row.Children.Add(line);
            DownloadList.Children.Add(row);
        }
        if (downloads.Count > 0) DownloadText.Visibility = Visibility.Collapsed;
    }

    private void OnDownloadFinished(CatalogModel model, Exception? error)
    {
        PaintDownload();
        if (error is null)
        {
            DownloadText.Text = Loc.Format("ModelInstalledName", model.Name);
            DownloadText.Visibility = Visibility.Visible;
        }
        else if (error is not OperationCanceledException)
            PromptWindow.Alert(this, Loc.Get("Error"), error.Message);
        PaintModel();
    }

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
        if (ModelCatalog.Unusable(file) is { } why)
        {
            PromptWindow.Alert(this, Loc.Get("ModelImport"), Loc.Format("ModelNotUsable", Path.GetFileName(file), why));
            return;
        }
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
        AutoContextCheck.Content = Loc.Get("AutoContext");
        AutoContextCheck.IsChecked = s.AutoContext;
        AutoContextHint.Text = Loc.Get("AutoContextHint");
        ContextTokensLabel.Text = Loc.Get("ContextTokensLabel");
        ContextTokensBox.Text = s.ContextTokens.ToString(culture);
        ContextTokensBox.IsEnabled = !s.AutoContext;   // en automatico, la casilla a mano se apaga
        // El maximo/minimo dependen de la cabecera del GGUF (se lee del disco): fuera del hilo de la interfaz.
        _ = RunBusyAsync(LimitsBusy, Loc.Get("LimitsLoading"), () => TokenLimits.RangeFor(s), range =>
        {
            if (s.AutoContext)
            {
                ContextTokensBox.Text = range.Auto.ToString(culture);
                // «Automático: 8192, el máximo que cabe en tu gráfica» (o el que admite la IA si no se sabe).
                var why = range.Fast is not null && range.Auto == range.Fast ? Loc.Get("AutoContextFast")
                        : range.FromModel ? Loc.Get("AutoContextModelMax") : Loc.Get("AutoContextDefault");
                ContextTokensHint.Text = Loc.Format("AutoContextChosen", range.Auto, why) + " "
                    + Loc.Format("ContextRange", range.Min, range.Max, range.Trained is { } tr ? Loc.Format("ContextTrainedShort", tr) : Loc.Get("ContextUnknownModel"))
                    + " " + Loc.Get("ContextRestart");
            }
            else
            {
                var hint = Loc.Format("ContextTokensHint", range.Min, range.Max, TokenLimits.ContextDefault);
                if (range.Fast is { } fast) hint += " " + Loc.Format("ContextFast", fast);
                if (range.Trained is { } trained) hint += " " + Loc.Format("ContextTrained", trained);
                if (s.ContextTokens > range.Max) hint += " " + Loc.Format("ContextCapped", range.Max);
                ContextTokensHint.Text = hint + " " + Loc.Get("ContextRestart");
            }
        });
    }

    private void OnAutoContextChanged(object sender, RoutedEventArgs e)
    {
        var s = AppSettings.Current;
        s.AutoContext = AutoContextCheck.IsChecked == true;
        s.Save();
        // El motor se reinicia con el nuevo contexto en la siguiente pregunta (no se corta ahora).
        DiagLog.Write("ajustes", $"contexto automatico: {(s.AutoContext ? "si" : "no")} (en uso al reiniciar el motor: {TokenLimits.Context(s)})");
        PaintLimits();
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

    /// <summary>El tope de acciones del modo agente, o «sin limite».</summary>
    private void PaintAgentMax()
    {
        var s = AppSettings.Current;
        AgentMaxLabel.Text = Loc.Get("AgentMaxActions");
        var unlimited = s.AgentMaxActions == ToolLoopGuard.UnlimitedCalls;
        AgentMaxBox.Text = (unlimited ? ToolLoopGuard.DefaultMaxCalls : s.AgentMaxActions).ToString(System.Globalization.CultureInfo.InvariantCulture);
        AgentMaxBox.IsEnabled = !unlimited;
        AgentUnlimitedCheck.IsChecked = unlimited;
        AgentUnlimitedCheck.Content = Loc.Get("AgentUnlimited");
        AgentMaxHint.Text = Loc.Format("AgentMaxActionsHint", ToolLoopGuard.MinMaxCalls, ToolLoopGuard.MaxMaxCalls, ToolLoopGuard.DefaultMaxCalls)
                            + (unlimited ? " " + Loc.Get("AgentUnlimitedHint") : string.Empty);
    }

    /// <summary>Sin limite de acciones (0); al quitarlo vuelve el numero de la casilla.</summary>
    private void OnAgentUnlimitedChanged(object sender, RoutedEventArgs e)
    {
        var s = AppSettings.Current;
        var unlimited = AgentUnlimitedCheck.IsChecked == true;
        s.AgentMaxActions = unlimited ? ToolLoopGuard.UnlimitedCalls
            : TokenLimits.Parse(AgentMaxBox.Text, ToolLoopGuard.MinMaxCalls, ToolLoopGuard.MaxMaxCalls) ?? ToolLoopGuard.DefaultMaxCalls;
        s.Save();
        DiagLog.Write("ajustes", unlimited ? "acciones por respuesta: sin limite" : $"acciones como mucho por respuesta: {s.AgentMaxActions}");
        PaintAgentMax();
    }

    /// <summary>El tope de acciones del modo agente: al salir de la casilla o con Intro; si no vale, se dice y se deja el de antes.</summary>
    private void OnAgentMaxChanged(object sender, RoutedEventArgs e)
    {
        var s = AppSettings.Current;
        if (s.AgentMaxActions == ToolLoopGuard.UnlimitedCalls) return;
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
        if (TokenLimits.Parse(ContextTokensBox.Text, range.Min, range.Max) is not { } value)
        {
            PromptWindow.Alert(this, Loc.Get("ContextTokensLabel"), Loc.Format("NumberOutOfRange", ContextTokensBox.Text.Trim(), range.Min, range.Max));
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
