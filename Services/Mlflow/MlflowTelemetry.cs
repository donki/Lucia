using System.Net.Http;
using System.Text;
using System.Threading.Channels;

namespace SocLucia.Services.Mlflow;

/// <summary>Con que se contesto: va como parametros de la ejecucion (si cambia, ejecucion nueva).</summary>
public sealed record RunConfig(string Model, string ModelFile, int Context, int MaxTokens, bool Thinking, double Temperature, string Accelerator, string AppVersion, string Gpu)
{
    public IEnumerable<KeyValuePair<string, string>> Params()
    {
        yield return new("model", Model);
        yield return new("model_file", ModelFile);
        yield return new("context_tokens", Context.ToString(System.Globalization.CultureInfo.InvariantCulture));
        yield return new("max_tokens", MaxTokens.ToString(System.Globalization.CultureInfo.InvariantCulture));
        yield return new("thinking", Thinking ? "true" : "false");
        yield return new("temperature", Temperature.ToString(System.Globalization.CultureInfo.InvariantCulture));
        yield return new("accelerator", Accelerator);
        yield return new("app_version", AppVersion);
        yield return new("gpu", Gpu);
    }
}

/// <summary>
/// Una pregunta contestada (o cortada): tiempos, tokens y resultado. <see cref="Question"/> y
/// <see cref="Answer"/> solo se rellenan si el usuario marco «mandar tambien el texto».
/// </summary>
public sealed record RequestRecord(string Source, double? TtftMs, double DurationMs, int? PromptTokens, int? CompletionTokens, double? TokensPerSecond,
    int AnswerChars, int ReasoningChars, int ToolCalls, string Outcome, string? Error = null, string? Question = null, string? Answer = null);

/// <summary>
/// Envio a MLflow en segundo plano. Decisiones (escritas tambien en el README):
/// <list type="bullet">
/// <item><b>Lo principal son las trazas</b> (seccion GenAI, pestaña Traces): una por interaccion
/// (<see cref="InteractionTrace"/>), en OTLP/HTTP protobuf a <c>/v1/traces</c> con la cabecera
/// <c>x-mlflow-experiment-id</c>, lo que admite MLflow desde la 3.6.0 (la 3.6 solo en protobuf; JSON llego
/// despues). La version se pregunta a <c>/version</c>: con una 2.x o anterior a 3.6, o si el servidor responde
/// que no las admite, las interacciones van como metricas de la ejecucion y se avisa una vez en el registro.</item>
/// <item>Una <b>ejecucion (run) por sesion de la aplicacion y configuracion</b>: los parametros de un run
/// no se pueden cambiar en MLflow, asi que si cambia el modelo, el contexto, el tope o «Pensar», empieza
/// otra. Cada pregunta es un paso (step) de sus metricas.</item>
/// <item>Solo la API de seguimiento (runs, metricas, parametros, etiquetas), que tiene cualquier servidor
/// de MLflow y Databricks; la de trazas cambia entre versiones. Los eventos del registro de diagnostico van
/// como fichero <c>events.log</c> de la ejecucion si el servidor sirve artefactos; si no, los errores, las
/// cancelaciones y las congelaciones van como etiquetas.</item>
/// <item>Tras cada envio la ejecucion se marca terminada con la hora: si la aplicacion se cierra o se cae,
/// no se queda «en curso» para siempre. Se puede seguir escribiendo en ella.</item>
/// <item>Nunca bloquea: cola acotada (se descarta lo mas viejo y se cuenta), reintentos con espera creciente
/// y un solo aviso en el registro local cuando deja de responder (y otro cuando vuelve).</item>
/// </list>
/// </summary>
public sealed class MlflowTelemetry : IDisposable
{
    /// <summary>La de la aplicacion (las pruebas usan las suyas).</summary>
    public static MlflowTelemetry Current { get; set; } = new();

    private readonly HttpClient _http;
    private readonly TimeSpan _flushEvery;
    private readonly TimeSpan _maxBackoff;
    private readonly int _capacity;
    private readonly object _gate = new();
    private Channel<object>? _queue;
    private CancellationTokenSource? _stop;
    private Task? _worker;
    private MlflowSettings? _settings;
    private int _dropped;
    private long _queued;
    private long _done;
    private volatile bool _failing;

    public MlflowTelemetry(HttpMessageHandler? handler = null, TimeSpan? flushEvery = null, int capacity = 1000, TimeSpan? maxBackoff = null)
    {
        _http = new HttpClient(handler ?? new HttpClientHandler(), disposeHandler: handler is null) { Timeout = TimeSpan.FromSeconds(20) };
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("sOCLucia");
        _flushEvery = flushEvery ?? TimeSpan.FromSeconds(3);
        _maxBackoff = maxBackoff ?? TimeSpan.FromSeconds(60);
        _capacity = capacity;
    }

    /// <summary>El cliente HTTP (la prueba de conexion de Ajustes usa el mismo).</summary>
    public HttpClient Http => _http;

    public bool Enabled { get { lock (_gate) return _settings is not null; } }

    /// <summary>Lo que se ha tirado por tener la cola llena (MLflow caido mucho rato).</summary>
    public int Dropped => Volatile.Read(ref _dropped);

    /// <summary>Si ahora mismo no se esta consiguiendo enviar.</summary>
    public bool Failing => _failing;

    /// <summary>Los envios hechos con exito (para las pruebas).</summary>
    public int Sent { get; private set; }

    /// <summary>Que proporciona la configuracion actual (para la ejecucion de los eventos sueltos).</summary>
    public Func<RunConfig>? ConfigNow { get; set; }

    /// <summary>Arranca, cambia o para el envio segun los ajustes. Lo de antes que quede en la cola se manda con los nuevos.</summary>
    public void Configure(MlflowSettings settings)
    {
        lock (_gate)
        {
            var on = settings.Enabled && MlflowApi.TryUri(settings.TrackingUri, out _);
            if (!on)
            {
                StopLocked();
                return;
            }
            // Una copia: cambiar los ajustes en pantalla no cambia lo que usa el hilo de envio a medias.
            var copy = new MlflowSettings
            {
                Enabled = true, TrackingUri = settings.TrackingUri.Trim(), Experiment = string.IsNullOrWhiteSpace(settings.Experiment) ? MlflowSettings.DefaultExperiment : settings.Experiment.Trim(),
                Username = settings.Username, ProtectedPassword = settings.ProtectedPassword, ProtectedToken = settings.ProtectedToken, SendText = settings.SendText,
                InstallId = settings.InstallId is { Length: > 0 } id ? id : MlflowSettings.NewInstallId(),
            };
            _mode = MlflowMode.Unknown;
            ServerVersion = null;
            StopLocked();
            _settings = copy;
            _queue = Channel.CreateBounded<object>(new BoundedChannelOptions(_capacity) { FullMode = BoundedChannelFullMode.DropOldest, SingleReader = true },
                _ => { Interlocked.Increment(ref _dropped); Interlocked.Increment(ref _done); });
            _stop = new CancellationTokenSource();
            var queue = _queue;
            var stop = _stop.Token;
            _worker = Task.Run(() => RunAsync(copy, queue, stop));
            DiagLog.Written += OnLog;
            DiagLog.Write("mlflow", $"envio activado a {Host(copy.TrackingUri)}, experimento «{copy.Experiment}», texto {(copy.SendText ? "si" : "no")}");
        }
    }

    private void StopLocked()
    {
        if (_settings is null) return;
        DiagLog.Written -= OnLog;
        _queue?.Writer.TryComplete();
        _stop?.CancelAfter(TimeSpan.FromSeconds(5));   // lo que queda, si el servidor responde, sale; si no, se deja
        _settings = null;
        _queue = null;
        _stop = null;
        _failing = false;
    }

    private static string Host(string uri) => MlflowApi.TryUri(uri, out var u) ? u.Host : "?";

    private void OnLog(string area, string line)
    {
        if (area == "mlflow") return;   // lo suyo no vuelve a entrar (ni hace eco)
        Enqueue(new LogEvent(DateTimeOffset.Now, area, line));
    }

    private void Enqueue(object item)
    {
        Channel<object>? queue;
        lock (_gate) queue = _queue;
        if (queue is not null && queue.Writer.TryWrite(item))
            Interlocked.Increment(ref _queued);
    }

    /// <summary>Una pregunta acabada. El texto solo sale si los ajustes lo permiten; si no, se borra aqui otra vez.</summary>
    public void Request(RunConfig config, RequestRecord record)
    {
        bool sendText;
        lock (_gate)
        {
            if (_settings is null) return;
            sendText = _settings.SendText;
        }
        if (!sendText) record = record with { Question = null, Answer = null };
        Enqueue(new RequestItem(DateTimeOffset.Now, config, record));
    }

    /// <summary>Si ahora se manda el texto (para quien arma las trazas). Sin envio activado, falso.</summary>
    public bool SendText { get { lock (_gate) return _settings?.SendText == true; } }

    /// <summary>El id aleatorio de esta instalacion para las trazas.</summary>
    public string InstallId { get { lock (_gate) return _settings?.InstallId is { Length: > 0 } id ? id : "lucia"; } }

    /// <summary>
    /// Una interaccion acabada, como traza. Si el texto no esta permitido, la traza no deberia llevarlo
    /// (lo controla <see cref="InteractionTrace"/>); aqui se comprueba otra vez y, si lo lleva, no sale.
    /// </summary>
    public void Trace(InteractionTrace trace)
    {
        if (!Enabled) return;
        if (trace.IncludesText && !SendText) return;
        Enqueue(new TraceItem(trace.Data));
    }

    /// <summary>El modo que se esta usando (para las pruebas y el resumen).</summary>
    public MlflowMode Mode => _mode;

    /// <summary>La version del servidor, si la dijo.</summary>
    public Version? ServerVersion { get; private set; }

    private volatile MlflowMode _mode;

    /// <summary>Una congelacion de la ventana (del vigilante), con lo que duro: metrica de la ejecucion y traza propia.</summary>
    public void Freeze(double seconds)
    {
        if (!Enabled) return;
        Enqueue(new FreezeItem(DateTimeOffset.Now, seconds));
        Enqueue(new TraceItem(InteractionTrace.Freeze(seconds, DiagLog.Phase, InstallId, ConfigNow?.Invoke().AppVersion ?? "-")));
    }

    private sealed record LogEvent(DateTimeOffset At, string Area, string Line);
    private sealed record RequestItem(DateTimeOffset At, RunConfig Config, RequestRecord Record);
    private sealed record FreezeItem(DateTimeOffset At, double Seconds);
    private sealed record TraceItem(TraceData Trace);

    // ------------------------------------------------------------------ el hilo de envio

    private sealed class RunState
    {
        public required string Id;
        public required string? ArtifactUri;
        public required RunConfig Config;
        public bool? Artifacts;   // null: sin probar
        public long Step;
        public long Freezes;
        public long Errors;
        public int EventTags;
        public readonly List<string> Events = [];
    }

    private async Task RunAsync(MlflowSettings settings, Channel<object> queue, CancellationToken stop)
    {
        if (!MlflowApi.TryUri(settings.TrackingUri, out var uri)) return;
        var api = new MlflowApi(_http, uri, settings.Username, Secrets.Unprotect(settings.ProtectedPassword), Secrets.Unprotect(settings.ProtectedToken));
        string? experiment = null;
        _run = null;
        var backoff = TimeSpan.FromSeconds(1);
        var pending = new List<object>();
        try
        {
            while (true)
            {
                if (pending.Count == 0)
                {
                    if (!await queue.Reader.WaitToReadAsync(stop).ConfigureAwait(false)) break;
                    // Se juntan unos segundos de cosas en un solo envio (menos peticiones, nada por mensaje).
                    await Task.Delay(_flushEvery, stop).ConfigureAwait(false);
                    while (pending.Count < 500 && queue.Reader.TryRead(out var item)) pending.Add(item);
                    if (pending.Count == 0) continue;
                }
                try
                {
                    experiment ??= await api.EnsureExperimentAsync(settings.Experiment, stop).ConfigureAwait(false);
                    if (_mode == MlflowMode.Unknown && ServerVersion is null)
                    {
                        ServerVersion = await api.GetVersionAsync(stop).ConfigureAwait(false);
                        _mode = MlflowApi.ModeFor(ServerVersion);
                        DiagLog.Write("mlflow", ServerVersion is null ? "el servidor no dice su version: se prueban las trazas"
                            : _mode == MlflowMode.Traces ? $"MLflow {ServerVersion}: interacciones como trazas (GenAI)"
                            : $"MLflow {ServerVersion} no tiene trazas de GenAI: interacciones como ejecuciones con metricas");
                    }
                    await SendAsync(api, experiment, pending, stop).ConfigureAwait(false);
                    Interlocked.Add(ref _done, pending.Count);
                    pending.Clear();
                    Sent++;
                    backoff = TimeSpan.FromSeconds(1);
                    if (_failing)
                    {
                        _failing = false;
                        DiagLog.Write("mlflow", "MLflow vuelve a responder; se envia lo pendiente");
                    }
                }
                catch (Exception ex) when (!stop.IsCancellationRequested)
                {
                    if (!_failing)
                    {
                        _failing = true;
                        // Una sola vez hasta que vuelva, y nunca con credenciales (no estan en el mensaje).
                        DiagLog.Write("mlflow", $"MLflow no responde ({Describe(ex)}); se reintenta en segundo plano y Lucia sigue igual");
                    }
                    if (ex is MlflowException { Status: 400 } && pending.Count > 1)
                    {
                        pending.RemoveAt(0);   // algo que el servidor no acepta no puede atascar lo demas
                        Interlocked.Increment(ref _done);
                    }
                    await Task.Delay(backoff, stop).ConfigureAwait(false);
                    backoff = TimeSpan.FromTicks(Math.Min(backoff.Ticks * 2, _maxBackoff.Ticks));
                }
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            DiagLog.Write("mlflow", "el envio se ha parado: " + Describe(ex));
        }
    }

    private static string Describe(Exception ex) => ex switch
    {
        MlflowException m => $"HTTP {m.Status}{(m.Code is null ? string.Empty : " " + m.Code)}",
        HttpRequestException h => h.HttpRequestError.ToString(),
        TaskCanceledException => "tiempo agotado",
        _ => ex.GetType().Name,
    };

    /// <summary>Manda un lote: abre ejecucion si hace falta (o otra si cambio la configuracion), metricas, etiquetas y eventos.</summary>
    private RunState? _run;

    private async Task SendAsync(MlflowApi api, string experiment, List<object> items, CancellationToken stop)
    {
        var metrics = new List<MlflowApi.Metric>();
        var tags = new List<KeyValuePair<string, string>>();
        async Task FlushAsync(RunState r)
        {
            foreach (var chunk in metrics.Chunk(MlflowApi.MaxMetrics))
                await api.LogBatchAsync(r.Id, chunk, [], [], stop).ConfigureAwait(false);
            foreach (var chunk in tags.Chunk(MlflowApi.MaxTags))
                await api.LogBatchAsync(r.Id, [], [], chunk, stop).ConfigureAwait(false);
            metrics.Clear();
            tags.Clear();
            if (r.Artifacts != false && r.Events.Count > 0)
            {
                r.Artifacts = await api.UploadArtifactAsync(r.ArtifactUri, "events.log", Encoding.UTF8.GetBytes(string.Join("\n", r.Events) + "\n"), stop).ConfigureAwait(false);
                if (r.Artifacts == false)
                    foreach (var e in r.Events.Where(Important).TakeLast(MlflowApi.MaxTags))
                        if (r.EventTags++ < 200) tags.Add(new($"event.{r.EventTags:0000}", e));
                if (tags.Count > 0) { await api.LogBatchAsync(r.Id, [], [], tags, stop).ConfigureAwait(false); tags.Clear(); }
            }
            await api.FinishRunAsync(r.Id, DateTimeOffset.Now, "FINISHED", stop).ConfigureAwait(false);
        }
        async Task<RunState> OpenAsync(RunConfig config, DateTimeOffset at)
        {
            var name = $"Lucia {at:yyyy-MM-dd HH:mm} · {config.Model}";
            var created = await api.CreateRunAsync(experiment, name, at, [new("mlflow.source.name", "sOC Lucia"), new("lucia.version", config.AppVersion)], stop).ConfigureAwait(false);
            // Guardada ya: si lo siguiente falla, el reintento sigue en esta y no abre otra vacia.
            _run = new RunState { Id = created.RunId, ArtifactUri = created.ArtifactUri, Config = config };
            await api.LogBatchAsync(created.RunId, [], config.Params().ToList(), [], stop).ConfigureAwait(false);
            return _run;
        }

        var run = _run;
        var traces = items.OfType<TraceItem>().Select(t => t.Trace).ToList();
        if (traces.Count > 0 && _mode != MlflowMode.Runs)
        {
            // Primero las trazas, enlazadas a la ejecucion de la sesion si ya hay una.
            var payload = Otlp.Encode(traces, ConfigNow?.Invoke().AppVersion ?? "-");
            if (await api.ExportTracesAsync(experiment, run?.Id, payload, stop).ConfigureAwait(false))
            {
                if (_mode == MlflowMode.Unknown) { _mode = MlflowMode.Traces; DiagLog.Write("mlflow", "el servidor admite trazas: interacciones como trazas (GenAI)"); }
            }
            else
            {
                _mode = MlflowMode.Runs;
                DiagLog.Write("mlflow", "el servidor no admite trazas (GenAI): interacciones como ejecuciones con metricas");
            }
        }

        foreach (var item in items)
        {
            if (item is TraceItem) continue;   // ya enviadas arriba (o descartadas si el servidor no las admite)
            var config = item is RequestItem r ? r.Config : run?.Config ?? ConfigNow?.Invoke() ?? new RunConfig("-", "-", 0, 0, false, 0, "-", "-", "-");
            if (run is null || (item is RequestItem && run.Config != config))
            {
                if (run is not null) await FlushAsync(run).ConfigureAwait(false);
                run = await OpenAsync(config, item switch { RequestItem x => x.At, LogEvent x => x.At, FreezeItem x => x.At, _ => DateTimeOffset.Now });
            }
            switch (item)
            {
                case RequestItem request:
                {
                    var rec = request.Record;
                    var step = ++run.Step;
                    void M(string key, double? value) { if (value is { } v) metrics.Add(new($"{rec.Source}.{key}", v, request.At, step)); }
                    M("ttft_ms", rec.TtftMs);
                    M("duration_ms", rec.DurationMs);
                    M("prompt_tokens", rec.PromptTokens);
                    M("completion_tokens", rec.CompletionTokens);
                    M("tokens_per_s", rec.TokensPerSecond);
                    M("answer_chars", rec.AnswerChars);
                    M("reasoning_chars", rec.ReasoningChars);
                    M("tool_calls", rec.ToolCalls);
                    M("cancelled", rec.Outcome == "cancelled" ? 1 : 0);
                    M("error", rec.Outcome == "error" ? 1 : 0);
                    if (rec.Error is not null) tags.Add(new($"{rec.Source}.{step:0000}.error", rec.Error));
                    // El texto, solo donde no hay trazas (con trazas va en sus entradas y salidas).
                    if (_mode == MlflowMode.Runs && rec.Question is not null) tags.Add(new($"{rec.Source}.{step:0000}.question", rec.Question));
                    if (_mode == MlflowMode.Runs && rec.Answer is not null) tags.Add(new($"{rec.Source}.{step:0000}.answer", rec.Answer));
                    break;
                }
                case FreezeItem freeze:
                    metrics.Add(new("ui_freeze_s", freeze.Seconds, freeze.At, ++run.Freezes));
                    break;
                case LogEvent log:
                    if (run.Events.Count >= 5000) run.Events.RemoveAt(0);
                    run.Events.Add($"{log.At:yyyy-MM-dd HH:mm:ss.fff} [{log.Area}] {log.Line}");
                    if (log.Area == "error" || log.Line.Contains("error", StringComparison.OrdinalIgnoreCase))
                        metrics.Add(new("log_errors", ++run.Errors, log.At, run.Errors));
                    break;
            }
        }
        if (run is not null) await FlushAsync(run).ConfigureAwait(false);
    }

    /// <summary>Lo que va como etiqueta si el servidor no guarda ficheros: errores, cancelaciones, congelaciones.</summary>
    private static bool Important(string line)
        => line.Contains("[error]", StringComparison.Ordinal) || line.Contains("[ui]", StringComparison.Ordinal)
           || line.Contains("error", StringComparison.OrdinalIgnoreCase) || line.Contains("cancelad", StringComparison.OrdinalIgnoreCase);

    /// <summary>Para las pruebas: espera a que la cola se vacie y el ultimo lote salga (o al tiempo).</summary>
    internal async Task<bool> DrainAsync(TimeSpan timeout)
    {
        var clock = System.Diagnostics.Stopwatch.StartNew();
        while (clock.Elapsed < timeout)
        {
            if (Interlocked.Read(ref _done) >= Interlocked.Read(ref _queued) && !_failing) return true;
            await Task.Delay(20).ConfigureAwait(false);
        }
        return false;
    }

    public void Dispose()
    {
        lock (_gate) StopLocked();
    }
}
