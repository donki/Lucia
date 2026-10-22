using SocLucia.Engine;
using SocLucia.Services;

namespace SocLucia.Tests;

/// <summary>Lo que se hace con las respuestas de la API de Hugging Face (JSON de ejemplo, sin red).</summary>
public class HuggingFaceTests
{
    private const long GiB = 1024L * 1024 * 1024;

    [Fact]
    public void Pick_file_prefers_q4_k_m_and_skips_shards_and_mmproj()
    {
        var file = ModelCatalog.PickFile("""
            [
              {"path":"README.md","size":10},
              {"path":"Modelo-Q8_0.gguf","size":800},
              {"path":"Modelo-Q4_K_M-00001-of-00002.gguf","size":100},
              {"path":"mmproj-F16.gguf","size":50},
              {"path":"Modelo-Q4_K_M.gguf","size":1,"lfs":{"size":400}}
            ]
            """);
        Assert.Equal(new ModelCatalog.RepoFile("Modelo-Q4_K_M.gguf", 400), file);
    }

    [Fact]
    public void Pick_file_follows_the_preference_order_then_the_smallest()
    {
        Assert.Equal("m-IQ4_XS.gguf", ModelCatalog.PickFile("""[{"path":"m-Q8_0.gguf"},{"path":"m-IQ4_XS.gguf"}]""").Name);
        var smallest = ModelCatalog.PickFile("""[{"path":"m-F32.gguf","size":900},{"path":"m-F16.gguf","size":"raro"},{"path":"m-BF16.gguf","size":500}]""");
        Assert.Equal(new ModelCatalog.RepoFile("m-BF16.gguf", 500), smallest);
        Assert.Throws<FileNotFoundException>(() => ModelCatalog.PickFile("""[{"path":"a.safetensors"}]"""));
        Assert.Throws<FileNotFoundException>(() => ModelCatalog.PickFile("[]"));
    }

    [Fact]
    public void Pick_mmproj_prefers_f16_then_bf16_then_the_smallest()
    {
        Assert.Equal("mmproj-F16.gguf", ModelCatalog.PickMmproj("""[{"path":"mmproj-F32.gguf"},{"path":"mmproj-F16.gguf"},{"path":"m-Q4_K_M.gguf"}]""")!.Name);
        Assert.Equal("mmproj-BF16.gguf", ModelCatalog.PickMmproj("""[{"path":"mmproj-F32.gguf"},{"path":"mmproj-BF16.gguf"}]""")!.Name);
        // BF16 delante en la lista no le quita el sitio a F16.
        Assert.Equal("mmproj-model-f16.gguf", ModelCatalog.PickMmproj("""[{"path":"mmproj-BF16.gguf"},{"path":"mmproj-model-f16.gguf"}]""")!.Name);
        var smallest = ModelCatalog.PickMmproj("""[{"path":"mmproj-Q8.gguf","lfs":{"size":9}},{"path":"mmproj-Q4.gguf","lfs":{"size":3}},{"path":"mmproj-x.gguf"}]""");
        Assert.Equal(new ModelCatalog.RepoFile("mmproj-Q4.gguf", 3), smallest);
        Assert.Null(ModelCatalog.PickMmproj("""[{"path":"m-Q4_K_M.gguf"}]"""));
    }

    [Theory]
    [InlineData("qwen", true, "https://huggingface.co/api/models?sort=downloads&direction=-1&limit=30&search=qwen&filter=gguf")]
    [InlineData("  ", false, "https://huggingface.co/api/models?sort=downloads&direction=-1&limit=30")]
    [InlineData("imágenes anime", false, "https://huggingface.co/api/models?sort=downloads&direction=-1&limit=30&search=anime&pipeline_tag=text-to-image")]
    [InlineData("VOZ tts catalan", true, "https://huggingface.co/api/models?sort=downloads&direction=-1&limit=30&search=catalan&pipeline_tag=text-to-speech&filter=gguf")]
    [InlineData("whisper", false, "https://huggingface.co/api/models?sort=downloads&direction=-1&limit=30&pipeline_tag=automatic-speech-recognition")]
    [InlineData("c# & más", false, "https://huggingface.co/api/models?sort=downloads&direction=-1&limit=30&search=c%23%20%26%20m%C3%A1s")]
    public void Search_url_turns_keywords_into_pipeline_tags(string query, bool onlyGguf, string url)
        => Assert.Equal(url, HuggingFace.SearchUrl(query, onlyGguf));

    [Fact]
    public void Candidates_read_license_gguf_kind_and_gating()
    {
        var list = HuggingFace.Candidates("""
            [
              {"id":"unsloth/Qwen3.5-4B-GGUF","downloads":1000,"likes":20,"tags":["gguf","license:apache-2.0"],"pipeline_tag":"text-generation","gated":false},
              {"id":"org/imagen","downloads":5,"tags":["diffusers","safetensors"],"gated":"manual"},
              {"id":"org/sin-nada","gated":null,"tags":"no-es-lista","pipeline_tag":7},
              {"id":""},
              {"downloads":3}
            ]
            """);
        Assert.Equal(3, list.Count);
        Assert.Equal(new HuggingFace.Candidate("unsloth/Qwen3.5-4B-GGUF", 1000, 20, "apache-2.0", false, true, "text-generation"), list[0]);
        Assert.Equal(new HuggingFace.Candidate("org/imagen", 5, 0, "?", true, false, "diffusers", 0, true), list[1]);
        Assert.Equal(new HuggingFace.Candidate("org/sin-nada", 0, 0, "?", false, false, ""), list[2]);
    }

    [Theory]
    [InlineData("unsloth/Qwen3.5-4B-GGUF", "Qwen3.5 4B (unsloth)")]
    [InlineData("bartowski/Phi_4_mini_gguf", "Phi 4 mini (bartowski)")]
    [InlineData("solo-nombre.gguf", "solo nombre")]
    [InlineData("org/Modelo", "Modelo (org)")]
    public void Name_for_a_repository(string repo, string name) => Assert.Equal(name, HuggingFace.NameFor(repo));

    [Theory]
    [InlineData("", true)]
    [InlineData("text-generation", true)]
    [InlineData("image-text-to-text", true)]
    [InlineData("conversational", true)]
    [InlineData("text-to-image", false)]
    [InlineData("diffusers", false)]
    public void Text_models_are_the_ones_llama_server_runs(string kind, bool text) => Assert.Equal(text, HuggingFace.TextModel(kind));

    [Fact]
    public void Rank_puts_runnable_fitting_models_first_then_by_downloads()
    {
        HuggingFace.Hit Hit(string repo, long bytes, long downloads, bool runnable)
            => new(new CatalogModel(repo, repo, bytes, "MIT", "CatalogHf"), "f.gguf", downloads, 0, false, runnable, "gguf");
        var pc = new PcProfile("cpu", 8, 16 * GiB, null, 0);
        var ranked = HuggingFace.Rank(
        [
            Hit("imagen", 0, 1_000_000, false),
            Hit("lento", 7000L * 1024 * 1024, 10, true),
            Hit("optimo-poco", 1000L * 1024 * 1024, 5, true),
            Hit("optimo-mucho", 2000L * 1024 * 1024, 50, true),
            Hit("enorme", 40 * GiB, 999, true),
        ], pc);
        Assert.Equal(["optimo-mucho", "optimo-poco", "lento", "enorme", "imagen"], ranked.Select(h => h.Model.Repo).ToArray());
        Assert.Equal("https://huggingface.co/lento", ranked[2].Url);
    }
}

public class ModelDownloadShortcutsTests
{
    [Fact]
    public async Task Download_is_skipped_when_the_file_is_already_there_with_its_size()
    {
        Sandbox.ResetSettings();
        Directory.CreateDirectory(Paths.Models);
        var path = Path.Combine(Paths.Models, "ya-esta-Q4_K_M.gguf");
        File.WriteAllBytes(path, new byte[42]);
        var model = ModelCatalog.All[0];
        Assert.Equal(path, await ModelCatalog.DownloadAsync(model, new ModelCatalog.RepoFile("sub/ya-esta-Q4_K_M.gguf", 42), null!, CancellationToken.None));
        Assert.Equal(path, await ModelCatalog.DownloadAsync(model, new ModelCatalog.RepoFile("ya-esta-Q4_K_M.gguf", null), null!, CancellationToken.None));
        File.Delete(path);
    }

    [Fact]
    public async Task Mmproj_needs_a_known_repository()
    {
        var unknown = new InstalledModel { Name = "Importado a mano", Path = Path.Combine(Sandbox.Root, "x.gguf") };
        Assert.Null(await ModelCatalog.DownloadMmprojAsync(unknown, null!, CancellationToken.None));
    }
}

public class ModelLibraryCrossVolumeTests
{
    [Fact]
    public async Task Move_between_volumes_copies_through_a_part_file_with_progress()
    {
        // El arenero esta en %TEMP% (C:); las pruebas se ejecutan desde otra unidad si el repo esta en D:.
        var other = Path.Combine(AppContext.BaseDirectory, "mover-" + Guid.NewGuid().ToString("N")[..6]);
        if (string.Equals(Path.GetPathRoot(other), Path.GetPathRoot(Sandbox.Root), StringComparison.OrdinalIgnoreCase))
            return;   // mismo volumen: ya lo cubre la otra prueba
        var s = Sandbox.ResetSettings();
        var from = Sandbox.NewFolder("origen");
        s.ModelsFolder = from;
        var data = new byte[3 * 1024 * 1024 + 7];
        Random.Shared.NextBytes(data);
        File.WriteAllBytes(Path.Combine(from, "grande.gguf"), data);
        var reports = new List<ModelLibrary.MoveProgress>();
        try
        {
            await ModelLibrary.MoveAsync(other, new SyncProgress<ModelLibrary.MoveProgress>(reports.Add), CancellationToken.None);
            Assert.Equal(data, File.ReadAllBytes(Path.Combine(other, "grande.gguf")));
            Assert.False(File.Exists(Path.Combine(from, "grande.gguf")));
            Assert.False(File.Exists(Path.Combine(other, "grande.gguf.part")));
            Assert.True(reports.Count >= 2);
            Assert.Equal(data.Length, reports[^1].Done);
        }
        finally
        {
            Sandbox.ResetSettings();
            try { Directory.Delete(other, recursive: true); } catch (Exception) { }
        }
    }
}
