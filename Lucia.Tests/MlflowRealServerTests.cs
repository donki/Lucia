using SocLucia.Engine;
using SocLucia.Services;
using SocLucia.Services.Mlflow;

namespace SocLucia.Tests;

/// <summary>
/// Integracion contra un MLflow de verdad, solo si se pide (constitucion General 8.6): con
/// <c>LUCIA_MLFLOW_URI=http://127.0.0.1:5055</c> (un <c>mlflow server</c> local) manda una interaccion
/// completa, una congelacion y eventos, y comprueba que el servidor acepta las trazas. Con
/// <c>LUCIA_MLFLOW_TEXT=1</c> manda tambien el texto. Sin la variable no hace nada.
/// </summary>
public class MlflowRealServerTests
{
    [Fact]
    public async Task Contra_un_MLflow_de_verdad_si_se_pide()
    {
        if (Environment.GetEnvironmentVariable("LUCIA_MLFLOW_URI") is not { Length: > 0 } uri) return;
        var text = Environment.GetEnvironmentVariable("LUCIA_MLFLOW_TEXT") == "1";
        var config = new RunConfig("Qwen3.5 4B", "Qwen3.5-4B-Q4_K_M.gguf", 8192, 2048, false, 0.7, "cuda", "2026.10.21.3", "RTX 3050 Ti");
        using var telemetry = new MlflowTelemetry(null, TimeSpan.FromMilliseconds(200)) { ConfigNow = () => config };
        telemetry.Configure(new MlflowSettings { Enabled = true, TrackingUri = uri, Experiment = "sOC Lucia (pruebas)", InstallId = "lucia-pruebas", SendText = text });

        var t = new InteractionTrace("chat", "conversacion-de-prueba", telemetry.InstallId, config, telemetry.SendText, "¿Qué dice mi contrato de alquiler sobre la fianza?", 49, 0);
        InteractionTrace.End(t.Engine());
        var docs = t.Child("documents", "RETRIEVER");
        t.Documents(docs, "Header\n[contratos\\alquiler.pdf]\nLa fianza es de dos meses.\n");
        var llm = t.Llm(1, 3, false);
        t.EndLlm(llm, 640, new ChatUsage(410, 22, 48.5), "Voy a leer el contrato.", 0, 1, "tool_calls", true, null);
        var tool = t.Tool("read_document", "{\"name\":\"contratos/alquiler.pdf\"}");
        t.EndTool(tool, "La fianza es de dos meses.", "ok");
        var llm2 = t.Llm(2, 5, false);
        t.EndLlm(llm2, 210, new ChatUsage(520, 31, 49.1), "Según tu contrato, la fianza son dos meses.", 0, 0, "stop", true, null);
        t.Event("chat", "motor listo a los 3 ms");
        t.Finish("ok", null, "Según tu contrato, la fianza son dos meses.", 640, new ChatUsage(930, 53, 49.1), 44, 1);
        telemetry.Trace(t);
        telemetry.Request(config, new RequestRecord("chat", 640, 2100, 930, 53, 49.1, 44, 0, 1, "ok"));
        DiagLog.Write("chat", "evento de prueba para MLflow");
        telemetry.Freeze(2.4);

        var cancelled = new InteractionTrace("chat", "conversacion-de-prueba", telemetry.InstallId, config, telemetry.SendText, "Cuéntame algo largo", 19, 0);
        cancelled.Llm(1, 3, false);
        cancelled.Finish("cancelled", null, null, null, null, 0, 0);
        telemetry.Trace(cancelled);

        Assert.True(await telemetry.DrainAsync(TimeSpan.FromSeconds(60)), "no se ha podido enviar");
        Assert.Equal(MlflowMode.Traces, telemetry.Mode);
        File.WriteAllText(Path.Combine(Path.GetTempPath(), "lucia-mlflow-traces.txt"), string.Join("\n", t.Data.Hex, cancelled.Data.Hex, telemetry.ServerVersion?.ToString() ?? "?"));
    }
}
