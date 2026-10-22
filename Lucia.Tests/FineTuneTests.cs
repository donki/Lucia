using System.Net;
using System.Windows;
using System.Windows.Controls;
using SocLucia.Chat;
using SocLucia.Engine;
using SocLucia.FineTune;
using SocLucia.Localization;
using SocLucia.Services;

namespace SocLucia.Tests;

public class TrainingDataTests
{
    [Fact]
    public void Jsonl_en_los_formatos_habituales()
    {
        var examples = TrainingData.ParseJson("""
            {"messages":[{"role":"system","content":"Eres Lucia."},{"role":"user","content":"Hola"},{"role":"assistant","content":"¡Hola!"},{"role":"user","content":"sin contestar"}]}
            {"conversations":[{"from":"human","value":"¿Qué tal?"},{"from":"gpt","value":"Bien."}]}
            {"prompt":"Capital de Francia","completion":"París"}
            {"instruction":"Traduce","input":"hola","output":"hello"}
            {"instruction":"Saluda","output":"Buenos días"}
            {"Pregunta":"¿Horario?","Respuesta":"De 9 a 18."}
            {"question":"q","answer":"a","system":"Breve."}
            esto no es json
            {"messages":[{"role":"user","content":[{"type":"text","text":"partes "},{"type":"text","text":"juntas"}]},{"role":"assistant","content":"vale"}]}
            {"messages":[{"role":"assistant","content":"sin pregunta"}]}
            {"otra":"cosa"}
            """, "f.jsonl");
        Assert.Equal(8, examples.Count);
        Assert.Equal(["system", "user", "assistant"], examples[0].Messages.Select(m => m.Role));   // lo de despues de la ultima respuesta sobra
        Assert.Equal(("¿Qué tal?", "Bien."), (examples[1].Question, examples[1].Answer));
        Assert.Equal(("Capital de Francia", "París"), (examples[2].Question, examples[2].Answer));
        Assert.Equal("Traduce\n\nhola", examples[3].Question);
        Assert.Equal(("Saluda", "Buenos días"), (examples[4].Question, examples[4].Answer));
        Assert.Equal(("¿Horario?", "De 9 a 18."), (examples[5].Question, examples[5].Answer));
        Assert.Equal(new TrainingMessage("system", "Breve."), examples[6].Messages[0]);
        Assert.Equal("partes juntas", examples[7].Question);
        Assert.All(examples, e => Assert.Equal("f.jsonl", e.Source));
    }

    [Fact]
    public void Json_con_una_lista_y_con_BOM()
    {
        var examples = TrainingData.ParseJson("\uFEFF [{\"prompt\":\"a\",\"completion\":\"b\"},{\"prompt\":\"c\",\"completion\":\"\"}]");
        Assert.Single(examples);
    }

    [Fact]
    public void Csv_con_cabecera_comillas_y_saltos_de_linea()
    {
        var examples = TrainingData.ParseCsv("\uFEFFpregunta;respuesta\r\n\"¿Uno; dos?\";\"Línea 1\nLínea 2 con \"\"comillas\"\"\"\r\n\r\nsolo una columna\r\nhola;\r\n¿Tres?;Tres.");
        Assert.Equal(2, examples.Count);
        Assert.Equal(("¿Uno; dos?", "Línea 1\nLínea 2 con \"comillas\""), (examples[0].Question, examples[0].Answer));
        Assert.Equal(("¿Tres?", "Tres."), (examples[1].Question, examples[1].Answer));
    }

    [Fact]
    public void Csv_sin_cabecera_con_comas_o_tabuladores()
    {
        Assert.Equal(2, TrainingData.ParseCsv("a,b\nc,d").Count);   // sin cabecera, la primera fila tambien cuenta
        var tsv = TrainingData.ParseCsv("question\tanswer\nx\ty", separator: '\t');
        Assert.Equal(("x", "y"), (tsv.Single().Question, tsv.Single().Answer));
    }

    [Fact]
    public void Desde_un_fichero_segun_la_extension()
    {
        var folder = Sandbox.NewFolder("ejemplos");
        File.WriteAllText(Path.Combine(folder, "e.csv"), "q;a\n1;2");
        File.WriteAllText(Path.Combine(folder, "e.tsv"), "1\t2");
        File.WriteAllText(Path.Combine(folder, "e.jsonl"), "{\"prompt\":\"1\",\"completion\":\"2\"}");
        foreach (var name in new[] { "e.csv", "e.tsv", "e.jsonl" })
            Assert.Equal(("1", "2"), (TrainingData.FromFile(Path.Combine(folder, name)).Single().Question, TrainingData.FromFile(Path.Combine(folder, name)).Single().Answer));
    }

    [Fact]
    public void De_las_conversaciones_solo_las_respuestas_de_texto_con_su_contexto()
    {
        var thread = new ChatThread
        {
            Title = "Charla",
            Messages =
            [
                new StoredMessage { Role = "user", Content = "Hola" },
                new StoredMessage { Role = "assistant", Content = "¡Hola!" },
                new StoredMessage { Role = "user", Content = "Lista la carpeta" },
                new StoredMessage { Role = "assistant", Content = "", ToolCalls = "[{}]" },
                new StoredMessage { Role = "tool", Content = "a.txt", ToolCallId = "1" },
                new StoredMessage { Role = "assistant", Content = "Hay a.txt" },   // sin pregunta delante tras la accion: no vale
                new StoredMessage { Role = "user", Content = "¿Y ahora?" },
                new StoredMessage { Role = "assistant", Content = "   " },
                new StoredMessage { Role = "assistant", Content = "Ahora nada." },
            ],
        };
        var examples = TrainingData.FromThreads([thread], system: "Sé breve.", historyTurns: 4);
        Assert.Equal(2, examples.Count);
        Assert.Equal(["system", "user", "assistant"], examples[0].Messages.Select(m => m.Role));
        Assert.Equal("Charla", examples[0].Source);
        Assert.Equal(("¿Y ahora?", "Ahora nada."), (examples[1].Question, examples[1].Answer));
        Assert.DoesNotContain(examples[1].Messages, m => m.Content.Contains("a.txt"));

        // La historia se recorta a los ultimos mensajes, y siempre empieza por una pregunta.
        var largo = new ChatThread { Messages = [.. Enumerable.Range(0, 6).SelectMany(i => new[] { new StoredMessage { Role = "user", Content = $"p{i}" }, new StoredMessage { Role = "assistant", Content = $"r{i}" } })] };
        var last = TrainingData.FromThreads([largo], historyTurns: 3).Last();
        Assert.Equal(["user", "assistant", "user", "assistant"], last.Messages.Select(m => m.Role));
        Assert.Equal("p4", last.Messages[0].Content);
    }

    [Fact]
    public void La_revision_quita_vacios_repetidos_y_largos_y_dice_si_bastan()
    {
        var list = new List<TrainingExample>
        {
            TrainingExample.Pair("Hola", "Adiós"),
            TrainingExample.Pair("  hola ", "adiós"),   // repetido aunque cambien espacios y mayusculas
            TrainingExample.Pair("", "x"),
            TrainingExample.Pair("largo", new string('a', TrainingData.MaxChars + 1)),
        };
        var (kept, report) = TrainingData.Review(list);
        Assert.Single(kept);
        Assert.Equal(new TrainingReport(1, 1, 1, 1, (4 + 5) / 4, TrainingVerdict.TooFew), report);
        Assert.Equal(TrainingVerdict.Few, TrainingData.Review(Enumerable.Range(0, TrainingData.Minimum).Select(i => TrainingExample.Pair($"p{i}", "r"))).Report.Verdict);
        Assert.Equal(TrainingVerdict.Good, TrainingData.Review(Enumerable.Range(0, TrainingData.Recommended).Select(i => TrainingExample.Pair($"p{i}", "r"))).Report.Verdict);
    }

    [Fact]
    public void Se_guarda_en_JSONL_de_mensajes_y_se_vuelve_a_leer_igual()
    {
        var path = Path.Combine(Sandbox.NewFolder("guardar"), "sub", "d.jsonl");
        var examples = new[] { TrainingExample.Pair("¿«Comillas» \"y\" acentos?", "Sí\nmulti", system: "S") };
        TrainingData.Save(examples, path);
        Assert.StartsWith("{\"messages\":[{\"role\":\"system\"", File.ReadAllText(path));
        var back = TrainingData.ParseJson(File.ReadAllText(path)).Single();
        Assert.Equal(examples[0].Messages, back.Messages);
    }
}

public class BaseModelsTests
{
    [Theory]
    [InlineData("""{"cardData":{"base_model":"Qwen/Qwen3.5-4B"},"tags":["gguf"]}""", "unsloth/Qwen3.5-4B-GGUF", "Qwen/Qwen3.5-4B")]
    [InlineData("""{"cardData":{"base_model":["", "google/gemma-3-1b-it"]},"tags":["gguf"]}""", "unsloth/gemma-3-1b-it-GGUF", "google/gemma-3-1b-it")]
    [InlineData("""{"cardData":{"base_model":"sin-barra"},"tags":["gguf"]}""", "org/X-GGUF", null)]
    [InlineData("""{"tags":["safetensors"]}""", "org/Modelo", "org/Modelo")]
    [InlineData("""{"tags":["gguf"]}""", "org/Modelo", null)]
    [InlineData("""{"cardData":null}""", "org/Algo-GGUF", null)]
    public void El_modelo_original_sale_de_la_ficha(string json, string repo, string? expected)
        => Assert.Equal(expected, BaseModels.BaseRepoFrom(json, repo));

    [Fact]
    public void La_ficha_del_original_dice_parametros_licencia_cierre_y_ficheros()
    {
        var model = BaseModels.ParseBase("Qwen/Qwen3.5-0.8B",
            """{"safetensors":{"total":800000000},"gated":false,"cardData":{"license":"apache-2.0"}}""",
            """
            [{"type":"file","path":"config.json","size":3000},
             {"type":"file","path":"model.safetensors","size":134,"lfs":{"size":1600000000}},
             {"type":"file","path":"tokenizer.json","size":12000},
             {"type":"file","path":"chat_template.jinja","size":7000},
             {"type":"file","path":"merges.txt","size":10},
             {"type":"file","path":"README.md","size":9},
             {"type":"file","path":"pytorch_model.bin","size":9},
             {"type":"directory","path":"onnx"},
             {"type":"file","path":"onnx/model.onnx","size":9}]
            """);
        Assert.Equal(800_000_000, model.Parameters);
        Assert.Equal(0.8, model.Billions, 3);
        Assert.False(model.Gated);
        Assert.Equal("apache-2.0", model.License);
        Assert.Equal(["config.json", "model.safetensors", "tokenizer.json", "chat_template.jinja", "merges.txt"], model.Files);
        Assert.Equal(3000 + 1_600_000_000L + 12000 + 7000 + 10, model.Bytes);

        var gated = BaseModels.ParseBase("g/m", """{"gated":"manual"}""", """[{"type":"file","path":"model.safetensors","lfs":{"size":2000}}]""");
        Assert.True(gated.Gated);
        Assert.Equal("?", gated.License);
        Assert.Equal(1000, gated.Parameters);   // sin el dato: 2 bytes por parametro
    }

    [Theory]
    [InlineData(800_000_000L, 4096, true, TrainFit.Ok)]
    [InlineData(4_660_000_000L, 4096, true, TrainFit.Tight)]
    [InlineData(9_000_000_000L, 4096, true, TrainFit.TooBig)]
    [InlineData(1_000_000_000L, 4096, false, TrainFit.CpuOnly)]
    [InlineData(4_000_000_000L, 0, true, TrainFit.TooBig)]
    public void Si_cabe_para_entrenar(long parameters, int vramMb, bool cuda, TrainFit expected)
    {
        var pc = new PcProfile("cpu", 8, 16L << 30, vramMb > 0 ? "RTX" : null, (long)vramMb << 20);
        var model = new BaseModel("o/m", parameters, false, "mit", parameters * 2, []);
        Assert.Equal(expected, BaseModels.FitOf(model, pc, cuda));
    }

    [Fact]
    public void Carpeta_local_y_ficheros_que_hacen_falta()
    {
        Assert.Equal(Path.Combine(BaseModels.Folder, "Qwen--Qwen3.5-4B"), BaseModels.LocalFolder("Qwen/Qwen3.5-4B"));
        Assert.True(BaseModels.Needed("tokenizer.model"));
        Assert.True(BaseModels.Needed("modeling_phi3.py"));
        Assert.False(BaseModels.Needed("sub/model.safetensors"));
        Assert.False(BaseModels.Needed("model.gguf"));
        Assert.Equal([8, 16, 32], BaseModels.Presets.Select(p => p.Rank));
        Assert.Equal(16, BaseModels.Quick.Alpha);
    }

    [Fact]
    public async Task Se_resuelve_y_se_baja_el_original_con_progreso()
    {
        var http = Downloader.Http;
        var net = new FakeHttp
        {
            Other = r => r.RequestUri!.ToString() switch
            {
                "https://huggingface.co/api/models/unsloth/M-GGUF" => Json("""{"cardData":{"base_model":"org/M"},"tags":["gguf"]}"""),
                "https://huggingface.co/api/models/org/M" => Json("""{"safetensors":{"total":10},"cardData":{"license":"mit"}}"""),
                "https://huggingface.co/api/models/org/M/tree/main" => Json("""[{"type":"file","path":"config.json","size":2},{"type":"file","path":"model.safetensors","size":4}]"""),
                "https://huggingface.co/org/M/resolve/main/config.json?download=true" => Bytes(2),
                "https://huggingface.co/org/M/resolve/main/model.safetensors?download=true" => Bytes(4),
                "https://huggingface.co/api/models/unsloth/Sin-GGUF" => Json("""{"tags":["gguf"]}"""),
                _ => null,
            },
        };
        Downloader.Http = new HttpClient(net);
        try
        {
            Assert.Null(await BaseModels.ResolveAsync("unsloth/Sin-GGUF", CancellationToken.None));
            Assert.Null(await BaseModels.ResolveAsync("no/existe", CancellationToken.None));
            var model = await BaseModels.ResolveAsync("unsloth/M-GGUF", CancellationToken.None);
            Assert.NotNull(model);
            Assert.Equal(("org/M", 10L, 6L), (model!.Repo, model.Parameters, model.Bytes));
            BaseModels.Delete("org/M");
            Assert.False(BaseModels.IsDownloaded("org/M"));
            var seen = new List<long>();
            var folder = await BaseModels.DownloadAsync(model, new SyncProgress<Downloader.Progress>(p => seen.Add(p.Received)), CancellationToken.None);
            Assert.True(BaseModels.IsDownloaded("org/M"));
            Assert.Equal(4, new FileInfo(Path.Combine(folder, "model.safetensors")).Length);
            Assert.Equal(6, seen.Max());
            Assert.Equal(folder, await BaseModels.DownloadAsync(model, null, CancellationToken.None));   // ya esta: no baja nada
            BaseModels.Delete("org/M");
        }
        finally
        {
            Downloader.Http = http;
        }
    }

    internal static HttpResponseMessage Json(string json) => new(HttpStatusCode.OK) { Content = new StringContent(json) };
    internal static HttpResponseMessage Bytes(int n) => new(HttpStatusCode.OK) { Content = new ByteArrayContent(new byte[n]) };
}

public class FineTuneRunnerTests
{
    [Fact]
    public void Lee_las_lineas_del_script_y_calcula_lo_que_falta()
    {
        Assert.Null(FineTuneRunner.Parse("registro cualquiera"));
        Assert.Null(FineTuneRunner.Parse("@lucia no-json"));
        Assert.Null(FineTuneRunner.Parse("@lucia [1]"));
        var p = FineTuneRunner.Parse("""@lucia {"event":"progress","step":4,"total":10,"loss":1.25,"epoch":0.5,"elapsed":20}""")!;
        Assert.Equal(new TrainEvent("progress", null, 4, 10, 1.25, 0.5, 20), p);
        Assert.Equal(30, FineTuneRunner.Remaining(p));
        Assert.Null(FineTuneRunner.Remaining(p with { Step = 1 }));
        Assert.Null(FineTuneRunner.Remaining(p with { Step = 10 }));
        Assert.Equal("C:\\a.gguf", FineTuneRunner.Parse("""@lucia {"event":"done","adapter":"C:\\a.gguf","examples":3}""")!.Path);
        Assert.Equal("oom", FineTuneRunner.Parse("""@lucia {"event":"error","kind":"oom","message":"m"}""")!.ErrorKind);
        Assert.Null(FineTuneRunner.Parse("""@lucia {"event":"stage","stage":"loading"}""")!.Loss);
    }

    [Theory]
    [InlineData(12, "FtPresetQuick", 4, 1, 48)]       // pocos ejemplos: las pasadas que hagan falta para llegar a los pasos minimos
    [InlineData(12, "FtPresetBalanced", 9, 1, 108)]
    [InlineData(12, "FtPresetDeep", 17, 1, 204)]
    [InlineData(3, "FtPresetBalanced", 15, 1, 45)]    // tope de pasadas
    [InlineData(200, "FtPresetQuick", 2, 4, 100)]     // muchos: de cuatro en cuatro y las pasadas minimas
    [InlineData(200, "FtPresetDeep", 5, 4, 250)]
    public void El_plan_de_pasadas_y_pasos(int examples, string key, int epochs, int accumulation, int steps)
        => Assert.Equal((epochs, accumulation, steps), BaseModels.Presets.Single(p => p.Key == key).Plan(examples));

    [Fact]
    public void La_estimacion_de_tiempo()
    {
        var (epochs, steps, seconds) = FineTuneWindow.Estimate(96, BaseModels.Balanced, 0.8, gpu: true);
        Assert.Equal((3, 288), (epochs, steps));   // con 96 ejemplos ya bastan las pasadas minimas
        Assert.InRange(seconds, 150, 400);   // medido: 96 ejemplos-paso en 69 s con 0,8B
        Assert.True(FineTuneWindow.Estimate(96, BaseModels.Balanced, 0.8, gpu: false).Seconds > seconds * 5);
        Assert.True(FineTuneWindow.Estimate(96, BaseModels.Balanced, 4, gpu: true).Seconds > seconds);
    }

    [Fact]
    public void Solo_se_sacan_los_convertidores_del_codigo_de_llama_cpp()
    {
        Assert.True(PythonEnv.IsConverterFile("llama.cpp-b10621/convert_lora_to_gguf.py"));
        Assert.True(PythonEnv.IsConverterFile("llama.cpp-b10621/gguf-py/gguf/constants.py"));
        Assert.True(PythonEnv.IsConverterFile("llama.cpp-b10621/conversion/qwen.py"));
        Assert.True(PythonEnv.IsConverterFile("llama.cpp-b10621/LICENSE"));
        Assert.False(PythonEnv.IsConverterFile("llama.cpp-b10621/src/llama.cpp"));
        Assert.False(PythonEnv.IsConverterFile("convert_lora_to_gguf.py"));
    }

    [Fact]
    public void El_script_va_dentro_de_la_aplicacion_y_el_entorno_se_reconoce_por_su_version()
    {
        PythonEnv.WriteScript();
        Assert.Contains("def train(cfg)", File.ReadAllText(PythonEnv.ScriptPath));
        Assert.False(PythonEnv.Installed);
        FakeEnv.Install();
        Assert.True(PythonEnv.Installed);
        File.WriteAllText(Path.Combine(PythonEnv.Folder, ".completa"), "0|viejo");
        Assert.False(PythonEnv.Installed);   // otra fijacion: se rehace
        PythonEnv.Remove();
        Assert.False(PythonEnv.Installed);
        Assert.False(File.Exists(PythonEnv.PythonExe));
    }
}

/// <summary>Un entorno de Python «instalado» cuyo python.exe es el llama-server de mentira en modo FAKE_PYTHON.</summary>
internal static class FakeEnv
{
    public static void Install()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(PythonEnv.PythonExe)!);
        foreach (var file in Directory.GetFiles(AppContext.BaseDirectory, "FakeLlama.*"))
            File.Copy(file, Path.Combine(Path.GetDirectoryName(PythonEnv.PythonExe)!, Path.GetFileName(file)), overwrite: true);
        File.Copy(Path.Combine(AppContext.BaseDirectory, "FakeLlama.exe"), PythonEnv.PythonExe, overwrite: true);
        Directory.CreateDirectory(PythonEnv.LlamaCppFolder);
        File.WriteAllText(Path.Combine(PythonEnv.Folder, ".completa"), FineTunePin.EnvVersion + "|cuda|pruebas");
    }

    /// <summary>La IA original ya bajada (para que el asistente no descargue nada).</summary>
    public static BaseModel Base(string repo = "org/Original")
    {
        var folder = BaseModels.LocalFolder(repo);
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, ".completa"), "pruebas");
        return new BaseModel(repo, 800_000_000, false, "apache-2.0", 1_600_000_000, ["model.safetensors"]);
    }
}

public class AdaptersTests
{
    [Fact]
    public void Uno_en_uso_por_IA_y_solo_si_el_fichero_existe()
    {
        var s = Sandbox.ResetSettings();
        var folder = Sandbox.NewFolder("adaptadores");
        LoraAdapter Make(string name, string model)
        {
            var path = Path.Combine(folder, name + ".gguf");
            File.WriteAllBytes(path, [1]);
            return new LoraAdapter { Name = name, Path = path, ModelPath = model };
        }
        var a = Make("a", "m1.gguf");
        var b = Make("b", "m1.gguf");
        var c = Make("c", "m2.gguf");
        Adapters.Add(s, a, activate: true);
        Adapters.Add(s, b, activate: true);
        Adapters.Add(s, c, activate: false);
        Assert.Same(b, Adapters.ActiveFor(s, "M1.GGUF"));
        Assert.False(a.Active);
        Assert.Null(Adapters.ActiveFor(s, "m2.gguf"));
        Assert.Null(Adapters.ActiveFor(s, null));
        Adapters.SetActive(s, null, "m1.gguf");
        Assert.Null(Adapters.ActiveFor(s, "m1.gguf"));
        Adapters.SetActive(s, a, "m1.gguf");
        File.Delete(a.Path);
        Assert.Null(Adapters.ActiveFor(s, "m1.gguf"));   // el fichero ya no esta: no se carga
        Adapters.Delete(s, b);
        Assert.False(File.Exists(b.Path));
        Assert.Equal(2, s.Adapters.Count);
        Adapters.Add(s, new LoraAdapter { Name = "otra vez", Path = c.Path, ModelPath = "m2.gguf" }, activate: false);
        Assert.Equal(2, s.Adapters.Count);   // misma ruta: se reemplaza
        Sandbox.ResetSettings();
    }

    [Theory]
    [InlineData("Mi estilo: «formal»", "Mi-estilo-«formal»")]
    [InlineData("  ..  ", "lucia")]
    [InlineData("a/b\\c", "a-b-c")]
    public void Nombres_de_fichero_seguros(string name, string safe) => Assert.Equal(safe, Adapters.SafeName(name));

    [Fact]
    public void Nombre_de_fichero_con_fecha() => Assert.Matches(@"^Prueba-\d{8}-\d{6}\.lora\.gguf$", Adapters.FileNameFor("Prueba"));
}

public class SearchFilterTests
{
    [Fact]
    public void La_direccion_lleva_tipo_tamaño_formato_orden_y_campos()
    {
        var url = HuggingFace.SearchUrl("qwen imágenes", new HuggingFace.Filters("image-text-to-text", 4, HuggingFace.Formats.Convertible, true, "likes"));
        Assert.StartsWith("https://huggingface.co/api/models?sort=likes&direction=-1&limit=60&search=qwen&pipeline_tag=image-text-to-text&num_parameters=min:0,max:4B", url);
        Assert.Contains("&expand[]=safetensors&expand[]=gguf", url);
        Assert.DoesNotContain("filter=gguf", url);
        var gguf = HuggingFace.SearchUrl("", new HuggingFace.Filters(MaxBillions: 0.5, Sort: "inventado"));
        Assert.StartsWith("https://huggingface.co/api/models?sort=downloads&direction=-1&limit=30&filter=gguf&num_parameters=min:0,max:500M", gguf);
        // La palabra clave pone el tipo si el filtro no dice otro.
        Assert.Contains("pipeline_tag=text-to-speech", HuggingFace.SearchUrl("voz", new HuggingFace.Filters()));
    }

    [Theory]
    [InlineData(3, "3B")]
    [InlineData(1.5, "1.5B")]
    [InlineData(0.5, "500M")]
    public void Parametros_en_el_formato_de_la_API(double value, string text) => Assert.Equal(text, HuggingFace.Billions(value));

    [Fact]
    public void Candidatos_con_parametros_y_safetensors()
    {
        var list = HuggingFace.Candidates("""
            [{"id":"a/Texto","tags":["safetensors","license:mit"],"pipeline_tag":"text-generation","safetensors":{"total":3000000000}},
             {"id":"a/Gguf","tags":["gguf","license:apache-2.0"],"pipeline_tag":"text-generation","gguf":{"total":4000000000}},
             {"id":"a/Imagen","tags":["safetensors"],"pipeline_tag":"text-to-image","safetensors":{"total":9}},
             {"id":"a/Cerrado","tags":["safetensors","license:llama3"],"pipeline_tag":"text-generation","safetensors":{"total":9},"gated":"auto"},
             {"id":"a/SinTipo","tags":["safetensors"],"safetensors":{"total":9}},
             {"id":"lmstudio-community/Qwen-9B-MLX-4bit","tags":["safetensors","mlx"],"pipeline_tag":"text-generation","safetensors":{"total":9}},
             {"id":"nvidia/Modelo-NVFP4","tags":["safetensors"],"pipeline_tag":"text-generation","safetensors":{"total":9}}]
            """);
        Assert.Equal((3_000_000_000L, true), (list[0].Parameters, list[0].Safetensors));
        Assert.Equal((4_000_000_000L, false), (list[1].Parameters, list[1].Safetensors));
        Assert.True(HuggingFace.IsConvertible(list[0]));
        Assert.False(HuggingFace.IsConvertible(list[1]));   // ya es GGUF
        Assert.False(HuggingFace.IsConvertible(list[2]));   // imagen
        Assert.False(HuggingFace.IsConvertible(list[3]));   // cerrado
        Assert.False(HuggingFace.IsConvertible(list[4]));   // sin tipo (podria ser cualquier cosa)
        Assert.True(list[5].Prequantized);                  // MLX: ya cuantizado, no se convierte
        Assert.False(HuggingFace.IsConvertible(list[5]));
        Assert.False(HuggingFace.IsConvertible(list[6]));   // NVFP4 en el nombre
        Assert.False(list[0].Prequantized);
        list = list.Take(5).ToList();
        Assert.Equal(5, HuggingFace.Filter(list, new HuggingFace.Filters()).Count);   // solo GGUF: ya lo filtra la API
        Assert.Equal(["a/Texto", "a/Gguf"], HuggingFace.Filter(list, new HuggingFace.Filters(Format: HuggingFace.Formats.Convertible)).Select(c => c.Id));
        Assert.Equal(["a/Texto", "a/Gguf", "a/Cerrado"], HuggingFace.Filter(list, new HuggingFace.Filters(Format: HuggingFace.Formats.All, CommercialOnly: true)).Select(c => c.Id));
    }

    [Fact]
    public void Los_convertibles_van_despues_de_los_que_se_ejecutan()
    {
        var pc = new PcProfile("cpu", 8, 16L << 30, null, 0);
        HuggingFace.Hit Hit(string repo, bool runnable, string kind, long downloads)
            => new(new CatalogModel(repo, repo, 1L << 30, "mit", "CatalogHf"), "f", downloads, 0, false, runnable, kind);
        var enorme = new HuggingFace.Hit(new CatalogModel("enorme", "enorme", 200L << 30, "mit", "CatalogHf"), "", 1_000_000, 0, false, false, "convertible");
        var ranked = HuggingFace.Rank([Hit("otro", false, "text-to-image", 99), enorme, Hit("conv", false, "convertible", 1), Hit("gguf", true, "gguf", 0)], pc);
        Assert.Equal(["gguf", "conv", "enorme", "otro"], ranked.Select(h => h.Model.Repo));   // de los convertibles, primero los que caben
        Assert.True(ranked[1].Convertible);
    }
}

/// <summary>El asistente Enseñar a Lucia y la tarjeta de Ajustes, con la red, el Python y el motor de mentira.</summary>
public class FineTuneWindowTests : UiTest
{
    private static void Teach(Action<FineTuneWindow> act)
    {
        var w = new FineTuneWindow();
        Ui.Expect<FineTuneWindow>(t =>
        {
            act(t);
            if (t.IsVisible) t.Close();
        });
        w.ShowDialog();
    }

    private void ServeResolve()
    {
        Net.Other = r => r.RequestUri!.ToString() switch
        {
            "https://huggingface.co/api/models/org/Prueba-GGUF" => BaseModelsTests.Json("""{"cardData":{"base_model":"org/Original"},"tags":["gguf"]}"""),
            "https://huggingface.co/api/models/org/Original" => BaseModelsTests.Json("""{"safetensors":{"total":800000000},"cardData":{"license":"apache-2.0"}}"""),
            "https://huggingface.co/api/models/org/Original/tree/main" => BaseModelsTests.Json("""[{"type":"file","path":"model.safetensors","lfs":{"size":1600000000}}]"""),
            _ => null,
        };
    }

    private void InstallPrueba()
    {
        AppSettings.Current.Remember(new InstalledModel { Name = "Prueba", Path = ModelFile, Repo = "org/Prueba-GGUF", License = "mit" });
    }

    [Fact]
    public void Asistente_DeLaIAAlosEjemplos_SoloSigueCuandoSePuede()
    {
        InstallPrueba();
        ServeResolve();
        var csv = Path.Combine(Sandbox.NewFolder("ej"), "e.csv");
        File.WriteAllText(csv, "pregunta;respuesta\n" + string.Join("\n", Enumerable.Range(1, 12).Select(i => $"P{i};R{i}")));
        Ui.Run(() => Teach(t =>
        {
            Assert.Equal(1, t.Step);
            Ui.WaitFor(() => t.NextEnabled);
            Assert.Contains("org/Original", Ui.Find<TextBlock>(t, "BaseText").Text);
            Assert.Contains("0,8", Ui.Find<TextBlock>(t, "BaseText").Text);
            Assert.NotEmpty(Ui.Find<TextBlock>(t, "EnvText").Text);
            Ui.Click(Ui.Find<Button>(t, "NextButton"));
            Assert.Equal(2, t.Step);
            Assert.False(t.NextEnabled);   // sin ejemplos no se sigue
            Assert.Equal(Loc.Format("FtReview", 0), t.ReviewTextForTest);

            // Escritos a mano
            Ui.Click(Ui.Find<Button>(t, "AddOneButton"));
            Ui.Find<TextBox>(t, "QuestionBox").Text = "¿Hola?";
            Ui.Find<TextBox>(t, "AnswerBox").Text = "Hola.";
            Ui.Click(Ui.Find<Button>(t, "OneSaveButton"));
            Assert.Equal(string.Empty, Ui.Find<TextBox>(t, "QuestionBox").Text);
            Ui.Click(Ui.Find<Button>(t, "OneSaveButton"));   // vacio: nada
            Assert.Single(t.Examples);

            // Desde un fichero (uno que no se lee y otro sin ejemplos avisan)
            var vacio = Path.Combine(Sandbox.NewFolder("ej2"), "v.csv");
            File.WriteAllText(vacio, "nada");
            Fake.Files.Enqueue([csv]);
            Ui.Click(Ui.Find<Button>(t, "FromFileButton"));
            Assert.Equal(13, t.Examples.Count);
            Assert.True(t.NextEnabled);
            Assert.Equal(Loc.Format("FtReview", 13), t.ReviewTextForTest);
            string? aviso = null;
            Ui.Answer("Ok", m => aviso = m);
            Fake.Files.Enqueue([vacio]);
            Ui.Click(Ui.Find<Button>(t, "FromFileButton"));
            Assert.Equal(Loc.Get("FtFileEmpty"), aviso);
            Fake.Files.Enqueue(null);   // cancelado
            Ui.Click(Ui.Find<Button>(t, "FromFileButton"));

            // Repetidos fuera, quitar uno, y la lista se ve
            Fake.Files.Enqueue([csv]);
            Ui.Click(Ui.Find<Button>(t, "FromFileButton"));
            Assert.Equal(13, t.Examples.Count);
            Assert.Contains(Loc.Format("FtDuplicates", 12), Ui.Find<TextBlock>(t, "ReviewHint").Text);
            var quitar = Ui.All<Button>(Ui.Find<StackPanel>(t, "ExampleList")).First(b => Equals(b.ToolTip, Loc.Get("FtRemove")));
            Ui.Click(quitar);
            Assert.Equal(12, t.Examples.Count);

            // Plantilla CSV
            var plantilla = Path.Combine(Sandbox.NewFolder("pl"), "plantilla.csv");
            Fake.Saves.Enqueue(plantilla);
            Ui.Click(Ui.Find<Button>(t, "TemplateButton"));
            Assert.Equal(2, TrainingData.FromFile(plantilla).Count);
            Assert.Contains(plantilla, Fake.Opened);

            // Paso 3: el nombre se propone y la estimacion sale
            Ui.Click(Ui.Find<Button>(t, "NextButton"));
            Assert.Equal(3, t.Step);
            Assert.StartsWith("Prueba · ", Ui.Find<TextBox>(t, "NameBox").Text);
            Assert.StartsWith(Loc.Format("FtEstimateTime", 12, 9, 108, "")[..30], t.EstimateForTest);
            Ui.Click(Ui.Find<System.Windows.Controls.Primitives.ToggleButton>(t, "DeepButton"));
            Assert.Equal(Loc.Get("FtPresetDeepHint"), Ui.Find<TextBlock>(t, "PresetText").Text);
            Ui.Find<TextBox>(t, "NameBox").Text = "";
            Assert.False(t.NextEnabled || Ui.Find<Button>(t, "NextButton").IsEnabled && false);
            Ui.Click(Ui.Find<Button>(t, "BackButton"));
            Assert.Equal(2, t.Step);
            Loc.Use("en");   // repinta en el otro idioma sin perder nada
            Assert.Equal(Loc.Format("FtReview", 12), t.ReviewTextForTest);
            Loc.Use("es");
            Ui.Answer("Yes");
            Ui.Click(Ui.Find<Button>(t, "ClearButton"));
            Assert.Empty(t.Examples);
        }));
    }

    [Fact]
    public void Asistente_ConLasConversaciones_ComoEjemplos()
    {
        InstallPrueba();
        ServeResolve();
        ThreadStore.Save(new ChatThread { Title = "Buena", Messages = [new StoredMessage { Role = "user", Content = "¿a?" }, new StoredMessage { Role = "assistant", Content = "b" }] });
        ThreadStore.Save(new ChatThread { Title = "Vacía", Messages = [new StoredMessage { Role = "user", Content = "¿a?" }] });
        Ui.Run(() => Teach(t =>
        {
            t.GoToStepForTest(2);
            Ui.Click(Ui.Find<Button>(t, "FromChatsButton"));
            var checks = Ui.All<CheckBox>(Ui.Find<StackPanel>(t, "ChatsList")).ToList();
            Assert.Single(checks);   // la que no tiene respuestas no sale
            Assert.Equal(Loc.Format("FtChatRow", "Buena", 1), checks[0].Content);
            checks[0].IsChecked = true;
            Ui.Click(Ui.Find<Button>(t, "ChatsAddButton"));
            Assert.Single(t.Examples);
            Assert.Equal(Visibility.Collapsed, Ui.Find<Border>(t, "ChatsBox").Visibility);
        }));
    }

    [Fact]
    public void Asistente_SinOriginal_OGrande_NoDejaSeguir()
    {
        AppSettings.Current.Remember(new InstalledModel { Name = "Prueba", Path = ModelFile });   // importado a mano: sin repositorio
        Ui.Run(() => Teach(t =>
        {
            Ui.WaitFor(() => Ui.Find<TextBlock>(t, "BaseText").Text == Loc.Get("FtNoRepo"));
            Assert.False(t.NextEnabled);
            var big = new BaseModel("org/Enorme", 70_000_000_000, false, "mit", 1, []);
            t.SetBaseForTest(new InstalledModel { Name = "X", Path = ModelFile }, big, new PcProfile("cpu", 8, 16L << 30, "RTX", 4L << 30));
            Assert.False(t.NextEnabled);
            Assert.StartsWith(Loc.Format("FtFitTooBig", "", "")[..10], t.FitTextForTest);
            t.SetBaseForTest(new InstalledModel { Name = "X", Path = ModelFile }, big with { Parameters = 1_000_000_000, Gated = true }, new PcProfile("cpu", 8, 16L << 30, "RTX", 4L << 30));
            Assert.Equal(Loc.Get("FtGated"), t.FitTextForTest);
            Assert.False(t.NextEnabled);
        }));
    }

    [Fact]
    public void Asistente_Entrena_GuardaElAdaptador_YElMotorLoCarga()
    {
        InstallPrueba();
        ServeResolve();
        FakeEnv.Install();
        FakeEnv.Base();
        var args = Path.Combine(Sandbox.NewFolder("fakellama-lora"), "args.log");
        Environment.SetEnvironmentVariable("FAKE_PYTHON", "ok");
        Environment.SetEnvironmentVariable("FAKE_LLAMA_LOG", args);
        try
        {
            Ui.Run(() =>
            {
                var main = new MainWindow();
                main.Show();
                LoraAdapter? adapter = null;
                Ui.Expect<FineTuneWindow>(t =>
                {
                Ui.WaitFor(() => t.NextEnabled);
                t.AddExamples(Enumerable.Range(1, 12).Select(i => TrainingExample.Pair($"P{i}", $"R{i}")));
                t.GoToStepForTest(3);
                Ui.Find<TextBox>(t, "NameBox").Text = "Mi estilo";
                t.GoToStepForTest(4);
                Ui.Click(Ui.Find<Button>(t, "StartButton"));
                Ui.WaitFor(() => t.Step == 5, 30_000);
                adapter = Assert.Single(AppSettings.Current.Adapters);
                Assert.True(adapter.Active);
                Assert.Equal(("Mi estilo", ModelFile, "org/Original"), (adapter.Name, adapter.ModelPath, adapter.BaseRepo));
                Assert.True(File.Exists(adapter.Path));
                Assert.StartsWith(Adapters.Folder, adapter.Path);
                Assert.Contains("Mi estilo", Ui.Find<TextBlock>(t, "DoneText").Text);
                // El dataset llevaba los 12 ejemplos
                Assert.Equal(12, File.ReadAllLines(Path.Combine(FineTuneRunner.WorkFolder, "dataset.jsonl")).Length);
                // Quitarlo y volverlo a poner desde la casilla
                var use = Ui.Find<CheckBox>(t, "UseCheck");
                use.IsChecked = false;
                Ui.Call(t, "OnUseChanged", use, new RoutedEventArgs());
                Assert.False(adapter.Active);
                use.IsChecked = true;
                Ui.Call(t, "OnUseChanged", use, new RoutedEventArgs());
                Assert.True(adapter.Active);
                // Probarlo: cierra el asistente y el motor arranca con --lora
                Ui.Click(Ui.Find<Button>(t, "TryButton"));
                });
                new FineTuneWindow { Owner = main }.ShowDialog();
                Ui.WaitFor(() => File.Exists(args) && File.ReadAllText(args).Contains("|--lora|"), 32_000);   // con la cobertura midiendo, arrancar el motor tarda mas
                Assert.Contains("|--lora|" + adapter!.Path, File.ReadAllText(args));
                Ui.WaitFor(() => App.Engine.Adapter is not null);
                Assert.Equal("Mi estilo", App.Engine.Adapter!.Name);
                main.Close();
            });
        }
        finally
        {
            Environment.SetEnvironmentVariable("FAKE_PYTHON", null);
            Environment.SetEnvironmentVariable("FAKE_LLAMA_LOG", null);
            PythonEnv.Remove();
        }
    }

    [Fact]
    public void Asistente_ErrorDeMemoria_YParar_NoGuardanNada()
    {
        InstallPrueba();
        ServeResolve();
        FakeEnv.Install();
        FakeEnv.Base();
        try
        {
            Ui.Run(() => Teach(t =>
            {
                Ui.WaitFor(() => t.NextEnabled);
                t.AddExamples(Enumerable.Range(1, 12).Select(i => TrainingExample.Pair($"P{i}", $"R{i}")));
                t.GoToStepForTest(4);
                Environment.SetEnvironmentVariable("FAKE_PYTHON", "error");
                Ui.Wait(t.StartAsync());
                Assert.Equal(Loc.Get("FtOutOfMemory"), Ui.Find<TextBlock>(t, "ErrorText").Text);
                Assert.Equal(4, t.Step);

                Environment.SetEnvironmentVariable("FAKE_PYTHON", "slow");
                var run = t.StartAsync();
                Ui.WaitFor(() => Ui.Find<TextBlock>(t, "DetailText").Text.StartsWith(Loc.Format("FtSteps", 1, 3), StringComparison.Ordinal), 20_000);
                Ui.Click(Ui.Find<Button>(t, "CancelButton"));
                Ui.Wait(run);
                Assert.Equal(Loc.Get("FtStopped"), Ui.Find<TextBlock>(t, "ErrorText").Text);
                Assert.Empty(AppSettings.Current.Adapters);
                Assert.Equal(Visibility.Visible, Ui.Find<Button>(t, "StartButton").Visibility);
            }));
        }
        finally
        {
            Environment.SetEnvironmentVariable("FAKE_PYTHON", null);
            PythonEnv.Remove();
        }
    }

    [Fact]
    public void Ajustes_TarjetaEnseñar_ListaYQuitaLoAprendido()
    {
        var path = Path.Combine(Sandbox.NewFolder("ad"), "a.lora.gguf");
        File.WriteAllBytes(path, [1]);
        AppSettings.Current.Adapters.Add(new LoraAdapter { Name = "Soporte", Path = path, ModelPath = ModelFile, Examples = 30, Active = true });
        AppSettings.Current.Adapters.Add(new LoraAdapter { Name = "Borrado", Path = path + ".no", ModelPath = ModelFile });
        Ui.Run(() =>
        {
            var w = new SettingsWindow();
            Ui.Expect<SettingsWindow>(s =>
            {
                Assert.Equal(Loc.Get("TeachTitle"), Ui.Find<TextBlock>(s, "TeachTitle").Text);
                var list = Ui.Find<StackPanel>(s, "AdapterList");
                Assert.Single(list.Children);   // el que ya no existe en disco se olvida
                Assert.Contains("Soporte", string.Join(" ", Ui.All<TextBlock>(list).Select(b => b.Text)));
                var use = Ui.All<CheckBox>(list).Single();
                Assert.True(use.IsChecked);
                use.IsChecked = false;
                use.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
                Assert.False(AppSettings.Current.Adapters.Single().Active);
                Ui.WaitFor(() => Ui.Find<TextBlock>(s, "TeachDisk").Text.Length > 0);
                Ui.Answer("Yes");
                var delete = Ui.All<Button>(Ui.Find<StackPanel>(s, "AdapterList")).Single(b => Equals(b.ToolTip, Loc.Get("TeachDeleteAdapter")));
                Ui.Click(delete);
                Assert.Empty(AppSettings.Current.Adapters);
                Assert.False(File.Exists(path));
                Assert.Equal(Loc.Get("TeachNoAdapters"), Ui.All<TextBlock>(Ui.Find<StackPanel>(s, "AdapterList")).Single().Text);
                s.Close();
            });
            w.ShowDialog();
        });
    }

    [Fact]
    public void Ajustes_Buscador_ConFiltros_YConvertirUnModeloSafetensors()
    {
        FakeEnv.Install();
        string? searched = null;
        Net.Other = r =>
        {
            var url = r.RequestUri!.ToString();
            if (url.StartsWith("https://huggingface.co/api/models?", StringComparison.Ordinal))
            {
                searched = url;
                return BaseModelsTests.Json("""[{"id":"org/Texto","downloads":10,"tags":["safetensors","license:apache-2.0"],"pipeline_tag":"text-generation","safetensors":{"total":1000000000}}]""");
            }
            return url switch
            {
                "https://huggingface.co/api/models/org/Texto" => BaseModelsTests.Json("""{"safetensors":{"total":1000000000},"tags":["safetensors"],"cardData":{"license":"apache-2.0"}}"""),
                "https://huggingface.co/api/models/org/Texto/tree/main" => BaseModelsTests.Json("""[{"type":"file","path":"model.safetensors","size":4}]"""),
                "https://huggingface.co/org/Texto/resolve/main/model.safetensors?download=true" => BaseModelsTests.Bytes(4),
                _ => null,
            };
        };
        // llama-quantize de mentira: el exe de pruebas sale con 0 y no deja fichero → la conversion lo dice
        File.Copy(Path.Combine(AppContext.BaseDirectory, "FakeLlama.exe"), Path.Combine(EngineFolder, "llama-quantize.exe"), overwrite: true);
        Environment.SetEnvironmentVariable("FAKE_PYTHON", "ok");
        Environment.SetEnvironmentVariable("FAKE_LLAMA_EXIT", "0");
        try
        {
            Ui.Run(() =>
            {
                var w = new SettingsWindow();
                Ui.Expect<SettingsWindow>(s =>
                {
                    Ui.Find<ComboBox>(s, "KindCombo").SelectedIndex = 2;     // vision
                    Ui.Find<ComboBox>(s, "SizeCombo").SelectedIndex = 3;     // hasta 4B
                    Ui.Find<ComboBox>(s, "FormatCombo").SelectedIndex = 1;   // convertibles
                    Ui.Find<ComboBox>(s, "SortCombo").SelectedIndex = 2;     // tendencia
                    Ui.Find<CheckBox>(s, "CommercialCheck").IsChecked = true;
                    Ui.Find<TextBox>(s, "SearchBox").Text = "texto";
                    Ui.Click(Ui.Find<Button>(s, "SearchButton"));
                    Ui.WaitFor(() => Ui.Find<StackPanel>(s, "SearchList").Children.Count == 1);
                    Assert.Contains("pipeline_tag=image-text-to-text", searched);
                    Assert.Contains("num_parameters=min:0,max:4B", searched);
                    Assert.Contains("sort=trendingScore", searched);
                    var text = string.Join(" ", Ui.All<TextBlock>(Ui.Find<StackPanel>(s, "SearchList")).Select(b => b.Text));
                    Assert.Contains(Loc.Format("SearchConvertible", "1", "", "")[..20], text);
                    // Los filtros se rehacen al cambiar de idioma sin perder lo elegido
                    Loc.Use("en");
                    Assert.Equal(3, Ui.Find<ComboBox>(s, "SizeCombo").SelectedIndex);
                    Loc.Use("es");

                    var convert = Ui.All<Button>(Ui.Find<StackPanel>(s, "SearchList")).Single(b => Equals(b.ToolTip, Loc.Get("SearchConvert")));
                    string? error = null;
                    Ui.Answer("Yes");                        // confirmar
                    Ui.Answer("Ok", m => error = m);          // el aviso de que no se pudo comprimir
                    Ui.Click(convert);
                    Ui.WaitFor(() => error is not null, 30_000);
                    Assert.StartsWith(Loc.Format("SearchConvertFailed", "")[..10], error);
                    Assert.True(FineTune.BaseModels.IsDownloaded("org/Texto"));   // el original se queda para enseñarle
                    s.Close();
                });
                w.ShowDialog();
            });
        }
        finally
        {
            Environment.SetEnvironmentVariable("FAKE_PYTHON", null);
            Environment.SetEnvironmentVariable("FAKE_LLAMA_EXIT", null);
            PythonEnv.Remove();
            BaseModels.Delete("org/Texto");
        }
    }
}
