using System.IO.Compression;
using System.Text;
using System.Text.Json.Nodes;
using System.Windows.Threading;
using SocLucia.Engine;
using SocLucia.Services;

namespace SocLucia.Tests;

/// <summary>Un GGUF de mentira: solo la cabecera con los metadatos que se le pidan.</summary>
internal static class FakeGguf
{
    public static byte[] Build(params (string Key, object Value)[] entries)
    {
        using var ms = new MemoryStream();
        using var w = new BinaryWriter(ms, Encoding.UTF8);
        w.Write(0x46554747u);   // "GGUF"
        w.Write(3u);
        w.Write(0UL);
        w.Write((ulong)entries.Length);
        foreach (var (key, value) in entries)
        {
            Str(w, key);
            switch (value)
            {
                case string s: w.Write(8u); Str(w, s); break;
                case uint u: w.Write(4u); w.Write(u); break;
                case int i: w.Write(5u); w.Write(i); break;
                case ulong ul: w.Write(10u); w.Write(ul); break;
                case float f: w.Write(6u); w.Write(f); break;
                case bool b: w.Write(7u); w.Write(b); break;
                case uint[] list: w.Write(9u); w.Write(4u); w.Write((ulong)list.Length); foreach (var x in list) w.Write(x); break;
                case float[] floats: w.Write(9u); w.Write(6u); w.Write((ulong)floats.Length); foreach (var x in floats) w.Write(x); break;
                case string[] strings: w.Write(9u); w.Write(8u); w.Write((ulong)strings.Length); foreach (var x in strings) Str(w, x); break;
                default: throw new ArgumentException(value.GetType().Name);
            }
        }
        w.Write(new byte[64]);   // como si siguieran los tensores
        return ms.ToArray();
    }

    private static void Str(BinaryWriter w, string s)
    {
        var bytes = Encoding.UTF8.GetBytes(s);
        w.Write((ulong)bytes.Length);
        w.Write(bytes);
    }

    /// <summary>Un modelo tipo llama: 32 capas, 32 cabezas, 8 de KV, 4096 de ancho (cabeza de 128).</summary>
    public static byte[] Llama(uint context = 131072, params (string, object)[] extra) => Build(
        [("general.architecture", "llama"), ("general.name", "Prueba"), ("llama.context_length", context), ("llama.block_count", 32u),
         ("llama.embedding_length", 4096u), ("llama.attention.head_count", 32u), ("llama.attention.head_count_kv", 8u),
         ("llama.rope.freq_base", 500000f), ("tokenizer.ggml.add_bos_token", true),
         ("tokenizer.ggml.tokens", Enumerable.Range(0, 5000).Select(i => "tok" + i).ToArray()),
         ("tokenizer.ggml.scores", new float[5000]), .. extra]);
}

public class GgufInfoTests
{
    [Fact]
    public void Lee_el_contexto_y_lo_que_ocupa_cada_token_saltando_el_vocabulario()
    {
        var info = GgufInfo.Parse(new MemoryStream(FakeGguf.Llama()))!;
        Assert.Equal("llama", info.Architecture);
        Assert.Equal(131072, info.ContextLength);
        Assert.Equal(32, info.Layers);
        Assert.Equal(8, info.HeadsKv);
        Assert.Equal(128, info.KeyLength);   // 4096 / 32
        Assert.Equal(128, info.ValueLength);
        Assert.Equal(32L * 8 * (128 + 128) * 2, info.KvBytesPerToken);   // 128 KB por token en F16
    }

    [Fact]
    public void Hibridos_por_capa_y_longitudes_propias()
    {
        var hybrid = GgufInfo.Parse(new MemoryStream(FakeGguf.Llama(262144, ("llama.full_attention_interval", 4u))))!;
        Assert.Equal(8L * 8 * 256 * 2, hybrid.KvBytesPerToken);   // una de cada cuatro capas guarda K y V
        var perLayer = GgufInfo.Parse(new MemoryStream(FakeGguf.Build(
            ("general.architecture", "gemma3"), ("gemma3.block_count", 4u), ("gemma3.attention.head_count", 8u),
            ("gemma3.attention.head_count_kv", new uint[] { 2, 4, 2, 4 }), ("gemma3.attention.key_length", 256u), ("gemma3.attention.value_length", 128u))))!;
        Assert.Null(perLayer.ContextLength);
        Assert.Equal(4, perLayer.HeadsKv);
        Assert.Equal(4L * 4 * (256 + 128) * 2, perLayer.KvBytesPerToken);
        var vague = GgufInfo.Parse(new MemoryStream(FakeGguf.Build(("general.architecture", "x"), ("x.context_length", 4096u))))!;
        Assert.Equal(4096, vague.ContextLength);
        Assert.Null(vague.KvBytesPerToken);
    }

    [Fact]
    public void Lo_que_no_es_un_GGUF_da_null()
    {
        Assert.Null(GgufInfo.Parse(new MemoryStream([1, 2, 3])));
        Assert.Null(GgufInfo.Parse(new MemoryStream(Encoding.ASCII.GetBytes("GGUF"))));
        var good = FakeGguf.Llama();
        Assert.Null(GgufInfo.Parse(new MemoryStream(good[..40])));   // cortado
        Assert.Null(GgufInfo.Parse(new MemoryStream(FakeGguf.Build(("general.name", "sin arquitectura")))));
        var v1 = (byte[])good.Clone();
        v1[4] = 1;
        Assert.Null(GgufInfo.Parse(new MemoryStream(v1)));
        Assert.Null(GgufInfo.Read(null));
        Assert.Null(GgufInfo.Read(Path.Combine(Sandbox.Root, "no-esta.gguf")));
    }

    [Fact]
    public void Read_guarda_lo_leido_y_relee_si_cambia_el_fichero()
    {
        var path = Path.Combine(Sandbox.NewFolder("gguf"), "m.gguf");
        File.WriteAllBytes(path, FakeGguf.Llama(32768));
        Assert.Equal(32768, GgufInfo.Read(path)!.ContextLength);
        File.WriteAllBytes(path, FakeGguf.Llama(65536, ("llama.extra", 1u)));
        File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddMinutes(1));
        Assert.Equal(65536, GgufInfo.Read(path)!.ContextLength);
    }
}

public class TokenLimitsTests
{
    private const long GiB = 1024L * 1024 * 1024;
    private static readonly GgufInfo Dense = new("qwen3", 40960, 36, 8, 128, 128, null);   // 144 KB por token

    [Fact]
    public void Con_una_grafica_de_4_GB_el_tope_sale_de_la_memoria_y_de_la_IA()
    {
        var pc = new PcProfile("cpu", 16, 16 * GiB, "RTX 3050 Ti", 4 * GiB);
        var range = TokenLimits.RangeFor(Dense, (long)(2.5 * GiB), pc);
        Assert.Equal(4096, range.Fast);     // lo que cabe en la grafica tras el modelo
        Assert.Equal(32768, range.Max);     // con la memoria compartida, mas lento
        Assert.Equal(40960, range.Trained);
        // La IA manda si se entreno con menos.
        Assert.Equal(16384, TokenLimits.RangeFor(Dense with { ContextLength = 16384 }, (long)(2.5 * GiB), pc).Max);
        // Un modelo que no cabe en la grafica: el minimo rapido.
        Assert.Equal(TokenLimits.ContextMin, TokenLimits.RangeFor(Dense, 6 * GiB, pc).Fast);
        // Automatico: lo mas grande que cabe en la grafica (Fast), siempre dentro de [Min, Max].
        Assert.Equal(TokenLimits.ContextMin, range.Min);
        Assert.Equal(range.Fast, range.Auto);
        Assert.InRange(range.Auto, range.Min, range.Max);
        Assert.True(range.FromModel);
    }

    [Fact]
    public void El_automatico_siempre_cae_dentro_del_rango_de_la_IA()
    {
        var gpu = new PcProfile("cpu", 16, 16 * GiB, "RTX 3050 Ti", 4 * GiB);
        // Con grafica y cabecera: Auto = Fast.
        var r = TokenLimits.RangeFor(Dense, (long)(2.5 * GiB), gpu);
        Assert.Equal(r.Fast, r.Auto);
        // Sin poder leer lo que ocupa cada token (no KV): Auto = Max (no hay Fast).
        var noKv = TokenLimits.RangeFor(new GgufInfo("x", 40960, null, null, null, null, null), 0, gpu);
        Assert.Null(noKv.Fast);
        Assert.Equal(noKv.Max, noKv.Auto);
        Assert.Equal(40960, noKv.Max);
        // Sin cabecera: cae a los valores de siempre y lo dice (FromModel false).
        var none = TokenLimits.RangeFor(null, 0, gpu);
        Assert.False(none.FromModel);
        Assert.Equal(TokenLimits.ContextUnknownMax, none.Auto);
        Assert.InRange(none.Auto, none.Min, none.Max);
    }

    [Fact]
    public void Sin_grafica_cuenta_la_memoria_y_sin_cabecera_el_tope_de_siempre()
    {
        var cpu = new PcProfile("cpu", 8, 16 * GiB, null, 0);
        var range = TokenLimits.RangeFor(Dense with { ContextLength = 131072 }, (long)(2.5 * GiB), cpu);
        Assert.Equal(57344, range.Fast);
        Assert.Equal(82944, range.Max);
        Assert.Equal(new TokenLimits.ContextRange(TokenLimits.ContextMin, TokenLimits.ContextUnknownMax, null, null), TokenLimits.RangeFor(null, 0, cpu));
        Assert.Equal(TokenLimits.ContextCeiling, TokenLimits.RangeFor(Dense with { ContextLength = 1_000_000, KeyLength = 1, ValueLength = 1 }, 0, cpu).Max);
        Assert.Equal(TokenLimits.ContextMin, TokenLimits.RangeFor(Dense with { ContextLength = 512 }, 0, cpu).Max);
    }

    [Fact]
    public void Lo_ajustado_se_recorta_a_sus_limites()
    {
        var s = Sandbox.ResetSettings();
        try
        {
            s.AutoContext = false;
            s.ModelPath = Path.Combine(Sandbox.NewFolder("lim"), "dummy.gguf");
            File.WriteAllBytes(s.ModelPath, [1, 2, 3]);   // sin cabecera: tope de siempre
            s.ContextTokens = 100_000;
            Assert.Equal(TokenLimits.ContextUnknownMax, TokenLimits.Context(s));
            s.ContextTokens = 100;
            Assert.Equal(TokenLimits.ContextMin, TokenLimits.Context(s));
            // En automatico, el ajuste a mano se ignora y se usa el que elige Lucia.
            s.AutoContext = true;
            Assert.Equal(TokenLimits.RangeFor(s).Auto, TokenLimits.Context(s));
            s.AutoContext = false;
            File.WriteAllBytes(s.ModelPath, FakeGguf.Llama(4096));
            File.SetLastWriteTimeUtc(s.ModelPath, DateTime.UtcNow.AddMinutes(2));
            s.ContextTokens = 8192;
            Assert.Equal(4096, TokenLimits.Context(s));   // la IA solo admite 4096
            s.MaxAnswerTokens = 10;
            Assert.Equal(TokenLimits.AnswerMin, TokenLimits.Answer(s));
            s.MaxAnswerTokens = 1_000_000;
            Assert.Equal(TokenLimits.AnswerMax, TokenLimits.Answer(s));
        }
        finally
        {
            s.ContextTokens = TokenLimits.ContextDefault;
            s.MaxAnswerTokens = TokenLimits.AnswerDefault;
            Sandbox.ResetSettings();
        }
    }

    [Theory]
    [InlineData("8192", 8192)]
    [InlineData(" 8 192 ", 8192)]
    [InlineData("8.192", 8192)]
    [InlineData("8,192", 8192)]
    [InlineData("8\u00A0192", 8192)]
    [InlineData("2048", 2048)]
    [InlineData("100", null)]
    [InlineData("99999999", null)]
    [InlineData("1234567890", null)]
    [InlineData("-4096", null)]
    [InlineData("4k", null)]
    [InlineData("", null)]
    [InlineData(null, null)]
    public void Los_numeros_escritos_a_mano(string? text, int? expected)
        => Assert.Equal(expected, TokenLimits.Parse(text, 2048, 32768));

    private static JsonObject Door(string json, int answer = 2048, int context = 8192)
        => JsonNode.Parse(TokenLimits.ApplyToDoorRequest(Encoding.UTF8.GetBytes(json), answer, context))!.AsObject();

    [Fact]
    public void La_puerta_pone_el_tope_de_Ajustes_si_el_editor_no_trae_el_suyo()
    {
        Assert.Equal(2048, Door("""{"messages":[]}""")["max_tokens"]!.GetValue<int>());
        Assert.Equal(6144, Door("""{"chat_template_kwargs":{"enable_thinking":true}}""")["max_tokens"]!.GetValue<int>());   // razonando, el triple
        Assert.Equal(4096, Door("""{"chat_template_kwargs":{"enable_thinking":true}}""", context: 4096)["max_tokens"]!.GetValue<int>());
        var unlimited = Door("""{"max_tokens":-1}""");
        Assert.Equal(2048, unlimited["max_tokens"]!.GetValue<int>());   // «sin limite» de OpenAI: el de Ajustes
    }

    [Fact]
    public void La_puerta_respeta_el_del_editor_recortado_al_contexto()
    {
        var own = Door("""{"max_tokens":300}""");
        Assert.Equal(300, own["max_tokens"]!.GetValue<int>());
        Assert.Equal(8192, Door("""{"max_completion_tokens":100000}""")["max_completion_tokens"]!.GetValue<int>());
        var predict = Door("""{"n_predict":512}""");
        Assert.Equal(512, predict["n_predict"]!.GetValue<int>());
        Assert.False(predict.ContainsKey("max_tokens"));
        var same = Encoding.UTF8.GetBytes("""{"max_tokens":300}""");
        Assert.Same(same, TokenLimits.ApplyToDoorRequest(same, 2048, 8192));   // sin cambios: el mismo cuerpo
        var broken = Encoding.UTF8.GetBytes("no es json");
        Assert.Same(broken, TokenLimits.ApplyToDoorRequest(broken, 2048, 8192));
        var array = Encoding.UTF8.GetBytes("[1]");
        Assert.Same(array, TokenLimits.ApplyToDoorRequest(array, 2048, 8192));
    }
}

public class DiagLogTests : IDisposable
{
    private readonly long _max = DiagLog.MaxBytes;
    private readonly int _keep = DiagLog.Keep;

    public DiagLogTests()
    {
        if (Directory.Exists(Paths.Logs))
            foreach (var f in Directory.GetFiles(Paths.Logs))
                try { File.Delete(f); } catch (IOException) { }
    }

    public void Dispose()
    {
        DiagLog.MaxBytes = _max;
        DiagLog.Keep = _keep;
        DiagLog.SetPhase("inactiva");
    }

    private static string Text() => File.ReadAllText(DiagLog.Current);

    [Fact]
    public void Apunta_con_hora_zona_e_hilo_y_tapa_la_carpeta_del_usuario()
    {
        var profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        DiagLog.Write("motor", $"modelo en {profile}\\models\\x.gguf de {Environment.UserName}");
        var line = Text().TrimEnd();
        Assert.Matches(@"^\d{4}-\d\d-\d\d \d\d:\d\d:\d\d\.\d{3} \[motor\] \(hilo \d+\) modelo en %USERPROFILE%\\models\\x\.gguf de <usuario>$", line);
        Assert.DoesNotContain(profile, line, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("InvalidOperationException: en %USERPROFILE%", DiagLog.Describe(new InvalidOperationException("en " + profile)));
    }

    [Fact]
    public void Rota_por_tamano_y_guarda_los_anteriores_que_tocan()
    {
        DiagLog.MaxBytes = 400;
        DiagLog.Keep = 2;
        for (var i = 0; i < 60; i++)
            DiagLog.Write("prueba", $"linea {i:00} " + new string('x', 30));
        Assert.True(File.Exists(DiagLog.Current));
        Assert.True(File.Exists(Path.Combine(Paths.Logs, "diagnostics.1.log")));
        Assert.True(File.Exists(Path.Combine(Paths.Logs, "diagnostics.2.log")));
        Assert.False(File.Exists(Path.Combine(Paths.Logs, "diagnostics.3.log")));
        foreach (var f in Directory.GetFiles(Paths.Logs, "diagnostics*.log"))
            Assert.True(new FileInfo(f).Length <= 400, f);
        Assert.Contains("linea 59", Text());   // lo ultimo, en el actual
        Assert.Contains("linea", File.ReadAllText(Path.Combine(Paths.Logs, "diagnostics.1.log")));
        DiagLog.Keep = 0;
        for (var i = 0; i < 20; i++)
            DiagLog.Write("prueba", new string('y', 40));
        Assert.True(new FileInfo(DiagLog.Current).Length <= 400);
    }

    [Fact]
    public void La_fase_dice_que_hace_y_desde_cuando()
    {
        DiagLog.SetPhase("esperando el motor");
        Assert.Matches(@"^esperando el motor \(desde hace \d+[.,]\d s\)$", DiagLog.Phase);
    }

    [Fact]
    public void El_zip_lleva_los_registros_tapados_y_un_resumen_y_guarda_los_tres_ultimos()
    {
        var profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        DiagLog.Write("app", "hola");
        EngineHost.Log($"cargando {profile}\\modelo.gguf");
        File.WriteAllText(Path.Combine(Paths.Logs, "commands.log"), "orden secreta");
        var zip = DiagLog.Bundle("resumen de " + profile);
        using (var archive = ZipFile.OpenRead(zip))
        {
            var names = archive.Entries.Select(e => e.Name).ToList();
            Assert.Contains("diagnostics.log", names);
            Assert.Contains("engine.log", names);
            Assert.Contains("resumen.txt", names);
            Assert.DoesNotContain("commands.log", names);   // lo que se pidio hacer no sale
            foreach (var entry in archive.Entries)
            {
                using var reader = new StreamReader(entry.Open());
                Assert.DoesNotContain(profile, reader.ReadToEnd(), StringComparison.OrdinalIgnoreCase);
            }
        }
        for (var i = 0; i < 4; i++)
            File.WriteAllText(Path.Combine(Paths.Logs, $"sOCLucia-registros-20200101-00000{i}.zip"), "viejo");
        var second = DiagLog.Bundle("otra");
        Assert.NotEqual(zip, second);   // en el mismo segundo, otro nombre
        Assert.Equal(3, Directory.GetFiles(Paths.Logs, "sOCLucia-registros-*.zip").Length);
    }
}

public class UiWatchdogTests
{
    private static Dispatcher StartDispatcher()
    {
        using var ready = new ManualResetEventSlim();
        Dispatcher? dispatcher = null;
        var thread = new Thread(() =>
        {
            dispatcher = Dispatcher.CurrentDispatcher;
            ready.Set();
            Dispatcher.Run();
        }) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        ready.Wait();
        return dispatcher!;
    }

    private static void BloquearLaInterfaz() => Thread.Sleep(900);

    [Fact]
    public void Apunta_la_congelacion_con_la_fase_y_la_operacion_y_cuando_vuelve()
    {
        var dispatcher = StartDispatcher();
        if (File.Exists(DiagLog.Current)) File.Delete(DiagLog.Current);
        DiagLog.SetPhase("probando el vigilante");
        using var watchdog = new UiWatchdog(dispatcher, TimeSpan.FromMilliseconds(250), TimeSpan.FromMilliseconds(50));
        Thread.Sleep(300);
        Assert.Equal(0, watchdog.Freezes);
        dispatcher.BeginInvoke(new Action(BloquearLaInterfaz));
        var clock = System.Diagnostics.Stopwatch.StartNew();
        while (!(File.Exists(DiagLog.Current) && File.ReadAllText(DiagLog.Current).Contains("vuelve a responder")) && clock.ElapsedMilliseconds < 10_000)
            Thread.Sleep(50);
        var log = File.ReadAllText(DiagLog.Current);
        Assert.Contains("VENTANA CONGELADA", log);
        Assert.Contains("probando el vigilante", log);
        Assert.Contains("UiWatchdogTests.BloquearLaInterfaz", log);
        Assert.Matches(@"vuelve a responder tras \d+[.,]\d s congelada", log);
        Assert.Equal(1, watchdog.Freezes);
        dispatcher.InvokeShutdown();
        DiagLog.SetPhase("inactiva");
    }

    [Fact]
    public void Describe_las_operaciones_y_los_metodos_async()
    {
        Assert.StartsWith("ninguna", UiWatchdog.Describe(null));
        Assert.Equal("UiWatchdogTests.BloquearLaInterfaz", UiWatchdog.Name(BloquearLaInterfaz));
        Assert.Equal("?", UiWatchdog.Name(null));
        Action lambda = () => Thread.Sleep(0);
        Assert.Matches(@"^UiWatchdogTests\.Describe_las_operaciones_y_los_metodos_async \(lambda\)$", UiWatchdog.Name(lambda));
        var dispatcher = StartDispatcher();
        var op = dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(BloquearLaInterfaz));
        Assert.Contains("BloquearLaInterfaz", UiWatchdog.Describe(op));
        op.Wait();
        dispatcher.InvokeShutdown();
    }
}
