using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json.Nodes;
using SocLucia.Engine;
using SocLucia.Services;
using SocLucia.Services.Mlflow;

namespace SocLucia.Tests;

/// <summary>Un servidor MLflow de mentira, en memoria: experimentos, runs, log-batch, update y el proxy de artefactos.</summary>
internal sealed class FakeMlflow : HttpMessageHandler
{
    public sealed record Call(string Method, string Path, JsonObject? Body, string? Auth, byte[]? Raw);

    public List<Call> Calls { get; } = [];
    public bool ExperimentExists { get; set; }
    public bool Artifacts { get; set; } = true;
    public string? ArtifactUri { get; set; }
    public volatile bool Down;
    public Func<HttpRequestMessage, HttpResponseMessage?>? Override { get; set; }
    /// <summary>Tarda esto en contestar (respetando la cancelacion, como un servidor de verdad).</summary>
    public TimeSpan Delay { get; set; }
    public Dictionary<string, string> Files { get; } = [];
    private int _runs;

    public List<Call> Of(string pathEnd) { lock (Calls) return Calls.Where(c => c.Path.EndsWith(pathEnd, StringComparison.Ordinal)).ToList(); }

    public IEnumerable<JsonObject> Batches => Of("runs/log-batch").Select(c => c.Body!);

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancel)
    {
        if (Down) throw new HttpRequestException(HttpRequestError.ConnectionError, "refused");
        if (Delay > TimeSpan.Zero) await Task.Delay(Delay, cancel);
        var raw = request.Content is null ? null : await request.Content.ReadAsByteArrayAsync(cancel);
        JsonObject? body = null;
        if (raw is { Length: > 0 } && request.Method != HttpMethod.Put) body = JsonNode.Parse(raw) as JsonObject;
        var path = request.RequestUri!.AbsolutePath;
        lock (Calls) Calls.Add(new Call(request.Method.Method, path + request.RequestUri.Query, body, request.Headers.Authorization?.ToString(), raw));
        if (Override?.Invoke(request) is { } custom) return custom;
        HttpResponseMessage Json(int status, string json) => new((HttpStatusCode)status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
        if (path.EndsWith("/experiments/get-by-name", StringComparison.Ordinal))
            return ExperimentExists ? Json(200, """{"experiment":{"experiment_id":"7","name":"x"}}""")
                : Json(404, """{"error_code":"RESOURCE_DOES_NOT_EXIST","message":"no existe"}""");
        if (path.EndsWith("/experiments/create", StringComparison.Ordinal)) { ExperimentExists = true; return Json(200, """{"experiment_id":"7"}"""); }
        if (path.EndsWith("/runs/create", StringComparison.Ordinal))
        {
            var id = "run" + Interlocked.Increment(ref _runs);
            return Json(200, new JsonObject { ["run"] = new JsonObject { ["info"] = new JsonObject { ["run_id"] = id, ["artifact_uri"] = ArtifactUri ?? $"mlflow-artifacts:/7/{id}/artifacts" } } }.ToJsonString());
        }
        if (path.EndsWith("/runs/log-batch", StringComparison.Ordinal) || path.EndsWith("/runs/update", StringComparison.Ordinal)) return Json(200, "{}");
        if (path.Contains("/mlflow-artifacts/artifacts/", StringComparison.Ordinal) && request.Method == HttpMethod.Put)
        {
            if (!Artifacts) return new HttpResponseMessage(HttpStatusCode.NotFound);
            lock (Files) Files[path] = Encoding.UTF8.GetString(raw ?? []);
            return new HttpResponseMessage(HttpStatusCode.OK);
        }
        return new HttpResponseMessage(HttpStatusCode.NotFound) { Content = new StringContent("<html>no</html>") };
    }
}

public class MlflowApiTests
{
    private static readonly Uri Base = new("http://mlflow.test:5000/");

    [Theory]
    [InlineData("http://localhost:5000", true)]
    [InlineData("https://mlflow.empresa.com/base/", true)]
    [InlineData("  http://127.0.0.1:5000  ", true)]
    [InlineData("ftp://x", false)]
    [InlineData("localhost:5000", false)]
    [InlineData("mlflow", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void Solo_valen_direcciones_http_y_https(string? text, bool ok) => Assert.Equal(ok, MlflowApi.TryUri(text, out _));

    [Fact]
    public async Task Crea_el_experimento_si_no_existe_y_el_run_con_su_nombre()
    {
        var fake = new FakeMlflow();
        var api = new MlflowApi(new HttpClient(fake), Base, "ana", "clave", null);
        Assert.Equal("7", await api.EnsureExperimentAsync("sOC Lucia", default));
        Assert.Equal("/api/2.0/mlflow/experiments/get-by-name?experiment_name=sOC%20Lucia", fake.Calls[0].Path);
        Assert.Equal("sOC Lucia", fake.Of("experiments/create").Single().Body!["name"]!.GetValue<string>());
        Assert.Equal("Basic " + Convert.ToBase64String(Encoding.UTF8.GetBytes("ana:clave")), fake.Calls[0].Auth);
        var (id, artifacts) = await api.CreateRunAsync("7", "Lucia hoy", DateTimeOffset.FromUnixTimeMilliseconds(1000), [new("a", "b")], default);
        Assert.Equal("run1", id);
        Assert.Equal("mlflow-artifacts:/7/run1/artifacts", artifacts);
        var body = fake.Of("runs/create").Single().Body!;
        Assert.Equal(1000, body["start_time"]!.GetValue<long>());
        Assert.Contains(body["tags"]!.AsArray(), t => t!["key"]!.GetValue<string>() == "mlflow.runName" && t["value"]!.GetValue<string>() == "Lucia hoy");
        // Ya existe (otro lo creo a la vez): se vuelve a buscar.
        var race = new FakeMlflow { Override = r => r.RequestUri!.AbsolutePath.EndsWith("create", StringComparison.Ordinal) ? new HttpResponseMessage(HttpStatusCode.BadRequest) { Content = new StringContent("""{"error_code":"RESOURCE_ALREADY_EXISTS"}""") } : null };
        var api2 = new MlflowApi(new HttpClient(race), Base, null, null, null);
        await Assert.ThrowsAsync<MlflowException>(() => api2.EnsureExperimentAsync("x", default));   // sigue sin aparecer: error
        Assert.Null(race.Calls[0].Auth);
    }

    [Fact]
    public void El_token_manda_sobre_usuario_y_contrasena()
    {
        var fake = new FakeMlflow { ExperimentExists = true };
        var api = new MlflowApi(new HttpClient(fake), Base, "ana", "clave", "tok-123");
        _ = api.FindExperimentAsync("x", default).Result;
        Assert.Equal("Bearer tok-123", fake.Calls.Single().Auth);
    }

    [Fact]
    public void El_lote_tiene_la_forma_de_MLflow_y_recorta_lo_largo()
    {
        var at = DateTimeOffset.FromUnixTimeMilliseconds(5000);
        var body = MlflowApi.BatchBody("r1",
            [new("chat.ttft_ms", 120.5, at, 3), new("raro", double.NaN, at, 1)],
            [new("model", "Qwen")],
            [new("chat.0003.answer", new string('x', 6000))]);
        Assert.Equal("r1", body["run_id"]!.GetValue<string>());
        var metric = body["metrics"]!.AsArray().Single()!;   // el NaN no se manda (MLflow lo rechaza)
        Assert.Equal(("chat.ttft_ms", 120.5, 5000L, 3L), (metric["key"]!.GetValue<string>(), metric["value"]!.GetValue<double>(), metric["timestamp"]!.GetValue<long>(), metric["step"]!.GetValue<long>()));
        Assert.Equal("Qwen", body["params"]![0]!["value"]!.GetValue<string>());
        var cut = body["tags"]![0]!["value"]!.GetValue<string>();
        Assert.Equal(MlflowApi.MaxValue, cut.Length);
        Assert.EndsWith("…", cut);
    }

    [Fact]
    public async Task Los_artefactos_van_por_el_proxy_y_si_no_lo_hay_no_es_un_error()
    {
        var fake = new FakeMlflow();
        var api = new MlflowApi(new HttpClient(fake), Base, null, null, null);
        Assert.True(await api.UploadArtifactAsync("mlflow-artifacts:/7/run1/artifacts", "events.log", Encoding.UTF8.GetBytes("hola"), default));
        Assert.Equal("hola", fake.Files["/api/2.0/mlflow-artifacts/artifacts/7/run1/artifacts/events.log"]);
        Assert.True(await api.UploadArtifactAsync("mlflow-artifacts://mlflow.test:5000/api/2.0/mlflow-artifacts/artifacts/7/run2/artifacts", "e.log", [1], default));
        Assert.Contains("/api/2.0/mlflow-artifacts/artifacts/7/run2/artifacts/e.log", fake.Files.Keys);
        Assert.False(await api.UploadArtifactAsync("s3://cubo/7/run1/artifacts", "events.log", [1], default));
        Assert.False(await api.UploadArtifactAsync(null, "events.log", [1], default));
        fake.Artifacts = false;
        Assert.False(await api.UploadArtifactAsync("mlflow-artifacts:/7/run1/artifacts", "events.log", [1], default));
        fake.Override = _ => new HttpResponseMessage(HttpStatusCode.Unauthorized);
        await Assert.ThrowsAsync<MlflowException>(() => api.UploadArtifactAsync("mlflow-artifacts:/7/run1/artifacts", "events.log", [1], default));
    }

    private static Task<(MlflowCheck, string?)> Check(HttpMessageHandler handler, string uri = "http://mlflow.test:5000", string? token = null)
        => MlflowApi.CheckAsync(new HttpClient(handler) { Timeout = TimeSpan.FromMilliseconds(500) }, uri, "sOC Lucia", "ana", "clave", token, default);

    private static FakeMlflow Status(int status, string body = "{}") => new() { Override = _ => new HttpResponseMessage((HttpStatusCode)status) { Content = new StringContent(body) } };

    [Fact]
    public async Task La_prueba_de_conexion_dice_que_pasa()
    {
        Assert.Equal(MlflowCheck.Ok, (await Check(new FakeMlflow { ExperimentExists = true })).Item1);
        var create = new FakeMlflow();
        Assert.Equal(MlflowCheck.OkWillCreate, (await Check(create)).Item1);
        Assert.Empty(create.Of("experiments/create"));   // probar no crea nada
        Assert.Equal(MlflowCheck.BadAddress, (await Check(new FakeMlflow(), "localhost:5000")).Item1);
        Assert.Equal(MlflowCheck.Unauthorized, (await Check(Status(401))).Item1);
        Assert.Equal(MlflowCheck.Forbidden, (await Check(Status(403))).Item1);
        Assert.Equal((MlflowCheck.NotMlflow, "HTTP 404"), await Check(Status(404, "<html>nginx</html>")));
        Assert.Equal((MlflowCheck.NotMlflow, "HTTP 200"), await Check(Status(200, "<html>no es json</html>")));
        Assert.Equal((MlflowCheck.ServerError, "HTTP 502"), await Check(Status(502)));
        Assert.Equal(MlflowCheck.Unreachable, (await Check(new FakeMlflow { Down = true })).Item1);
        var slow = new FakeMlflow { Delay = TimeSpan.FromSeconds(3) };
        Assert.Equal(MlflowCheck.Timeout, (await Check(slow)).Item1);
    }

    [Fact]
    public async Task Sin_servidor_en_esa_direccion_no_se_llega()
    {
        var port = Sandbox.FreePort();   // nadie escucha
        var (result, detail) = await MlflowApi.CheckAsync(new HttpClient { Timeout = TimeSpan.FromSeconds(5) }, $"http://127.0.0.1:{port}", "x", null, null, null, default);
        Assert.Equal(MlflowCheck.Unreachable, result);
        Assert.Equal("ConnectionError", detail);
    }
}

public class SecretsTests
{
    [Fact]
    public void Se_guardan_cifrados_para_este_usuario_y_se_recuperan()
    {
        var stored = Secrets.Protect("mi-token-secreto");
        Assert.StartsWith("dpapi1:", stored);
        Assert.DoesNotContain("mi-token-secreto", stored);
        Assert.Equal("mi-token-secreto", Secrets.Unprotect(stored));
        Assert.Equal(string.Empty, Secrets.Protect(""));
        Assert.Equal(string.Empty, Secrets.Unprotect(null));
        Assert.Equal(string.Empty, Secrets.Unprotect("en claro"));
        Assert.Equal(string.Empty, Secrets.Unprotect("dpapi1:no-es-base64!"));
        Assert.Equal(string.Empty, Secrets.Unprotect("dpapi1:" + Convert.ToBase64String([1, 2, 3])));
    }
}

public class ChatUsageTests
{
    [Fact]
    public void Lee_el_uso_y_los_tiempos_de_llama_server()
    {
        var u = ChatUsage.From(JsonNode.Parse("""{"usage":{"prompt_tokens":120,"completion_tokens":40},"timings":{"prompt_n":121,"predicted_n":41,"predicted_per_second":23.5}}"""))!;
        Assert.Equal((120, 40, 23.5), (u.PromptTokens!.Value, u.CompletionTokens!.Value, u.TokensPerSecond!.Value));
        var onlyTimings = ChatUsage.From(JsonNode.Parse("""{"timings":{"prompt_n":10,"predicted_n":5}}"""))!;
        Assert.Equal((10, 5), (onlyTimings.PromptTokens!.Value, onlyTimings.CompletionTokens!.Value));
        Assert.Null(onlyTimings.TokensPerSecond);
        Assert.Null(ChatUsage.From(JsonNode.Parse("""{"choices":[]}""")));
        Assert.Null(ChatUsage.From(null));
        Assert.Null(ChatUsage.From(JsonNode.Parse("""{"usage":{"completion_tokens":"x"}}"""))!.CompletionTokens);
    }

    [Fact]
    public async Task El_streaming_trae_el_uso_al_final_y_la_respuesta_lo_guarda()
    {
        using var server = new LocalServer(ctx => LocalServer.Write(ctx, 200, "text/event-stream",
            "data: {\"choices\":[{\"delta\":{\"content\":\"Hola\"}}]}\n\n" +
            "data: {\"choices\":[{\"delta\":{},\"finish_reason\":\"stop\"}],\"usage\":{\"prompt_tokens\":9,\"completion_tokens\":2},\"timings\":{\"predicted_per_second\":12.5}}\n\n" +
            "data: [DONE]\n\n"));
        var live = new SocLucia.Chat.PendingAnswer();
        await ChatClient.PumpAsync(server.BaseUrl, [new ChatMessage("user", "hola")], false, 100, null, live, default);
        Assert.Equal(new ChatUsage(9, 2, 12.5), live.Usage);
        Assert.Equal("stop", live.Read().Finish);
    }
}

public class MlflowTelemetryTests : IDisposable
{
    private readonly FakeMlflow _fake = new();
    private readonly MlflowTelemetry _telemetry;
    private static readonly RunConfig Config = new("Qwen3.5 4B", "q.gguf", 8192, 2048, false, 0.7, "cuda", "2026.10.21.2", "RTX");

    public MlflowTelemetryTests()
    {
        _telemetry = new MlflowTelemetry(_fake, TimeSpan.FromMilliseconds(50), capacity: 1000, maxBackoff: TimeSpan.FromMilliseconds(200)) { ConfigNow = () => Config };
        if (File.Exists(DiagLog.Current)) File.Delete(DiagLog.Current);
    }

    public void Dispose() => _telemetry.Dispose();

    private static MlflowSettings Settings(bool sendText = false, string? token = null, string? password = null) => new()
    {
        Enabled = true, TrackingUri = "http://mlflow.test:5000", Experiment = "Pruebas", Username = "ana",
        ProtectedPassword = Secrets.Protect(password ?? string.Empty), ProtectedToken = Secrets.Protect(token ?? string.Empty), SendText = sendText,
    };

    private static RequestRecord Record(string outcome = "ok") => new("chat", 850, 4200, 120, 64, 18.2, 300, 0, 0, outcome, outcome == "error" ? "HttpRequestException: x" : null, "¿qué tiempo hace?", "Hace sol.");

    private async Task Drained() => Assert.True(await _telemetry.DrainAsync(TimeSpan.FromSeconds(10)), "no se ha enviado");

    private List<(string Key, double Value, long Step)> Metrics() =>
        _fake.Batches.SelectMany(b => b["metrics"]!.AsArray()).Select(m => (m!["key"]!.GetValue<string>(), m["value"]!.GetValue<double>(), m["step"]!.GetValue<long>())).ToList();

    private List<(string Key, string Value)> Tags() =>
        _fake.Batches.SelectMany(b => b["tags"]!.AsArray()).Select(t => (t!["key"]!.GetValue<string>(), t["value"]!.GetValue<string>())).ToList();

    [Fact]
    public async Task Apagado_no_manda_nada()
    {
        _telemetry.Configure(new MlflowSettings { Enabled = false, TrackingUri = "http://mlflow.test:5000" });
        Assert.False(_telemetry.Enabled);
        _telemetry.Request(Config, Record());
        _telemetry.Freeze(3);
        DiagLog.Write("chat", "algo");
        _telemetry.Configure(new MlflowSettings { Enabled = true, TrackingUri = "no es una direccion" });
        Assert.False(_telemetry.Enabled);
        await Task.Delay(200);
        Assert.Empty(_fake.Calls);
    }

    [Fact]
    public async Task Una_pregunta_llega_como_run_con_parametros_metricas_eventos_y_sin_texto()
    {
        _telemetry.Configure(Settings());
        DiagLog.Write("chat", "pregunta enviada en la conversacion abc: 17 caracteres");
        _telemetry.Request(Config, Record());
        _telemetry.Freeze(2.5);
        await Drained();
        Assert.Single(_fake.Of("experiments/create"));
        Assert.Single(_fake.Of("runs/create"));
        var parameters = _fake.Batches.SelectMany(b => b["params"]!.AsArray()).ToDictionary(p => p!["key"]!.GetValue<string>(), p => p!["value"]!.GetValue<string>());
        Assert.Equal("Qwen3.5 4B", parameters["model"]);
        Assert.Equal("8192", parameters["context_tokens"]);
        Assert.Equal("2048", parameters["max_tokens"]);
        Assert.Equal("0.7", parameters["temperature"]);
        var metrics = Metrics();
        Assert.Contains(("chat.ttft_ms", 850, 1L), metrics);
        Assert.Contains(("chat.duration_ms", 4200, 1L), metrics);
        Assert.Contains(("chat.completion_tokens", 64, 1L), metrics);
        Assert.Contains(("chat.tokens_per_s", 18.2, 1L), metrics);
        Assert.Contains(("chat.error", 0, 1L), metrics);
        Assert.Contains(("ui_freeze_s", 2.5, 1L), metrics);
        // Sin la casilla del texto, la pregunta y la respuesta no salen (aunque vinieran en el registro).
        var everything = string.Join("\n", _fake.Calls.Select(c => c.Body?.ToJsonString() ?? Encoding.UTF8.GetString(c.Raw ?? [])));
        Assert.DoesNotContain("qué tiempo hace", everything);
        Assert.DoesNotContain("Hace sol", everything);
        // Los eventos del registro de diagnostico, como fichero de la ejecucion.
        var events = _fake.Files.Single(f => f.Key.EndsWith("/run1/artifacts/events.log", StringComparison.Ordinal)).Value;
        Assert.Contains("[chat] pregunta enviada en la conversacion abc", events);
        Assert.DoesNotContain("[mlflow]", events);   // lo suyo no hace eco
        Assert.Equal("FINISHED", _fake.Of("runs/update").Last().Body!["status"]!.GetValue<string>());
        Assert.Equal("Basic " + Convert.ToBase64String(Encoding.UTF8.GetBytes("ana:")), _fake.Calls[0].Auth);
    }

    [Fact]
    public async Task Con_la_casilla_el_texto_va_como_etiquetas_recortadas()
    {
        _telemetry.Configure(Settings(sendText: true));
        _telemetry.Request(Config, Record() with { Answer = new string('y', 9000) });
        await Drained();
        var tags = Tags();
        Assert.Contains(("chat.0001.question", "¿qué tiempo hace?"), tags);
        Assert.Equal(MlflowApi.MaxValue, tags.Single(t => t.Key == "chat.0001.answer").Value.Length);
    }

    [Fact]
    public async Task Si_cambia_la_configuracion_empieza_otro_run_y_los_errores_van_como_etiqueta()
    {
        _telemetry.Configure(Settings());
        _telemetry.Request(Config, Record());
        _telemetry.Request(Config with { Context = 16384 }, Record("error"));
        await Drained();
        Assert.Equal(2, _fake.Of("runs/create").Count);
        Assert.Contains(("chat.0001.error", "HttpRequestException: x"), Tags());
        Assert.Contains(("chat.error", 1, 1L), Metrics());
    }

    [Fact]
    public async Task Sin_proxy_de_artefactos_los_eventos_importantes_van_como_etiquetas()
    {
        _fake.Artifacts = false;
        _telemetry.Configure(Settings());
        DiagLog.Write("error", "no controlado: InvalidOperationException: x");
        DiagLog.Write("chat", "motor listo a los 10 ms");
        await Drained();
        var tags = Tags().Where(t => t.Key.StartsWith("event.", StringComparison.Ordinal)).Select(t => t.Value).ToList();
        Assert.Contains(tags, t => t.Contains("no controlado"));
        Assert.DoesNotContain(tags, t => t.Contains("motor listo"));
        Assert.Contains(Metrics(), m => m.Key == "log_errors");
    }

    [Fact]
    public async Task Los_secretos_viajan_solo_en_la_cabecera_y_nunca_en_el_registro()
    {
        _telemetry.Configure(Settings(token: "tok-super-secreto", password: "clave-super-secreta"));
        _telemetry.Request(Config, Record());
        await Drained();
        Assert.All(_fake.Calls, c => Assert.Equal("Bearer tok-super-secreto", c.Auth));
        var everything = string.Join("\n", _fake.Calls.Select(c => c.Body?.ToJsonString() ?? Encoding.UTF8.GetString(c.Raw ?? [])));
        Assert.DoesNotContain("super-secret", everything);
        Assert.DoesNotContain("super-secret", File.ReadAllText(DiagLog.Current));
    }

    [Fact]
    public async Task Caido_no_bloquea_avisa_una_vez_y_al_volver_manda_lo_pendiente()
    {
        _fake.Down = true;
        _telemetry.Configure(Settings());
        var clock = System.Diagnostics.Stopwatch.StartNew();
        for (var i = 0; i < 20; i++)
            _telemetry.Request(Config, Record());   // nunca espera al servidor
        Assert.True(clock.ElapsedMilliseconds < 500, $"encolar tardo {clock.ElapsedMilliseconds} ms");
        var waited = System.Diagnostics.Stopwatch.StartNew();
        while (!_telemetry.Failing && waited.ElapsedMilliseconds < 5000) await Task.Delay(20);
        await Task.Delay(800);   // varios reintentos
        var log = File.ReadAllText(DiagLog.Current);
        Assert.Equal(1, System.Text.RegularExpressions.Regex.Matches(log, "MLflow no responde").Count);
        _fake.Down = false;
        await Drained();
        Assert.Contains("MLflow vuelve a responder", File.ReadAllText(DiagLog.Current));
        Assert.Equal(20, Metrics().Count(m => m.Key == "chat.duration_ms"));
        Assert.Equal(0, _telemetry.Dropped);
    }

    [Fact]
    public async Task Con_la_cola_llena_se_tira_lo_mas_viejo_y_se_cuenta()
    {
        using var small = new MlflowTelemetry(_fake, TimeSpan.FromMilliseconds(50), capacity: 5, maxBackoff: TimeSpan.FromSeconds(30));
        _fake.Down = true;
        small.Configure(Settings());
        await Task.Delay(300);   // el envio ya esta esperando a reintentar
        for (var i = 0; i < 50; i++)
            small.Request(Config, Record());
        Assert.True(small.Dropped >= 40, $"tirados {small.Dropped}");
        small.Dispose();   // apagar con el servidor caido tampoco bloquea
    }
}
