using System.Windows.Controls;
using SocLucia.Engine;
using SocLucia.Localization;
using SocLucia.Services;

namespace SocLucia.Tests;

/// <summary>
/// Elegir el fichero bueno de un repositorio, no instalar lo que no es una IA y decir en claro por que no
/// carga. Casos reales del 2026-10-22: «…imatrix.gguf», «…FastMTP-32K.gguf» y un adaptador LoRA de Bonsai
/// se bajaban como si fueran la IA, y el motor solo decia «ha terminado con el codigo 1».
/// </summary>
public class ModelFilesTests
{
    private static string Tree(params (string Name, double Gb)[] files)
        => "[" + string.Join(",", files.Select(f => "{\"type\":\"file\",\"path\":\"" + f.Name + "\",\"lfs\":{\"size\":" + (long)(f.Gb * (1L << 30)) + "}}")) + "]";

    [Fact]
    public void De_un_repositorio_sin_Q4_K_M_no_se_lleva_el_ayudante_ni_la_imatrix()
    {
        // HauhauCS/Qwen3.8-27B-…-MTP-GGUF: cuantizados con nombre propio (Q4_K_P) y un ayudante FastMTP pequeño.
        var hauhau = ModelCatalog.PickFile(Tree(("Qwen3.8-27B-Aggressive-FastMTP-32K.gguf", 0.84), ("Qwen3.8-27B-Aggressive-Q8_K_P.gguf", 29.3),
            ("Qwen3.8-27B-Aggressive-Q4_K_P.gguf", 16.69), ("Qwen3.8-27B-Aggressive-Q3_K_P.gguf", 12.5), ("mmproj-Qwen3.8-27B-BF16.gguf", 0.87)));
        Assert.Equal("Qwen3.8-27B-Aggressive-Q4_K_P.gguf", hauhau.Name);
        // SC117/Qwen-AgentWorld-35B-A3B-MTP-…-APEX-GGUF: sin cuantizado en el nombre y con una imatrix de 0,18 GB.
        var apex = ModelCatalog.PickFile(Tree(("AgentWorld-MTP-APEX-I-Balanced.gguf", 24.1), ("AgentWorld-MTP-APEX-I-Mini.gguf", 12.8),
            ("AgentWorld-MTP-BF16.gguf", 66.2), ("AgentWorld-MTP-Uncensored.imatrix.gguf", 0.18), ("mmproj-F16.gguf", 0.84)));
        Assert.Equal("AgentWorld-MTP-APEX-I-Mini.gguf", apex.Name);
        // Un auxiliar con otro nombre: menos del 5 % del mayor no es un modelo.
        Assert.Equal("m-grande.gguf", ModelCatalog.PickFile(Tree(("m-raro.gguf", 0.1), ("m-grande.gguf", 8), ("m-enorme.gguf", 16))).Name);
        // Repositorio solo con un adaptador: no hay IA que bajar
        Assert.Throws<FileNotFoundException>(() => ModelCatalog.PickFile(Tree(("bonsai2-27b-uncensored-own42-layers15-63-lora.gguf", 0.006))));
    }

    [Theory]
    [InlineData("x.imatrix.gguf", true)]
    [InlineData("mmproj-F16.gguf", true)]
    [InlineData("Qwen3.8-27B-Aggressive-FastMTP-32K.gguf", true)]
    [InlineData("modelo-draft-Q4_K_M.gguf", true)]
    [InlineData("bonsai2-27b-uncensored-own42-layers15-63-lora.gguf", true)]
    [InlineData("algo_adapter.gguf", true)]
    [InlineData("Qwen-AgentWorld-35B-A3B-MTP-Uncensored-APEX-I-Mini.gguf", false)]   // «MTP» en el nombre del modelo no lo hace auxiliar
    [InlineData("Qwen3.5-4B-Q4_K_M.gguf", false)]
    [InlineData("explorador-Q4_K_M.gguf", false)]   // «lora» dentro de una palabra no cuenta
    public void Ficheros_auxiliares_por_el_nombre(string name, bool helper) => Assert.Equal(helper, ModelCatalog.IsHelperFile(name));

    [Fact]
    public void Lo_que_no_es_una_IA_se_dice_por_la_cabecera()
    {
        var folder = Sandbox.NewFolder("cabeceras");
        string Write(string name, byte[] bytes) { var p = Path.Combine(folder, name); File.WriteAllBytes(p, bytes); return p; }
        var good = Write("bueno-Q4_K_M.gguf", FakeGguf.Build(("general.architecture", "qwen35"), ("qwen35.context_length", 32768u)));
        var adapter = Write("bonsai-adaptador-Q4.gguf", FakeGguf.Build(("general.architecture", "qwen35"), ("general.type", "adapter"), ("adapter.type", "lora")));
        var vision = Write("vista-Q4.gguf", FakeGguf.Build(("general.architecture", "clip")));
        var noArch = Write("sin-arquitectura-Q4.gguf", FakeGguf.Build(("general.name", "x")));
        var notGguf = Write("raro-Q4.gguf", [1, 2, 3]);
        Assert.Null(ModelCatalog.Unusable(good));
        Assert.Equal(Loc.Get("ModelIsAdapter"), ModelCatalog.Unusable(adapter));
        Assert.Equal(Loc.Get("ModelHelperFile"), ModelCatalog.Unusable(vision));
        Assert.Equal(Loc.Get("ModelNoArchitecture"), ModelCatalog.Unusable(noArch));
        Assert.Null(ModelCatalog.Unusable(notGguf));   // sin cabecera GGUF: si no carga, el motor dira por que
        Assert.Equal(Loc.Get("ModelHelperFile"), ModelCatalog.Unusable(Write("x.imatrix.gguf", [1])));
        Assert.Equal("adapter", GgufInfo.Read(adapter)!.Type);
    }

    [Theory]
    [InlineData("0.00.936.778 E llama_model_load: error loading model: unknown model architecture: ''", "EngineErrNoArch")]
    [InlineData("0.00.936.778 E llama_model_load: error loading model: unknown model architecture: 'bitnet9'", "EngineErrArch")]
    [InlineData("0.02.481.822 E llama_model_load: error loading model: check_tensor_dims: tensor 'output.weight' has wrong shape; expected   5120, 248320, got   5120,  32768,      1,      1", "EngineErrTensors")]
    [InlineData("0.01.244.389 E llama_model_load: error loading model: error loading model hyperparameters: key not found in model: qwen35.context_length", "EngineErrMissingKey")]
    [InlineData("E ggml_vulkan: Device memory allocation of size 123 failed. ErrorOutOfDeviceMemory", "EngineErrMemory")]
    [InlineData("E llama_model_load: error loading model: tensor 'blk.0' data is not within the file bounds, model is corrupted or incomplete", "EngineErrCorrupt")]
    public void El_error_del_motor_en_claro(string line, string key)
    {
        var text = EngineErrors.Explain([line, "0.00.936.786 E llama_model_load_from_file_impl: failed to load model"], 1, "Mi IA");
        Assert.StartsWith(Loc.Format(key, "Mi IA", "§", "§").Split('§')[0], text);
        Assert.Contains("Mi IA", text);
    }

    [Fact]
    public void Un_adaptador_sin_datos_del_modelo_se_explica_como_adaptador()
    {
        var text = EngineErrors.Explain(["E llama_model_load: error loading model: error loading model hyperparameters: key not found in model: qwen35.context_length"], 1, "bonsai2 27b uncensored own42 layers15 63 lora");
        Assert.Equal(Loc.Format("EngineErrAdapter", "bonsai2 27b uncensored own42 layers15 63 lora", "qwen35.context_length"), text);
        Assert.Equal(Loc.Format("EngineErrOther", "X", "algo nuevo"), EngineErrors.Explain(["0.1.2.3 E srv load_model: algo nuevo"], 1, "X"));
        Assert.Equal(Loc.Format("EngineExited", 3), EngineErrors.Explain([], 3, "X"));
        Assert.True(EngineErrors.IsErrorLine("0.00.936.778 E llama_model_load: x"));
        Assert.False(EngineErrors.IsErrorLine("0.00.910.354 I srv init: The UI is disabled"));
    }
}

/// <summary>El motor que no carga, en la ventana: el chat lo dice en claro y Ajustes lo marca en la IA.</summary>
public class EngineFailureWindowTests : UiTest
{
    [Fact]
    public void Si_la_IA_no_carga_se_dice_por_que_y_Ajustes_la_marca()
    {
        AppSettings.Current.Remember(new InstalledModel { Name = "Prueba", Path = ModelFile });
        Environment.SetEnvironmentVariable("FAKE_LLAMA_STDERR", "0.02.481.822 E llama_model_load: error loading model: check_tensor_dims: tensor 'output.weight' has wrong shape; expected 5120, 248320, got 5120, 32768|0.03.012.214 E srv llama_server: exiting due to model loading error");
        Environment.SetEnvironmentVariable("FAKE_LLAMA_EXIT", "1");
        Net.Healthy = false;
        try
        {
            Ui.Run(() =>
            {
                var start = App.Engine.EnsureReadyAsync(null, CancellationToken.None);
                Ui.Wait(start);
                Assert.IsType<EngineLoadException>(start.Exception?.InnerException);
                Ui.WaitFor(() => AppSettings.Current.BrokenModels.ContainsKey(ModelFile));
                var why = AppSettings.Current.BrokenModels[ModelFile];
                Assert.StartsWith(Loc.Format("EngineErrTensors", "Prueba", "§").Split('§')[0], why);
                Assert.Equal(EngineState.Error, App.Engine.State);
                Assert.Equal(why, App.Engine.Detail);
                var s = new SettingsWindow();
                Ui.Expect<SettingsWindow>(set =>
                {
                    Ui.WaitFor(() => Ui.All<TextBlock>(Ui.Find<StackPanel>(set, "InstalledList")).Any(t => t.Text == "⚠ " + why));
                    set.Close();
                });
                s.ShowDialog();
            });
        }
        finally
        {
            Environment.SetEnvironmentVariable("FAKE_LLAMA_STDERR", null);
            Environment.SetEnvironmentVariable("FAKE_LLAMA_EXIT", null);
            AppSettings.Current.BrokenModels.Clear();
        }
    }
}
