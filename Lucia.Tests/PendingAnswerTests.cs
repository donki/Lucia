using SocLucia.Chat;
using SocLucia.Engine;
using SocLucia.Localization;

namespace SocLucia.Tests;

/// <summary>
/// La respuesta pendiente (lo que llena el hilo del streaming y lee la interfaz), el texto del
/// indicador de espera, el ritmo de repintado y el bombeo del streaming fuera del hilo que lo pide.
/// </summary>
public class PendingAnswerTests
{
    [Fact]
    public void Junta_texto_y_razonamiento_y_avanza_de_fase()
    {
        var live = new PendingAnswer();
        Assert.Equal(AnswerPhase.Starting, live.Phase);
        var empty = live.Read();
        Assert.Equal(string.Empty, empty.Content);
        Assert.Null(empty.Reasoning);

        live.Advance(AnswerPhase.Waiting);
        live.Add(new ChatDelta(null, "pienso"));
        Assert.Equal(AnswerPhase.Reasoning, live.Phase);
        live.Add(new ChatDelta("Hola", " un poco"));
        live.Add(new ChatDelta(" mundo", null));
        live.Add(new ChatDelta(null, null, null, "stop"));

        var now = live.Read();
        Assert.Equal("Hola mundo", now.Content);
        Assert.Equal("pienso un poco", now.Reasoning);
        Assert.Equal("stop", now.Finish);
        Assert.Null(now.Calls);
        Assert.Equal(AnswerPhase.Writing, now.Phase);
    }

    [Fact]
    public void La_fase_no_vuelve_atras_y_la_version_solo_cambia_si_cambia_algo()
    {
        var live = new PendingAnswer();
        var v0 = live.Read().Version;
        live.Advance(AnswerPhase.Preparing);
        var v1 = live.Read().Version;
        Assert.NotEqual(v0, v1);
        live.Advance(AnswerPhase.Preparing);   // la misma: nada
        live.Advance(AnswerPhase.Starting);    // atras: nada
        Assert.Equal(v1, live.Read().Version);
        Assert.Equal(AnswerPhase.Preparing, live.Phase);

        live.Add(new ChatDelta("ya", null));
        live.Add(new ChatDelta(null, "razono despues"));   // razonar tras escribir no la devuelve a «razonando»
        Assert.Equal(AnswerPhase.Writing, live.Phase);
        live.Advance(AnswerPhase.Waiting);
        Assert.Equal(AnswerPhase.Writing, live.Phase);
    }

    [Fact]
    public void Guarda_las_herramientas_pedidas()
    {
        var live = new PendingAnswer();
        var calls = new List<ToolCall> { new() { Id = "call_0", Name = "read_file", Arguments = "{}" } };
        live.Add(new ChatDelta(null, null, calls));
        Assert.Same(calls, live.Read().Calls);
        Assert.Equal(string.Empty, live.Read().Content);
    }

    [Fact]
    public void Al_repetir_sin_pensar_conserva_el_razonamiento_anterior()
    {
        var live = new PendingAnswer("lo que razono antes");
        Assert.Equal("lo que razono antes", live.Read().Reasoning);
        Assert.Equal(AnswerPhase.Starting, live.Phase);   // el razonamiento heredado no cuenta como fase
        live.Add(new ChatDelta("respuesta", null));
        Assert.Equal("lo que razono antes", live.Read().Reasoning);
        Assert.Equal("respuesta", live.Read().Content);
        Assert.Null(new PendingAnswer(string.Empty).Read().Reasoning);
    }

    [Fact]
    public async Task Varios_hilos_a_la_vez_no_pierden_trozos()
    {
        var live = new PendingAnswer();
        var reader = Task.Run(() =>
        {
            // Mientras se escribe, se lee como lo haria la interfaz: nunca un estado roto.
            for (var i = 0; i < 2000; i++)
                Assert.True(live.Read().Content.All(c => c == 'x'));
        });
        await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => Task.Run(() =>
        {
            for (var i = 0; i < 500; i++)
                live.Add(new ChatDelta("x", null));
        })));
        await reader;
        Assert.Equal(4000, live.Read().Content.Length);
    }

    [Theory]
    [InlineData(AnswerPhase.Starting, EngineState.DownloadingEngine, "AnswerDownloadingEngine")]
    [InlineData(AnswerPhase.Starting, EngineState.Starting, "AnswerLoadingModel")]
    [InlineData(AnswerPhase.Starting, EngineState.Ready, "AnswerPreparing")]
    [InlineData(AnswerPhase.Starting, EngineState.Stopped, "AnswerPreparing")]
    [InlineData(AnswerPhase.Preparing, EngineState.Starting, "AnswerPreparing")]
    [InlineData(AnswerPhase.Waiting, EngineState.Ready, "AnswerWaiting")]
    [InlineData(AnswerPhase.Waiting, EngineState.Starting, "AnswerWaiting")]
    [InlineData(AnswerPhase.Reasoning, EngineState.Ready, "AnswerReasoning")]
    [InlineData(AnswerPhase.Writing, EngineState.Ready, "AnswerWaiting")]
    public void El_indicador_dice_por_donde_va(AnswerPhase phase, EngineState engine, string key)
    {
        foreach (var language in new[] { "es", "en" })
        {
            Loc.Use(language);
            try
            {
                Assert.Equal(Loc.Get(key), PendingAnswer.Status(phase, engine, TimeSpan.FromSeconds(1)));
            }
            finally
            {
                Loc.Use("es");
            }
        }
    }

    [Fact]
    public void Pasados_unos_segundos_dice_los_que_lleva()
    {
        Loc.Use("es");
        Assert.Equal("Lucia está pensando…", PendingAnswer.Status(AnswerPhase.Waiting, EngineState.Ready, TimeSpan.FromSeconds(2.9)));
        Assert.Equal("Lucia está pensando… · 3 s", PendingAnswer.Status(AnswerPhase.Waiting, EngineState.Ready, TimeSpan.FromSeconds(3)));
        Assert.Equal("Cargando la IA… · 75 s", PendingAnswer.Status(AnswerPhase.Starting, EngineState.Starting, TimeSpan.FromSeconds(75.8)));
        Loc.Use("en");
        try
        {
            Assert.Equal("Loading the AI… · 12 s", PendingAnswer.Status(AnswerPhase.Starting, EngineState.Starting, TimeSpan.FromSeconds(12)));
        }
        finally
        {
            Loc.Use("es");
        }
    }

    [Theory]
    [InlineData(0, 100)]       // pintar casi gratis: el minimo
    [InlineData(10, 100)]
    [InlineData(30, 150)]      // cinco veces lo que costo: pintar se lleva como mucho una quinta parte
    [InlineData(200, 1000)]
    [InlineData(400, 1500)]    // una respuesta enorme: se sigue viendo avanzar
    [InlineData(5000, 1500)]
    public void El_repintado_se_espacia_segun_lo_que_cuesta(int lastMs, int expectedMs)
    {
        Assert.Equal(TimeSpan.FromMilliseconds(expectedMs), PendingAnswer.NextRepaint(TimeSpan.FromMilliseconds(lastMs)));
    }

    /// <summary>Un contexto que nunca ejecuta lo que se le manda: lo que dependa del hilo que llama se queda colgado.</summary>
    private sealed class DeadContext : SynchronizationContext
    {
        public int Posts;
        public override void Post(SendOrPostCallback d, object? state) => Interlocked.Increment(ref Posts);
        public override void Send(SendOrPostCallback d, object? state) => Interlocked.Increment(ref Posts);
    }

    [Fact]
    public async Task El_bombeo_no_usa_el_hilo_que_lo_pide()
    {
        using var server = new LocalServer(ctx => LocalServer.Write(ctx, 200, "text/event-stream",
            "data: {\"choices\":[{\"delta\":{\"reasoning_content\":\"mmm\"}}]}\n\n" +
            "data: {\"choices\":[{\"delta\":{\"content\":\"Hola\"}}]}\n\n" +
            "data: {\"choices\":[{\"delta\":{\"content\":\" otra vez\"},\"finish_reason\":\"stop\"}]}\n\n" +
            "data: [DONE]\n\n"));
        var live = new PendingAnswer();
        var dead = new DeadContext();
        var previous = SynchronizationContext.Current;
        Task pump;
        SynchronizationContext.SetSynchronizationContext(dead);   // como el hilo de la interfaz ocupado
        try
        {
            pump = ChatClient.PumpAsync(server.BaseUrl, [new ChatMessage("user", "hola")], false, 100, null, live, CancellationToken.None);
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(previous);
        }
        await pump.WaitAsync(TimeSpan.FromSeconds(15));   // si necesitara el hilo que lo pidio, no acabaria nunca
        Assert.Equal(0, dead.Posts);
        var now = live.Read();
        Assert.Equal("Hola otra vez", now.Content);
        Assert.Equal("mmm", now.Reasoning);
        Assert.Equal("stop", now.Finish);
    }

    [Fact]
    public async Task El_bombeo_se_para_al_cancelar_y_deja_lo_que_llego()
    {
        var release = new TaskCompletionSource();
        using var server = new LocalServer(async ctx =>
        {
            ctx.Response.StatusCode = 200;
            ctx.Response.ContentType = "text/event-stream";
            ctx.Response.SendChunked = true;
            var first = System.Text.Encoding.UTF8.GetBytes("data: {\"choices\":[{\"delta\":{\"content\":\"Empiezo\"}}]}\n\n");
            await ctx.Response.OutputStream.WriteAsync(first);
            await ctx.Response.OutputStream.FlushAsync();
            await Task.WhenAny(release.Task, Task.Delay(20_000));
            try { ctx.Response.Close(); } catch (Exception) { }
        });
        using var cancel = new CancellationTokenSource();
        var live = new PendingAnswer();
        var pump = ChatClient.PumpAsync(server.BaseUrl, [new ChatMessage("user", "hola")], false, 100, null, live, cancel.Token);
        var waited = System.Diagnostics.Stopwatch.StartNew();
        while (live.Read().Content.Length == 0 && waited.ElapsedMilliseconds < 15_000)
            await Task.Delay(10);
        Assert.Equal("Empiezo", live.Read().Content);
        Assert.False(pump.IsCompleted);
        cancel.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pump);
        release.SetResult();
        Assert.Equal("Empiezo", live.Read().Content);
    }

    [Fact]
    public async Task Los_errores_del_servidor_salen_por_el_bombeo()
    {
        using var server = new LocalServer(ctx => LocalServer.Write(ctx, 500, "application/json", "{\"error\":{\"message\":\"sin memoria\"}}"));
        var live = new PendingAnswer();
        var error = await Assert.ThrowsAsync<HttpRequestException>(() => ChatClient.PumpAsync(server.BaseUrl, [new ChatMessage("user", "hola")], false, 100, null, live, CancellationToken.None));
        Assert.Equal("500: sin memoria", error.Message);
        Assert.Equal(string.Empty, live.Read().Content);
    }
}
