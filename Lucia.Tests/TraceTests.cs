using System.Net;
using System.Net.Http;
using System.Text;
using SocLucia.Engine;
using SocLucia.Services;
using SocLucia.Services.Mlflow;

namespace SocLucia.Tests;

public class OtlpTests
{
    [Fact]
    public void Lo_codificado_se_lee_igual_con_un_lector_independiente()
    {
        var trace = new TraceData { TraceId = Enumerable.Range(1, 16).Select(i => (byte)i).ToArray() };
        var start = DateTimeOffset.FromUnixTimeMilliseconds(1_700_000_000_123).AddTicks(4567);
        var root = new SpanData { SpanId = [1, 1, 1, 1, 1, 1, 1, 1], Name = "chat", Start = start, End = start.AddSeconds(2), Ok = true };
        root.Set("texto", "ñandú «x»").Set("entero", 42L).Set("negativo", -5).Set("real", 0.7).Set("si", true)
            .Set("mapa", new Dictionary<string, object?> { ["a"] = 1L, ["b"] = new List<object?> { "x", 2L } }).Set("nulo", null);
        root.Events.Add((start.AddSeconds(1), "chat", [new("message", "motor listo")]));
        var child = new SpanData { SpanId = [2, 2, 2, 2, 2, 2, 2, 2], ParentId = root.SpanId, Name = "tool x", Start = start, End = start.AddSeconds(-1), Ok = false, StatusMessage = "falló" };
        trace.Spans.Add(root);
        trace.Spans.Add(child);
        var spans = OtlpReader.Spans(Otlp.Encode([trace], "2026.10.21.3"));
        Assert.Equal(2, spans.Count);
        var r = spans[0];
        Assert.Equal("0102030405060708090a0b0c0d0e0f10", r.TraceId);
        Assert.Equal("0101010101010101", r.SpanId);
        Assert.Null(r.ParentId);
        Assert.Equal(1_700_000_000_123_456_700UL, r.StartNanos);   // con los microsegundos
        Assert.Equal(r.StartNanos + 2_000_000_000UL, r.EndNanos);
        Assert.Equal("ñandú «x»", r.Attributes["texto"]);
        Assert.Equal(42L, r.Attributes["entero"]);
        Assert.Equal(-5L, r.Attributes["negativo"]);
        Assert.Equal(0.7, r.Attributes["real"]);
        Assert.Equal(true, r.Attributes["si"]);
        var map = Assert.IsType<Dictionary<string, object?>>(r.Attributes["mapa"]);
        Assert.Equal(1L, map["a"]);
        Assert.Equal(new List<object?> { "x", 2L }, map["b"]);
        Assert.False(r.Attributes.ContainsKey("nulo"));
        Assert.Equal(("chat", "motor listo"), (r.Events.Single().Name, r.Events.Single().Attributes["message"]));
        Assert.Equal(1, r.StatusCode);
        var c = spans[1];
        Assert.Equal(r.SpanId, c.ParentId);
        Assert.Equal(c.StartNanos, c.EndNanos);   // un fin anterior al inicio se corrige
        Assert.Equal((2, "falló"), (c.StatusCode, c.StatusMessage));
    }
}

public class InteractionTraceTests
{
    private static readonly RunConfig Config = new("Qwen3.5 4B", "q.gguf", 8192, 2048, false, 0.7, "cuda", "2026.10.21.3", "RTX");
    private const string Question = "¿Qué dice mi contrato de alquiler?";
    private const string Answer = "Dice que la fianza son dos meses.";
    private const string Context = "The user keeps documents in `C:\\x`.\nExcerpts…\n[contratos\\alquiler.pdf]\nLa fianza es de dos meses.\n\n[notas.md]\nalgo\n";

    private static (InteractionTrace Trace, List<OtlpSpan> Spans, string Raw) Build(bool text, string outcome = "ok", string? error = null)
    {
        var t = new InteractionTrace("chat", "conv123", "lucia-abc", Config, text, Question, Question.Length, 1);
        var engine = t.Engine();
        InteractionTrace.End(engine);
        var docs = t.Child("documents", "RETRIEVER");
        t.Documents(docs, Context);
        var llm = t.Llm(1, 4, false);
        t.EndLlm(llm, 850, new ChatUsage(120, 40, 25), "Voy a mirar.", 0, 1, "tool_calls", true, null);
        var tool = t.Tool("read_document", "{\"name\":\"contratos/alquiler.pdf\"}");
        t.EndTool(tool, "La fianza es de dos meses.", "ok");
        var llm2 = t.Llm(2, 6, false);
        if (outcome == "ok") t.EndLlm(llm2, 300, new ChatUsage(200, 20, 30), Answer, 0, 0, "stop", true, null);
        t.Event("chat", "motor listo a los 3 ms");
        var data = t.Finish(outcome, error, outcome == "ok" ? Answer : null, 850, new ChatUsage(320, 60, 30), outcome == "ok" ? Answer.Length : 0, 1);
        var bytes = Otlp.Encode([data], "x");
        return (t, OtlpReader.Spans(bytes), Encoding.UTF8.GetString(bytes));
    }

    [Fact]
    public void La_raiz_es_CHAT_MODEL_con_sus_hijos_tipados_y_colgando_de_ella()
    {
        var (_, spans, _) = Build(text: false);
        var root = spans.Single(s => s.ParentId is null);
        Assert.Equal(("chat", "CHAT_MODEL"), (root.Name, root.Type));
        Assert.All(spans.Where(s => s != root), s => Assert.Equal(root.SpanId, s.ParentId));
        Assert.All(spans, s => Assert.Equal(root.TraceId, s.TraceId));
        Assert.Equal(["TASK", "RETRIEVER", "LLM", "TOOL", "LLM"], spans.Where(s => s != root).Select(s => s.Type!).ToArray());
        Assert.Equal("conv123", root.Attributes["session.id"]);
        Assert.Equal("lucia-abc", root.Attributes["user.id"]);
        Assert.Equal("Qwen3.5 4B", root.Attributes["mlflow.llm.model"]);
        Assert.Equal(0.7, root.Attributes["gen_ai.request.temperature"]);
        Assert.Equal(2048L, root.Attributes["gen_ai.request.max_tokens"]);
        Assert.Equal(8192L, root.Attributes["lucia.context_tokens"]);
        Assert.Equal(850.0, root.Attributes["lucia.ttft_ms"]);
        Assert.Equal("chat", root.Attributes["lucia.mode"]);
        var usage = Assert.IsType<Dictionary<string, object?>>(root.Attributes["mlflow.chat.tokenUsage"]);
        Assert.Equal((320L, 60L, 380L), ((long)usage["input_tokens"]!, (long)usage["output_tokens"]!, (long)usage["total_tokens"]!));
        Assert.Equal(30.0, root.Attributes["lucia.tokens_per_s"]);
        Assert.Equal(1, root.StatusCode);
        Assert.Contains(root.Events, e => e.Name == "chat");
        var docs = spans.Single(s => s.Type == "RETRIEVER");
        var outputs = Assert.IsType<Dictionary<string, object?>>(docs.Attributes["mlflow.spanOutputs"]);
        Assert.Equal(new List<object?> { "contratos\\alquiler.pdf", "notas.md" }, outputs["documents"]);
        var tool = spans.Single(s => s.Type == "TOOL");
        Assert.Equal(("tool read_document", "read_document"), (tool.Name, tool.Attributes["gen_ai.tool.name"]));
    }

    [Fact]
    public void Sin_la_casilla_no_sale_ningun_texto_solo_longitudes()
    {
        var (t, spans, raw) = Build(text: false);
        Assert.False(t.IncludesText);
        Assert.DoesNotContain("contrato de alquiler", raw);
        Assert.DoesNotContain("fianza", raw);              // ni la respuesta, ni los documentos, ni la salida de la herramienta
        Assert.DoesNotContain("Voy a mirar", raw);
        Assert.DoesNotContain("\"name\":\"contratos", raw); // ni los argumentos de la herramienta
        var inputs = Assert.IsType<Dictionary<string, object?>>(spans[0].Attributes["mlflow.spanInputs"]);
        Assert.Equal((long)Question.Length, inputs["question_chars"]);
        Assert.False(inputs.ContainsKey("messages"));
    }

    [Fact]
    public void Con_la_casilla_la_pregunta_y_la_respuesta_van_en_entradas_y_salidas()
    {
        var (_, spans, _) = Build(text: true);
        var root = spans[0];
        var inputs = Assert.IsType<Dictionary<string, object?>>(root.Attributes["mlflow.spanInputs"]);
        var message = Assert.IsType<Dictionary<string, object?>>(Assert.IsType<List<object?>>(inputs["messages"]).Single());
        Assert.Equal(("user", Question), (message["role"], message["content"]));
        var outputs = Assert.IsType<Dictionary<string, object?>>(root.Attributes["mlflow.spanOutputs"]);
        Assert.Contains(Answer, outputs["choices"]!.ToString() + string.Join("", ((List<object?>)outputs["choices"]!).Select(c => ((Dictionary<string, object?>)((Dictionary<string, object?>)c!)["message"]!)["content"])));
        var docs = (Dictionary<string, object?>)spans.Single(s => s.Type == "RETRIEVER").Attributes["mlflow.spanOutputs"]!;
        Assert.Equal(Context, docs["context"]);
    }

    [Fact]
    public void Error_y_cancelacion_quedan_marcados_y_cierran_lo_abierto()
    {
        var (_, spans, _) = Build(text: false, outcome: "error", error: "HttpRequestException: 500");
        var root = spans[0];
        Assert.Equal((2, "HttpRequestException: 500"), (root.StatusCode, root.StatusMessage));
        Assert.Equal("error", root.Attributes["lucia.outcome"]);
        Assert.Equal(2, spans.Last().StatusCode);   // la vuelta que no acabo, cerrada con el error
        var (_, cancelled, _) = Build(text: false, outcome: "cancelled");
        Assert.Equal(1, cancelled[0].StatusCode);   // cancelar no es un fallo del sistema…
        Assert.Equal("cancelled", cancelled[0].Attributes["lucia.outcome"]);   // …pero queda dicho
        Assert.Contains(cancelled[0].Events, e => e.Name == "cancelled");
    }

    [Fact]
    public void Una_congelacion_es_una_traza_propia()
    {
        var spans = OtlpReader.Spans(Otlp.Encode([InteractionTrace.Freeze(3.5, "repintando", "lucia-abc", "v")], "v"));
        var s = spans.Single();
        Assert.Equal(("ui freeze", 2, 3.5), (s.Name, s.StatusCode, (double)s.Attributes["lucia.freeze_s"]!));
        Assert.Equal(3_500_000_000UL, s.EndNanos - s.StartNanos);
    }
}

public class MlflowTracesTests : IDisposable
{
    private readonly FakeMlflow _fake = new();
    private readonly MlflowTelemetry _telemetry;
    private static readonly RunConfig Config = new("Qwen3.5 4B", "q.gguf", 8192, 2048, false, 0.7, "cuda", "2026.10.21.3", "RTX");

    public MlflowTracesTests()
    {
        _telemetry = new MlflowTelemetry(_fake, TimeSpan.FromMilliseconds(50), maxBackoff: TimeSpan.FromMilliseconds(200)) { ConfigNow = () => Config };
        if (File.Exists(DiagLog.Current)) File.Delete(DiagLog.Current);
    }

    public void Dispose() => _telemetry.Dispose();

    private static MlflowSettings Settings(bool text = false) => new() { Enabled = true, TrackingUri = "http://mlflow.test:5000", Experiment = "Pruebas", InstallId = "lucia-xyz", SendText = text };

    private InteractionTrace Interaction(bool text)
    {
        var t = new InteractionTrace("chat", "conv1", _telemetry.InstallId, Config, text, "pregunta secreta", 16, 0);
        var llm = t.Llm(1, 2, false);
        t.EndLlm(llm, 100, new ChatUsage(10, 5, 20), "respuesta secreta", 0, 0, "stop", true, null);
        t.Finish("ok", null, "respuesta secreta", 100, new ChatUsage(10, 5, 20), 17, 0);
        return t;
    }

    private async Task Drained() => Assert.True(await _telemetry.DrainAsync(TimeSpan.FromSeconds(10)), "no se ha enviado");

    [Fact]
    public async Task Con_MLflow_3_las_interacciones_van_como_trazas_al_experimento()
    {
        _telemetry.Configure(Settings());
        _telemetry.Request(Config, new RequestRecord("chat", 100, 500, 10, 5, 20, 17, 0, 0, "ok"));
        _telemetry.Trace(Interaction(text: false));
        await Drained();
        Assert.Equal(MlflowMode.Traces, _telemetry.Mode);
        Assert.Equal(new Version(3, 7, 0), _telemetry.ServerVersion);
        var call = _fake.Of("/v1/traces").Single();
        Assert.Equal("7", call.Headers!["x-mlflow-experiment-id"]);
        Assert.Equal("application/x-protobuf", call.Headers["content-type"]);
        var spans = _fake.Spans;
        Assert.Equal(2, spans.Count);
        Assert.Equal("CHAT_MODEL", spans[0].Type);
        Assert.Equal("lucia-xyz", spans[0].Attributes["user.id"]);
        Assert.DoesNotContain("secreta", Encoding.UTF8.GetString(call.Raw!));
        Assert.Contains("interacciones como trazas", File.ReadAllText(DiagLog.Current));
        Assert.DoesNotContain(_fake.Batches.SelectMany(b => b["tags"]!.AsArray()), t => t!["key"]!.GetValue<string>().EndsWith(".question"));
    }

    [Fact]
    public async Task Con_el_texto_permitido_la_traza_lo_lleva_y_sin_el_no_sale_aunque_se_arme()
    {
        _telemetry.Configure(Settings(text: true));
        _telemetry.Trace(Interaction(text: true));
        await Drained();
        Assert.Contains("pregunta secreta", Encoding.UTF8.GetString(_fake.Of("/v1/traces").Single().Raw!));
        using var off = new MlflowTelemetry(_fake, TimeSpan.FromMilliseconds(50)) { ConfigNow = () => Config };
        off.Configure(Settings(text: false));
        off.Trace(Interaction(text: true));   // armada con texto por error: no se envia
        await Task.Delay(300);
        Assert.Single(_fake.Of("/v1/traces"));
    }

    [Fact]
    public async Task Con_MLflow_2_van_como_ejecuciones_y_lo_dice_una_vez()
    {
        _fake.Version = "2.17.2";
        _telemetry.Configure(Settings(text: true));
        _telemetry.Trace(Interaction(text: true));
        _telemetry.Request(Config, new RequestRecord("chat", 100, 500, 10, 5, 20, 17, 0, 0, "ok", null, "pregunta", "respuesta"));
        await Drained();
        Assert.Equal(MlflowMode.Runs, _telemetry.Mode);
        Assert.Empty(_fake.Of("/v1/traces"));
        Assert.Contains(_fake.Batches.SelectMany(b => b["tags"]!.AsArray()), t => t!["key"]!.GetValue<string>() == "chat.0001.question");
        Assert.Contains("MLflow 2.17.2 no tiene trazas", File.ReadAllText(DiagLog.Current));
    }

    [Fact]
    public async Task Sin_version_se_prueban_las_trazas_y_si_no_las_admite_se_pasa_a_ejecuciones()
    {
        _fake.Version = null;
        _fake.Traces = false;
        _telemetry.Configure(Settings());
        _telemetry.Trace(Interaction(text: false));
        _telemetry.Request(Config, new RequestRecord("chat", 100, 500, 10, 5, 20, 17, 0, 0, "ok"));
        await Drained();
        _telemetry.Trace(Interaction(text: false));
        await Drained();
        Assert.Equal(MlflowMode.Runs, _telemetry.Mode);
        Assert.Single(_fake.Of("/v1/traces"));   // se probo una vez y ya no se insiste
        var log = File.ReadAllText(DiagLog.Current);
        Assert.Contains("no dice su version", log);
        Assert.Single(System.Text.RegularExpressions.Regex.Matches(log, "no admite trazas"));
        Assert.Contains(_fake.Batches.SelectMany(b => b["metrics"]!.AsArray()), m => m!["key"]!.GetValue<string>() == "chat.duration_ms");
    }

    [Fact]
    public async Task Una_congelacion_llega_como_traza_y_como_metrica()
    {
        _telemetry.Configure(Settings());
        _telemetry.Freeze(4.2);
        await Drained();
        Assert.Equal("ui freeze", _fake.Spans.Single().Name);
        Assert.Contains(_fake.Batches.SelectMany(b => b["metrics"]!.AsArray()), m => m!["key"]!.GetValue<string>() == "ui_freeze_s");
    }

    [Theory]
    [InlineData("3.7.0", "3.7.0", MlflowMode.Traces)]
    [InlineData("3.6.0", "3.6.0", MlflowMode.Traces)]
    [InlineData("\"3.10.1\"", "3.10.1", MlflowMode.Traces)]
    [InlineData("3.5.1", "3.5.1", MlflowMode.Runs)]
    [InlineData("2.17.2", "2.17.2", MlflowMode.Runs)]
    [InlineData("3.8.0rc1", "3.8.0", MlflowMode.Traces)]
    [InlineData("<html>", null, MlflowMode.Unknown)]
    [InlineData(null, null, MlflowMode.Unknown)]
    public void La_version_decide_el_modo(string? text, string? version, MlflowMode mode)
    {
        var parsed = MlflowApi.ParseVersion(text);
        Assert.Equal(version, parsed?.ToString());
        Assert.Equal(mode, MlflowApi.ModeFor(parsed));
    }

    [Fact]
    public async Task La_prueba_de_conexion_dice_la_version_y_el_modo()
    {
        var (result, _, version, mode) = await MlflowApi.CheckFullAsync(new HttpClient(new FakeMlflow { ExperimentExists = true, Version = "3.7.0" }), "http://mlflow.test:5000", "x", null, null, null, default);
        Assert.Equal((MlflowCheck.Ok, "3.7.0", MlflowMode.Traces), (result, version?.ToString(), mode));
        var old = await MlflowApi.CheckFullAsync(new HttpClient(new FakeMlflow { Version = "2.9.0" }), "http://mlflow.test:5000", "x", null, null, null, default);
        Assert.Equal((MlflowCheck.OkWillCreate, MlflowMode.Runs), (old.Result, old.Mode));
    }

    [Theory]
    [InlineData(404, "<html>", false)]
    [InlineData(405, "", false)]
    [InlineData(415, "", false)]
    [InlineData(501, "REST OTLP span logging is not supported", false)]
    [InlineData(400, "Invalid Content-Type: application/x-protobuf. Expected: application/json", false)]
    public async Task Las_respuestas_de_no_admitidas_dan_false(int status, string body, bool expected)
    {
        var fake = new FakeMlflow { Override = r => r.RequestUri!.AbsolutePath == "/v1/traces" ? new HttpResponseMessage((HttpStatusCode)status) { Content = new StringContent(body) } : null };
        var api = new MlflowApi(new HttpClient(fake), new Uri("http://mlflow.test:5000"), null, null, null);
        Assert.Equal(expected, await api.ExportTracesAsync("7", null, [0x0A, 0x00], default));
        var broken = new FakeMlflow { Override = _ => new HttpResponseMessage(HttpStatusCode.InternalServerError) { Content = new StringContent("{\"error_code\":\"INTERNAL_ERROR\"}") } };
        await Assert.ThrowsAsync<MlflowException>(() => new MlflowApi(new HttpClient(broken), new Uri("http://mlflow.test:5000"), null, null, null).ExportTracesAsync("7", null, [0x0A, 0x00], default));
    }
}

public class DoorTextTests
{
    [Theory]
    [InlineData("""{"messages":[{"role":"system","content":"s"},{"role":"user","content":"hola"},{"role":"assistant","content":"x"},{"role":"user","content":"adios"}]}""", "adios")]
    [InlineData("""{"messages":[{"role":"user","content":[{"type":"text","text":"uno"},{"type":"image_url","image_url":{"url":"x"}},{"type":"text","text":"dos"}]}]}""", "uno\ndos")]
    [InlineData("""{"prompt":"x"}""", null)]
    [InlineData("no es json", null)]
    public void El_ultimo_mensaje_del_usuario(string body, string? expected)
        => Assert.Equal(expected, SocLucia.Editor.EditorDoor.LastUserText(Encoding.UTF8.GetBytes(body)));
}
