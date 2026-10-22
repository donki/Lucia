using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using SocLucia.Chat;
using SocLucia.Engine;
using SocLucia.FineTune;
using SocLucia.Localization;
using SocLucia.Services;

namespace SocLucia;

/// <summary>
/// Enseñar a Lucia: asistente para hacer fine-tuning sin saber de fine-tuning. Cinco pasos: la IA
/// que aprende (y si cabe en este PC), los ejemplos (de las conversaciones, de un fichero o
/// escritos a mano, revisados al momento), como entrenar (tres niveles con su tiempo estimado),
/// entrenar (instala lo que falte, baja la IA original y entrena con progreso) y listo (el
/// adaptador queda en uso con esa IA y se puede quitar cuando se quiera desde Ajustes).
/// </summary>
public partial class FineTuneWindow : Window
{
    private const int Steps = 5;
    private int _step = 1;
    private List<InstalledModel> _models = [];
    private InstalledModel? _model;
    private BaseModel? _base;
    private readonly List<TrainingExample> _examples = [];
    private List<TrainingExample> _kept = [];
    private TrainingReport? _report;
    private TrainingPreset _preset = BaseModels.Balanced;
    private PcProfile? _pc;
    private CancellationTokenSource? _run;
    private CancellationTokenSource? _resolve;
    private LoraAdapter? _adapter;
    private double? _firstLoss;
    private bool _nameTouched;

    public FineTuneWindow()
    {
        InitializeComponent();
        SourceInitialized += (_, _) => ThemeManager.ApplyToWindow(this);
        Loc.LanguageChanged += ApplyTexts;
        Closing += OnClosing;
        Closed += (_, _) => { Loc.LanguageChanged -= ApplyTexts; _resolve?.Cancel(); _run?.Cancel(); };
        SystemBox.Text = AppSettings.Current.Instructions;
        NameBox.TextChanged += (_, _) => { if (NameBox.IsKeyboardFocused) _nameTouched = true; PaintEstimate(); };
        ApplyTexts();
        ShowStep(1);
        Loaded += async (_, _) => await LoadModelsAsync();
    }

    public int Step => _step;
    internal IReadOnlyList<TrainingExample> Examples => _kept;

    private void ApplyTexts()
    {
        Title = Loc.Get("FtTitle");
        CloseButton.ToolTip = Loc.Get("Close");
        BackButton.Content = IconLabel("", Loc.Get("FtBack"));
        IntroTitle.Text = Loc.Get("FtIntroTitle");
        IntroText.Text = Loc.Get("FtIntroText");
        IntroHow.Text = Loc.Get("FtIntroHow");
        ModelLabel.Text = Loc.Get("FtModelLabel");
        ExamplesTitle.Text = Loc.Get("FtExamplesTitle");
        ExamplesHint.Text = Loc.Format("FtExamplesHint", TrainingData.Minimum, TrainingData.Recommended);
        FromChatsButton.Content = IconLabel("", Loc.Get("FtFromChats"));
        FromFileButton.Content = IconLabel("", Loc.Get("FtFromFile"));
        AddOneButton.Content = IconLabel("", Loc.Get("FtAddOne"));
        TemplateButton.Content = IconLabel("", Loc.Get("FtTemplate"));
        FromFileButton.ToolTip = Loc.Get("FtFromFileTip");
        TemplateButton.ToolTip = Loc.Get("FtTemplateTip");
        ChatsHint.Text = Loc.Get("FtChatsHint");
        ChatsAddButton.Content = IconLabel("", Loc.Get("FtChatsAdd"));
        ChatsCancelButton.ToolTip = OneCancelButton.ToolTip = Loc.Get("Cancel");
        QuestionLabel.Text = Loc.Get("FtQuestion");
        AnswerLabel.Text = Loc.Get("FtAnswer");
        OneSaveButton.Content = IconLabel("", Loc.Get("FtAddThis"));
        SystemLabel.Text = Loc.Get("FtSystem");
        SystemHint.Text = Loc.Get("FtSystemHint");
        ClearButton.ToolTip = Loc.Get("FtClear");
        PlanTitle.Text = Loc.Get("FtPlanTitle");
        PlanHint.Text = Loc.Get("FtPlanHint");
        QuickButton.Content = Loc.Get(BaseModels.Quick.Key);
        BalancedButton.Content = Loc.Get(BaseModels.Balanced.Key);
        DeepButton.Content = Loc.Get(BaseModels.Deep.Key);
        NameLabel.Text = Loc.Get("FtName");
        NameHint.Text = Loc.Get("FtNameHint");
        EstimateTitle.Text = Loc.Get("FtEstimate");
        TrainTitle.Text = Loc.Get("FtTrainTitle");
        TrainHint.Text = Loc.Get("FtTrainHint");
        StartButton.Content = IconLabel("", Loc.Get("FtStart"));
        CancelButton.ToolTip = Loc.Get("FtStop");
        LogButton.Content = IconLabel("", Loc.Get("FtOpenLog"));
        DoneTitle.Text = Loc.Get("FtDoneTitle");
        UseCheck.Content = Loc.Get("FtUse");
        UseHint.Text = Loc.Get("FtUseHint");
        TryButton.Content = IconLabel("", Loc.Get("FtTry"));
        PaintSteps();
        PaintModelInfo();
        PaintExamples();
        PaintPreset();
        if (_adapter is not null) PaintDone();
    }

    // ------------------------------------------------------------------ pasos

    private void ShowStep(int step)
    {
        _step = step;
        Step1.Visibility = step == 1 ? Visibility.Visible : Visibility.Collapsed;
        Step2.Visibility = step == 2 ? Visibility.Visible : Visibility.Collapsed;
        Step3.Visibility = step == 3 ? Visibility.Visible : Visibility.Collapsed;
        Step4.Visibility = step == 4 ? Visibility.Visible : Visibility.Collapsed;
        Step5.Visibility = step == 5 ? Visibility.Visible : Visibility.Collapsed;
        Scroller.ScrollToTop();
        if (step == 3) PaintEstimate();
        PaintSteps();
    }

    private void PaintSteps()
    {
        StepBar.Children.Clear();
        string[] names = ["FtStep1", "FtStep2", "FtStep3", "FtStep4", "FtStep5"];
        for (var i = 1; i <= Steps; i++)
        {
            var current = i == _step;
            StepBar.Children.Add(new TextBlock
            {
                Text = $"{i}. {Loc.Get(names[i - 1])}",
                Margin = new Thickness(0, 0, 14, 0),
                FontWeight = current ? FontWeights.SemiBold : FontWeights.Normal,
                Foreground = (Brush)FindResource(current ? "Primary" : i < _step ? "TextPrimary" : "TextSecondary"),
                FontSize = 13,
            });
        }
        var running = _run is not null;
        BackButton.Visibility = _step is > 1 and < 5 ? Visibility.Visible : Visibility.Collapsed;
        BackButton.IsEnabled = !running;
        NextButton.Visibility = _step < 4 ? Visibility.Visible : Visibility.Collapsed;
        NextButton.Content = IconLabel("", Loc.Get("FtNext"));
        NextButton.IsEnabled = CanGoOn();
    }

    private bool CanGoOn() => _step switch
    {
        1 => _base is not null && _model is not null && _pc is not null && BaseModels.FitOf(_base, _pc, PythonEnv.UseCuda) != TrainFit.TooBig && !_base.Gated,
        2 => _report is { Verdict: not TrainingVerdict.TooFew },
        3 => NameBox.Text.Trim().Length > 0,
        _ => false,
    };

    private void OnNext(object sender, RoutedEventArgs e)
    {
        if (!CanGoOn()) return;
        if (_step == 2 && !_nameTouched) NameBox.Text = $"{_model?.Name} · {DateTime.Now:yyyy-MM-dd}";
        ShowStep(_step + 1);
    }

    private void OnBack(object sender, RoutedEventArgs e)
    {
        if (_run is null && _step > 1) ShowStep(_step - 1);
    }

    private void OnClose(object sender, RoutedEventArgs e) => Close();

    private void OnClosing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        if (_run is null) return;
        if (!PromptWindow.Confirm(this, Loc.Get("FtTitle"), Loc.Get("FtCloseConfirm"))) { e.Cancel = true; return; }
        _run.Cancel();
    }

    // ------------------------------------------------------------------ 1. la IA

    private async Task LoadModelsAsync()
    {
        ModelBusy.Start(Loc.Get("FtLoadingModels"));
        try
        {
            var (pc, models) = await Task.Run(() => (PcProfile.Detect(App.Engine.Accelerator), ModelCatalog.InstalledFiles()));
            _pc = pc;
            // Solo las IA de texto: el modelo de imagenes tambien es un GGUF de la carpeta.
            _models = models.Where(m => !string.Equals(m.Path, ImageEngine.ModelPath, StringComparison.OrdinalIgnoreCase)).ToList();
            ModelCombo.ItemsSource = _models.Select(m => m.Name).ToList();
            var active = _models.FindIndex(m => string.Equals(m.Path, AppSettings.Current.ModelPath, StringComparison.OrdinalIgnoreCase));
            ModelCombo.SelectedIndex = _models.Count == 0 ? -1 : Math.Max(0, active);
            if (_models.Count == 0) { BaseText.Text = Loc.Get("FtNoModels"); FitText.Text = string.Empty; }
        }
        finally
        {
            ModelBusy.Stop();
        }
    }

    private async void OnModelChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ModelCombo.SelectedIndex < 0 || ModelCombo.SelectedIndex >= _models.Count) return;
        _model = _models[ModelCombo.SelectedIndex];
        _base = null;
        PaintModelInfo();
        _resolve?.Cancel();
        var cts = _resolve = new CancellationTokenSource();
        var repo = _model.Repo ?? ModelCatalog.All.FirstOrDefault(m => m.Name == _model.Name)?.Repo;
        if (repo is null) { BaseText.Text = Loc.Get("FtNoRepo"); FitText.Text = string.Empty; PaintSteps(); return; }
        ModelBusy.Start(Loc.Get("FtResolving"));
        try
        {
            var found = await BaseModels.ResolveAsync(repo, cts.Token);
            if (cts.IsCancellationRequested) return;
            _base = found;
            if (found is null) { BaseText.Text = Loc.Format("FtNoBase", repo); FitText.Text = string.Empty; }
            else PaintModelInfo();
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            BaseText.Text = Loc.Format("FtResolveError", ex.Message);
            DiagLog.Write("enseñar", "no se encuentra la IA original: " + DiagLog.Describe(ex));
        }
        finally
        {
            if (ReferenceEquals(_resolve, cts)) ModelBusy.Stop();
            PaintSteps();
        }
    }

    private void PaintModelInfo()
    {
        var cuda = PythonEnv.UseCuda;
        EnvText.Text = PythonEnv.Installed ? Loc.Get("FtEnvReady") : Loc.Format("FtEnvMissing", cuda ? "≈ 4 GB" : "≈ 1 GB");
        if (_base is null || _pc is null) { BaseText.Text = _model is null ? BaseText.Text : string.Empty; FitText.Text = string.Empty; return; }
        BaseText.Text = Loc.Format("FtBase", _base.Repo, $"{_base.Billions:0.#}", Human(_base.Bytes), _base.License)
                        + (BaseModels.IsDownloaded(_base.Repo) ? " · " + Loc.Get("FtBaseDownloaded") : string.Empty) + ".";
        var fit = BaseModels.FitOf(_base, _pc, cuda);
        FitText.Text = _base.Gated ? Loc.Get("FtGated") : fit switch
        {
            TrainFit.Ok => Loc.Format("FtFitOk", _pc.Gpu ?? "GPU", Human(BaseModels.VramNeeded(_base.Parameters)), Human(_pc.VramBytes)),
            TrainFit.Tight => Loc.Format("FtFitTight", Human(BaseModels.VramNeeded(_base.Parameters)), Human(_pc.VramBytes)),
            TrainFit.CpuOnly => Loc.Get("FtFitCpu"),
            _ => Loc.Format("FtFitTooBig", Human(BaseModels.VramNeeded(_base.Parameters)), Human(_pc.VramBytes)),
        };
        FitText.Foreground = (Brush)FindResource(_base.Gated || fit == TrainFit.TooBig ? "Danger" : fit == TrainFit.Ok ? "Success" : "TextSecondary");
    }

    // ------------------------------------------------------------------ 2. los ejemplos

    private void OnFromChats(object sender, RoutedEventArgs e)
    {
        OneBox.Visibility = Visibility.Collapsed;
        ChatsList.Children.Clear();
        var threads = ThreadStore.LoadAll().Where(t => t.TaskId is null).ToList();
        foreach (var thread in threads)
        {
            var count = TrainingData.FromThreads([thread]).Count;
            if (count == 0) continue;
            ChatsList.Children.Add(new CheckBox { Style = (Style)FindResource("Check"), Content = Loc.Format("FtChatRow", thread.Title.Length > 0 ? thread.Title : "…", count), Tag = thread, Margin = new Thickness(0, 2, 0, 2) });
        }
        if (ChatsList.Children.Count == 0)
            ChatsList.Children.Add(new TextBlock { Style = (Style)FindResource("HintText"), Text = Loc.Get("FtNoChats") });
        ChatsAddButton.IsEnabled = ChatsList.Children.OfType<CheckBox>().Any();
        ChatsBox.Visibility = Visibility.Visible;
    }

    private void OnChatsAdd(object sender, RoutedEventArgs e)
    {
        var chosen = ChatsList.Children.OfType<CheckBox>().Where(c => c.IsChecked == true).Select(c => (ChatThread)c.Tag).ToList();
        AddExamples(TrainingData.FromThreads(chosen));
        ChatsBox.Visibility = Visibility.Collapsed;
    }

    private void OnChatsCancel(object sender, RoutedEventArgs e) => ChatsBox.Visibility = Visibility.Collapsed;

    private void OnFromFile(object sender, RoutedEventArgs e)
    {
        if (Desktop.Current.PickFiles(this, Loc.Get("FtFromFile"), Loc.Get("FtFileFilter"), multiselect: true) is not { Length: > 0 } files) return;
        var added = 0;
        foreach (var file in files)
        {
            try
            {
                var examples = TrainingData.FromFile(file);
                added += examples.Count;
                AddExamples(examples);
            }
            catch (Exception ex)
            {
                PromptWindow.Alert(this, Loc.Get("Error"), Loc.Format("FtFileError", Path.GetFileName(file), ex.Message));
            }
        }
        if (added == 0) PromptWindow.Alert(this, Loc.Get("FtFromFile"), Loc.Get("FtFileEmpty"));
    }

    private void OnAddOne(object sender, RoutedEventArgs e)
    {
        ChatsBox.Visibility = Visibility.Collapsed;
        OneBox.Visibility = Visibility.Visible;
        QuestionBox.Focus();
    }

    private void OnOneSave(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(QuestionBox.Text) || string.IsNullOrWhiteSpace(AnswerBox.Text)) return;
        AddExamples([TrainingExample.Pair(QuestionBox.Text, AnswerBox.Text, Loc.Get("FtByHand"))]);
        QuestionBox.Clear();
        AnswerBox.Clear();
        QuestionBox.Focus();
    }

    private void OnOneCancel(object sender, RoutedEventArgs e) => OneBox.Visibility = Visibility.Collapsed;

    /// <summary>Guarda una plantilla CSV con ejemplos de muestra para rellenar en Excel o el Bloc de notas.</summary>
    private void OnTemplate(object sender, RoutedEventArgs e)
    {
        if (Desktop.Current.PickSave(this, Loc.Get("FtTemplateFile"), "CSV (*.csv)|*.csv", ".csv") is not { } path) return;
        File.WriteAllText(path, Loc.Get("FtTemplateCsv"), new System.Text.UTF8Encoding(true));
        Desktop.Current.Open(path);
    }

    private void OnClearExamples(object sender, RoutedEventArgs e)
    {
        if (_examples.Count == 0 || !PromptWindow.Confirm(this, Loc.Get("FtClear"), Loc.Get("FtClearConfirm"))) return;
        _examples.Clear();
        PaintExamples();
    }

    internal void AddExamples(IEnumerable<TrainingExample> examples)
    {
        _examples.AddRange(examples);
        PaintExamples();
    }

    private const int Shown = 40;

    private void PaintExamples()
    {
        (_kept, _report) = TrainingData.Review(_examples);
        var r = _report;
        ReviewText.Text = Loc.Format("FtReview", r.Kept);
        ReviewText.Foreground = (Brush)FindResource(r.Verdict switch { TrainingVerdict.Good => "Success", TrainingVerdict.Few => "TextPrimary", _ => "Danger" });
        var notes = new List<string>
        {
            Loc.Get(r.Verdict switch { TrainingVerdict.Good => "FtVerdictGood", TrainingVerdict.Few => "FtVerdictFew", _ => "FtVerdictTooFew" }),
        };
        if (r.Duplicates > 0) notes.Add(Loc.Format("FtDuplicates", r.Duplicates));
        if (r.Empty > 0) notes.Add(Loc.Format("FtEmpty", r.Empty));
        if (r.TooLong > 0) notes.Add(Loc.Format("FtTooLong", r.TooLong));
        ReviewHint.Text = string.Join(" ", notes);
        ClearButton.Visibility = _examples.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        ExampleList.Children.Clear();
        foreach (var example in _kept.Take(Shown))
            ExampleList.Children.Add(ExampleRow(example));
        MoreExamples.Text = _kept.Count > Shown ? Loc.Format("FtMore", _kept.Count - Shown) : string.Empty;
        PaintSteps();
    }

    private UIElement ExampleRow(TrainingExample example)
    {
        var grid = new Grid { Margin = new Thickness(0, 3, 0, 3) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var text = new StackPanel();
        text.Children.Add(new TextBlock { Style = (Style)FindResource("BodyText"), Text = "❓ " + OneLine(example.Question), TextTrimming = TextTrimming.CharacterEllipsis });
        text.Children.Add(new TextBlock { Style = (Style)FindResource("HintText"), Text = "💬 " + OneLine(example.Answer), TextTrimming = TextTrimming.CharacterEllipsis });
        if (example.Source.Length > 0)
            text.Children.Add(new TextBlock { Style = (Style)FindResource("HintText"), Text = example.Source, FontSize = 11, Opacity = 0.7, TextTrimming = TextTrimming.CharacterEllipsis });
        grid.Children.Add(text);
        var remove = new Button { Style = (Style)FindResource("GhostIconButton"), Content = "", ToolTip = Loc.Get("FtRemove"), VerticalAlignment = VerticalAlignment.Center };
        remove.Click += (_, _) => { _examples.RemoveAll(x => x.Key == example.Key); PaintExamples(); };
        Grid.SetColumn(remove, 1);
        grid.Children.Add(remove);
        return grid;
    }

    private static string OneLine(string text)
    {
        var line = text.Replace('\r', ' ').Replace('\n', ' ').Trim();
        return line.Length > 160 ? line[..157] + "…" : line;
    }

    // ------------------------------------------------------------------ 3. como entrenar

    private void OnPreset(object sender, RoutedEventArgs e)
    {
        _preset = sender == QuickButton ? BaseModels.Quick : sender == DeepButton ? BaseModels.Deep : BaseModels.Balanced;
        PaintPreset();
    }

    private void PaintPreset()
    {
        QuickButton.IsChecked = _preset == BaseModels.Quick;
        BalancedButton.IsChecked = _preset == BaseModels.Balanced;
        DeepButton.IsChecked = _preset == BaseModels.Deep;
        PresetText.Text = Loc.Get(_preset.Key + "Hint");
        PaintEstimate();
    }

    /// <summary>
    /// Pasadas, pasos y segundos que se tardan, a ojo: en la grafica ~0,35 s por ejemplo mas ~0,4 s por
    /// cada mil millones de parametros (medido con Qwen3.5 0,8B en una RTX 3050 Ti 4 GB: 96 ejemplos
    /// entrenados en 69 s), mas cargar la IA y convertir el resultado; en la CPU, unas diez veces mas.
    /// </summary>
    public static (int Epochs, int Steps, int Seconds) Estimate(int examples, TrainingPreset preset, double billions, bool gpu)
    {
        var (epochs, accumulation, steps) = preset.Plan(examples);
        var perExample = (0.35 + 0.4 * billions) * (preset.MaxTokens > 512 ? 1.0 : 0.8);
        if (!gpu) perExample *= 10;
        var fixedCost = 60 + 15 * billions;   // cargar la IA y convertir el adaptador a GGUF
        return (epochs, steps, (int)(steps * accumulation * perExample + fixedCost));
    }

    private void PaintEstimate()
    {
        if (_base is null || _pc is null || _report is null) { EstimateText.Text = string.Empty; return; }
        var gpu = PythonEnv.UseCuda && _pc.HasUsableGpu;
        var (epochs, steps, seconds) = Estimate(_report.Kept, _preset, _base.Billions, gpu);
        var lines = new List<string> { Loc.Format("FtEstimateTime", _report.Kept, epochs, steps, Minutes(seconds)) };
        if (!PythonEnv.Installed) lines.Add(Loc.Format("FtEstimateEnv", PythonEnv.UseCuda ? "≈ 4 GB" : "≈ 1 GB"));
        if (!BaseModels.IsDownloaded(_base.Repo)) lines.Add(Loc.Format("FtEstimateBase", _base.Repo, Human(_base.Bytes)));
        lines.Add(Loc.Get("FtEstimateChat"));
        EstimateText.Text = string.Join(Environment.NewLine, lines);
        NextButton.IsEnabled = CanGoOn();
    }

    private static string Minutes(int seconds) => seconds < 90 ? Loc.Format("FtSeconds", seconds) : Loc.Format("FtMinutes", Math.Ceiling(seconds / 60.0));

    // ------------------------------------------------------------------ 4. entrenar

    private async void OnStart(object sender, RoutedEventArgs e) => await StartAsync();

    public async Task StartAsync()
    {
        if (_run is not null || _base is null || _model is null) return;
        var cts = _run = new CancellationTokenSource();
        StartButton.Visibility = Visibility.Collapsed;
        ErrorText.Visibility = Visibility.Collapsed;
        RunBox.Visibility = Visibility.Visible;
        _firstLoss = null;
        LossText.Text = DetailText.Text = string.Empty;
        PaintSteps();
        var name = NameBox.Text.Trim();
        var system = SystemBox.Text.Trim();
        var examples = _kept.Select(x => system.Length == 0 || x.Messages[0].Role == "system" ? x
            : new TrainingExample { Messages = [new("system", system), .. x.Messages], Source = x.Source }).ToList();
        try
        {
            Stage(Loc.Get("FtStageEnv"), indeterminate: true);
            var status = new Progress<string>(s => Stage(s, indeterminate: true));
            await PythonEnv.EnsureAsync(status, new Progress<Downloader.Progress>(Download), cts.Token);

            Stage(Loc.Format("FtStageBase", _base.Repo), indeterminate: true);
            var folder = await BaseModels.DownloadAsync(_base, new Progress<Downloader.Progress>(Download), cts.Token);

            var dataset = Path.Combine(FineTuneRunner.WorkFolder, "dataset.jsonl");
            TrainingData.Save(examples, dataset);
            DetailText.Text = string.Empty;
            var events = new Progress<TrainEvent>(OnTrainEvent);
            var path = await FineTuneRunner.TrainAsync(folder, dataset, examples.Count, _preset, name, events, cts.Token);
            if (path is null) { Stopped(); return; }
            _adapter = new LoraAdapter
            {
                Name = name, Path = path, ModelPath = _model.Path, BaseRepo = _base.Repo,
                Examples = examples.Count, Preset = _preset.Key, CreatedAt = DateTimeOffset.Now,
            };
            Adapters.Add(AppSettings.Current, _adapter, activate: true);
            DiagLog.Write("enseñar", $"adaptador «{name}» entrenado con {examples.Count} ejemplos sobre {_base.Repo}");
            PaintDone();
            _run = null;
            ShowStep(5);
        }
        catch (OperationCanceledException)
        {
            Stopped();
        }
        catch (Exception ex)
        {
            DiagLog.Write("enseñar", "el entrenamiento ha fallado: " + DiagLog.Describe(ex));
            ErrorText.Text = ex.Message;
            ErrorText.Visibility = Visibility.Visible;
            Idle();
        }
    }

    private void Stopped()
    {
        ErrorText.Text = Loc.Get("FtStopped");
        ErrorText.Visibility = Visibility.Visible;
        Idle();
    }

    private void Idle()
    {
        _run = null;
        RunBox.Visibility = Visibility.Collapsed;
        StartButton.Visibility = Visibility.Visible;
        StartButton.Content = IconLabel("", Loc.Get("FtStartAgain"));
        PaintSteps();
    }

    private void OnCancel(object sender, RoutedEventArgs e)
    {
        if (_run is null) return;
        StageText.Text = Loc.Get("FtStopping");
        _run.Cancel();
    }

    private void Stage(string text, bool indeterminate)
    {
        StageText.Text = text;
        TrainBar.IsIndeterminate = indeterminate;
    }

    private void Download(Downloader.Progress p)
    {
        TrainBar.IsIndeterminate = p.Fraction is null;
        TrainBar.Value = (p.Fraction ?? 0) * 100;
        DetailText.Text = p.Total is { } t ? $"{Human(p.Received)} / {Human(t)}" : Human(p.Received);
    }

    private void OnTrainEvent(TrainEvent e)
    {
        switch (e.Kind)
        {
            case "stage":
                Stage(Loc.Get(e.Stage switch
                {
                    "loading" => "FtStageLoading",
                    "preparing" => "FtStagePreparing",
                    "training" => "FtStageTraining",
                    "saving" => "FtStageSaving",
                    "converting" => "FtStageConverting",
                    "quantizing" => "FtStageQuantizing",
                    _ => "FtStageEnv",
                }), indeterminate: e.Stage != "training");
                if (e.Stage == "training") { TrainBar.Value = 0; DetailText.Text = Loc.Format("FtSteps", 0, e.Total); }
                break;
            case "progress":
                TrainBar.IsIndeterminate = false;
                TrainBar.Value = e.Total > 0 ? 100.0 * e.Step / e.Total : 0;
                var remaining = FineTuneRunner.Remaining(e);
                DetailText.Text = Loc.Format("FtSteps", e.Step, e.Total) + (remaining is { } r ? " · " + Loc.Format("FtRemaining", Minutes(r)) : string.Empty);
                if (e.Loss is { } loss)
                {
                    _firstLoss ??= loss;
                    LossText.Text = Loc.Format("FtLoss", _firstLoss.Value.ToString("0.00"), loss.ToString("0.00"));
                }
                break;
        }
    }

    private void OnOpenLog(object sender, RoutedEventArgs e)
    {
        if (File.Exists(PythonEnv.LogPath)) Desktop.Current.Open(PythonEnv.LogPath);
    }

    // ------------------------------------------------------------------ 5. listo

    private void PaintDone()
    {
        if (_adapter is null) return;
        DoneText.Text = Loc.Format("FtDoneText", _adapter.Name, _model?.Name ?? string.Empty, _adapter.Examples);
        UseCheck.IsChecked = _adapter.Active;
    }

    private void OnUseChanged(object sender, RoutedEventArgs e)
    {
        if (_adapter is null) return;
        Adapters.SetActive(AppSettings.Current, UseCheck.IsChecked == true ? _adapter : null, _adapter.ModelPath);
    }

    /// <summary>Pone en uso la IA entrenada (con su adaptador) y abre una conversacion nueva para probarla.</summary>
    private void OnTry(object sender, RoutedEventArgs e)
    {
        if (_model is not null)
        {
            var s = AppSettings.Current;
            s.ModelPath = _model.Path;
            s.ModelName = _model.Name;
            s.ModelLicense = _model.License;
            s.Save();
        }
        var main = Application.Current.Windows.OfType<MainWindow>().FirstOrDefault();
        Close();
        if (Owner is SettingsWindow settings) settings.Close();
        main?.ModelChanged();
        main?.NewConversation();
    }

    // ------------------------------------------------------------------ ayudas

    private static StackPanel IconLabel(string glyph, string text)
    {
        var panel = new StackPanel { Orientation = Orientation.Horizontal };
        panel.Children.Add(new TextBlock { Text = glyph, FontFamily = new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets"), FontSize = 14, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) });
        panel.Children.Add(new TextBlock { Text = text, VerticalAlignment = VerticalAlignment.Center });
        return panel;
    }

    private static string Human(long bytes) => bytes switch
    {
        >= 1L << 30 => $"{bytes / (double)(1L << 30):0.0} GB",
        >= 1L << 20 => $"{bytes / (double)(1L << 20):0} MB",
        _ => $"{bytes / 1024.0:0} KB",
    };

    // ------------------------------------------------------------------ pruebas

    internal void GoToStepForTest(int step) => ShowStep(step);

    /// <summary>De punta a punta sin ratón: espera a la IA original, carga los ejemplos del fichero, entrena con «Equilibrado» y cierra la app al acabar.</summary>
    internal async Task RunForTestAsync(string file)
    {
        while (_base is null || _pc is null) await Task.Delay(250);
        AddExamples(TrainingData.FromFile(file));
        _preset = BaseModels.Balanced;
        NameBox.Text = "prueba";
        ShowStep(4);
        await StartAsync();
        DiagLog.Write("enseñar", $"prueba acabada en el paso {_step}: {(ErrorText.Visibility == Visibility.Visible ? ErrorText.Text : _adapter?.Path)}");
        Application.Current.Shutdown();
    }
    internal void SetBaseForTest(InstalledModel model, BaseModel baseModel, PcProfile pc) { _model = model; _base = baseModel; _pc = pc; PaintModelInfo(); PaintSteps(); }
    internal bool NextEnabled => NextButton.IsEnabled;
    internal string ReviewTextForTest => ReviewText.Text;
    internal string FitTextForTest => FitText.Text;
    internal string EstimateForTest => EstimateText.Text;
}
