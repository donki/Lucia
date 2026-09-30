using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using SocLucia.Engine;
using SocLucia.Services;

namespace SocLucia.Tests;

public class ChatClientTests
{
    private static async Task<List<ChatDelta>> Collect(string baseUrl, IEnumerable<ChatMessage> messages, bool thinking = false, JsonArray? tools = null)
    {
        var list = new List<ChatDelta>();
        await foreach (var d in ChatClient.StreamAsync(baseUrl, messages, thinking, 100, tools, CancellationToken.None))
            list.Add(d);
        return list;
    }

    private static LocalServer Sse(string body, int status = 200, string type = "text/event-stream")
        => new(ctx => LocalServer.Write(ctx, status, type, body));

    [Fact]
    public async Task Streams_content_and_reasoning_and_the_finish_reason()
    {
        using var server = Sse(
            ": comentario\n\n" +
            "data: {\"choices\":[{\"delta\":{\"reasoning_content\":\"pienso\"}}]}\n\n" +
            "data: {\"choices\":[{\"delta\":{\"content\":\"Hola\"}}]}\n\n" +
            "data: no-es-json\n\n" +
            "data: {\"choices\":[{\"delta\":{\"content\":\"\"}}]}\n\n" +
            "data: {\"choices\":[]}\n\n" +
            "data: {\"choices\":[{\"delta\":{\"content\":\" mundo\"},\"finish_reason\":\"stop\"}]}\n\n" +
            "data: [DONE]\n\n" +
            "data: {\"choices\":[{\"delta\":{\"content\":\"despues\"}}]}\n\n");
        var deltas = await Collect(server.BaseUrl, [new ChatMessage("user", "hola")]);
        Assert.Equal(4, deltas.Count);
        Assert.Equal("pienso", deltas[0].Reasoning);
        Assert.Null(deltas[0].Content);
        Assert.Equal("Hola", deltas[1].Content);
        Assert.Equal(" mundo", deltas[2].Content);
        Assert.Equal("stop", deltas[3].FinishReason);
        Assert.Null(deltas[3].ToolCalls);
    }

    [Fact]
    public async Task Joins_tool_call_fragments_by_index_and_names_missing_ids()
    {
        using var server = Sse(
            "data: {\"choices\":[{\"delta\":{\"tool_calls\":[{\"index\":1,\"function\":{\"name\":\"list_folder\",\"arguments\":\"{\\\"pa\"}}]}}]}\n" +
            "data: {\"choices\":[{\"delta\":{\"tool_calls\":[{\"index\":0,\"id\":\"call_a\",\"function\":{\"name\":\"read_\",\"arguments\":\"{\\\"path\\\":\"}}]}}]}\n" +
            "data: {\"choices\":[{\"delta\":{\"tool_calls\":[{\"index\":0,\"function\":{\"name\":\"file\",\"arguments\":\"\\\"a.txt\\\"}\"}}]}}]}\n" +
            "data: {\"choices\":[{\"delta\":{\"tool_calls\":[{\"index\":1,\"function\":{\"arguments\":\"th\\\":\\\"C:\\\\\\\\\\\"}\"}}]}}]}\n" +
            "data: {\"choices\":[{\"delta\":{},\"finish_reason\":\"tool_calls\"}]}\n" +
            "data: [DONE]\n");
        var deltas = await Collect(server.BaseUrl, [new ChatMessage("user", "x")]);
        Assert.Equal(2, deltas.Count);
        var calls = deltas[0].ToolCalls!;
        Assert.Equal(2, calls.Count);
        Assert.Equal(("call_a", "read_file", "{\"path\":\"a.txt\"}"), (calls[0].Id, calls[0].Name, calls[0].Arguments));
        Assert.Equal(("call_1", "list_folder"), (calls[1].Id, calls[1].Name));
        Assert.Equal("C:\\", JsonNode.Parse(calls[1].Arguments)!["path"]!.GetValue<string>());
        Assert.Equal("tool_calls", deltas[1].FinishReason);
        var json = calls[0].ToJson();
        Assert.Equal("function", json["type"]!.GetValue<string>());
        Assert.Equal("read_file", json["function"]!["name"]!.GetValue<string>());
    }

    [Fact]
    public async Task Sends_messages_tools_images_and_thinking_in_openai_format()
    {
        using var server = Sse("data: [DONE]\n");
        var calls = new JsonArray(new ToolCall { Id = "c1", Name = "read_file", Arguments = "{}" }.ToJson());
        var tools = new JsonArray(new JsonObject { ["type"] = "function" });
        await Collect(server.BaseUrl,
        [
            new ChatMessage("system", "sé breve"),
            new ChatMessage("user", "mira", Images: ["data:image/png;base64,AAAA"]),
            new ChatMessage("assistant", "", ToolCalls: calls),
            new ChatMessage("assistant", "voy", ToolCalls: calls),
            new ChatMessage("tool", "resultado", ToolCallId: "c1"),
        ], thinking: true, tools: tools);
        var body = JsonNode.Parse(server.Bodies.Single())!;
        Assert.Equal("/v1/chat/completions", server.Requests.Single().Url!.AbsolutePath);
        Assert.True(body["stream"]!.GetValue<bool>());
        Assert.Equal(300, body["max_tokens"]!.GetValue<int>());   // con pensamiento, el triple
        Assert.True(body["chat_template_kwargs"]!["enable_thinking"]!.GetValue<bool>());
        Assert.Equal("auto", body["tool_choice"]!.GetValue<string>());
        var m = body["messages"]!.AsArray();
        Assert.Equal("sé breve", m[0]!["content"]!.GetValue<string>());
        var parts = m[1]!["content"]!.AsArray();
        Assert.Equal("image_url", parts[0]!["type"]!.GetValue<string>());
        Assert.Equal("data:image/png;base64,AAAA", parts[0]!["image_url"]!["url"]!.GetValue<string>());
        Assert.Equal("mira", parts[1]!["text"]!.GetValue<string>());
        Assert.Null(m[2]!["content"]);
        Assert.Equal("c1", m[2]!["tool_calls"]![0]!["id"]!.GetValue<string>());
        Assert.Equal("voy", m[3]!["content"]!.GetValue<string>());
        Assert.Equal("c1", m[4]!["tool_call_id"]!.GetValue<string>());
        Assert.Equal("resultado", m[4]!["content"]!.GetValue<string>());
    }

    [Fact]
    public async Task Without_tools_or_thinking_the_body_is_minimal()
    {
        using var server = Sse("data: [DONE]\n");
        await Collect(server.BaseUrl, [new ChatMessage("user", "hola")], thinking: false, tools: []);
        var body = JsonNode.Parse(server.Bodies.Single())!.AsObject();
        Assert.False(body.ContainsKey("tools"));
        Assert.Equal(100, body["max_tokens"]!.GetValue<int>());
        Assert.False(body["chat_template_kwargs"]!["enable_thinking"]!.GetValue<bool>());
    }

    [Theory]
    [InlineData("{\"error\":{\"message\":\"context too long\"}}", "500: context too long")]
    [InlineData("{\"error\":\"simple\"}", "500: simple")]
    [InlineData("{\"otro\":1}", "500: {\"otro\":1}")]
    [InlineData("texto plano", "500: texto plano")]
    [InlineData("[1]", "500: [1]")]
    [InlineData("{\"error\":{\"code\":1}}", "500: {\"code\":1}")]
    public async Task Http_errors_carry_the_server_message(string body, string message)
    {
        using var server = Sse(body, 500, "application/json");
        var ex = await Assert.ThrowsAsync<HttpRequestException>(() => Collect(server.BaseUrl, [new ChatMessage("user", "x")]));
        Assert.Equal(message, ex.Message);
    }

    [Fact]
    public async Task Long_error_bodies_are_cut_to_300_characters()
    {
        using var server = Sse(new string('e', 1000), 400, "text/plain");
        var ex = await Assert.ThrowsAsync<HttpRequestException>(() => Collect(server.BaseUrl, [new ChatMessage("user", "x")]));
        Assert.Equal("400: " + new string('e', 300), ex.Message);
    }
}

public class DownloaderTests
{
    private static readonly byte[] Payload = Encoding.ASCII.GetBytes(string.Concat(Enumerable.Range(0, 5000).Select(i => (char)('a' + i % 26))));
    private static string Sha(byte[] b) => Convert.ToHexString(SHA256.HashData(b)).ToLowerInvariant();

    /// <summary>Un servidor que sirve <see cref="Payload"/> y, si se quiere, entiende Range.</summary>
    private static LocalServer Server(bool ranges, bool rangeNotSatisfiable = false, long? headLength = null)
        => new(async ctx =>
        {
            var range = ctx.Request.Headers["Range"];
            if (ctx.Request.HttpMethod == "HEAD")
            {
                ctx.Response.StatusCode = 200;
                ctx.Response.ContentLength64 = headLength ?? Payload.Length;
                ctx.Response.Close();
                return;
            }
            if (range is not null && rangeNotSatisfiable)
            {
                ctx.Response.StatusCode = 416;
                ctx.Response.Close();
                return;
            }
            var from = range is not null && ranges ? int.Parse(range["bytes=".Length..].TrimEnd('-')) : 0;
            ctx.Response.StatusCode = from > 0 ? 206 : 200;
            ctx.Response.ContentLength64 = Payload.Length - from;
            await ctx.Response.OutputStream.WriteAsync(Payload.AsMemory(from));
            ctx.Response.Close();
        });

    private sealed class Recorder : IProgress<Downloader.Progress>
    {
        public List<Downloader.Progress> Reports { get; } = [];
        public void Report(Downloader.Progress value) { lock (Reports) Reports.Add(value); }
    }

    [Fact]
    public async Task Downloads_to_a_temp_file_and_renames_it_with_progress()
    {
        using var server = Server(ranges: false);
        var dest = Path.Combine(Sandbox.NewFolder("dl"), "sub", "f.bin");
        var progress = new Recorder();
        await Downloader.DownloadAsync(server.BaseUrl + "/f", dest, Sha(Payload), progress, CancellationToken.None);
        Assert.Equal(Payload, File.ReadAllBytes(dest));
        Assert.False(File.Exists(dest + ".part"));
        var last = progress.Reports[^1];
        Assert.Equal((Payload.Length, (long?)Payload.Length), (last.Received, last.Total));
        Assert.Equal(1.0, last.Fraction);
        Assert.Null(new Downloader.Progress(5, null).Fraction);
        Assert.Null(new Downloader.Progress(5, 0).Fraction);
        Assert.Equal(Sha(Payload), await Downloader.HashAsync(dest, CancellationToken.None));
    }

    [Fact]
    public async Task Resumes_a_partial_download_with_range()
    {
        using var server = Server(ranges: true);
        var dest = Path.Combine(Sandbox.NewFolder("dl"), "f.bin");
        File.WriteAllBytes(dest + ".part", Payload[..1200]);
        await Downloader.DownloadAsync(server.BaseUrl + "/f", dest, Sha(Payload), null, CancellationToken.None);
        Assert.Equal(Payload, File.ReadAllBytes(dest));
        Assert.Equal("bytes=1200-", server.Requests[0].Headers["Range"]);
    }

    [Fact]
    public async Task Starts_again_when_the_server_ignores_range()
    {
        using var server = Server(ranges: false);
        var dest = Path.Combine(Sandbox.NewFolder("dl"), "f.bin");
        File.WriteAllBytes(dest + ".part", Encoding.ASCII.GetBytes("basura"));
        await Downloader.DownloadAsync(server.BaseUrl + "/f", dest, null, null, CancellationToken.None);
        Assert.Equal(Payload, File.ReadAllBytes(dest));
    }

    [Fact]
    public async Task A_complete_part_file_is_accepted_when_range_is_not_satisfiable()
    {
        using var server = Server(ranges: true, rangeNotSatisfiable: true);
        var dest = Path.Combine(Sandbox.NewFolder("dl"), "f.bin");
        File.WriteAllBytes(dest + ".part", Payload);
        var progress = new Recorder();
        await Downloader.DownloadAsync(server.BaseUrl + "/f", dest, Sha(Payload), progress, CancellationToken.None);
        Assert.Equal(Payload, File.ReadAllBytes(dest));
        Assert.Equal(Payload.Length, progress.Reports.Single().Received);
    }

    [Fact]
    public async Task A_stale_part_file_is_thrown_away_when_range_is_not_satisfiable()
    {
        // El HEAD dice otro tamaño: se borra el .part y se baja de cero (ya sin Range).
        using var server = Server(ranges: true, rangeNotSatisfiable: true, headLength: 99);
        var dest = Path.Combine(Sandbox.NewFolder("dl"), "f.bin");
        File.WriteAllBytes(dest + ".part", Payload[..10]);
        await Downloader.DownloadAsync(server.BaseUrl + "/f", dest, null, null, CancellationToken.None);
        Assert.Equal(Payload, File.ReadAllBytes(dest));
    }

    [Fact]
    public async Task A_wrong_hash_deletes_the_download()
    {
        using var server = Server(ranges: false);
        var dest = Path.Combine(Sandbox.NewFolder("dl"), "f.bin");
        var ex = await Assert.ThrowsAsync<InvalidDataException>(() => Downloader.DownloadAsync(server.BaseUrl + "/f", dest, new string('0', 64), null, CancellationToken.None));
        Assert.Contains("SHA-256", ex.Message);
        Assert.False(File.Exists(dest));
        Assert.False(File.Exists(dest + ".part"));
    }

    [Fact]
    public async Task Http_errors_propagate()
    {
        using var server = new LocalServer(ctx => LocalServer.Write(ctx, 404, "text/plain", "no"));
        var dest = Path.Combine(Sandbox.NewFolder("dl"), "f.bin");
        await Assert.ThrowsAsync<HttpRequestException>(() => Downloader.DownloadAsync(server.BaseUrl + "/f", dest, null, null, CancellationToken.None));
        Assert.False(File.Exists(dest));
    }
}

public class ModelCatalogTests
{
    private const long GiB = 1024L * 1024 * 1024;
    private static PcProfile Pc(long ramGiB, long vramGiB = 0) => new("cpu", 8, ramGiB * GiB, vramGiB > 0 ? "gpu" : null, vramGiB * GiB);

    [Fact]
    public void Catalog_is_sorted_from_big_to_small_with_open_licences()
    {
        Assert.Equal(ModelCatalog.All.OrderByDescending(m => m.ApproxBytes), ModelCatalog.All);
        Assert.All(ModelCatalog.All, m => Assert.EndsWith("-GGUF", m.Repo));
        Assert.All(ModelCatalog.All, m => Assert.StartsWith("Catalog", m.Blurb));
        Assert.All(ModelCatalog.All, m => Assert.True(m.MinRamBytes > m.ApproxBytes));
    }

    [Fact]
    public void Fit_on_cpu_only_and_with_a_gpu()
    {
        var small = ModelCatalog.All.Single(m => m.Name == "Qwen3.5 2B");
        var mid = ModelCatalog.All.Single(m => m.Name == "Ministral 3 8B");
        var big = ModelCatalog.All.Single(m => m.Name == "Qwen3.8 27B");
        var gemma12 = ModelCatalog.All.Single(m => m.Name == "Gemma 4 12B");
        Assert.Equal(ModelFit.TooBig, ModelCatalog.FitOf(big, Pc(16)));
        Assert.Equal(ModelFit.Optimal, ModelCatalog.FitOf(small, Pc(16)));
        Assert.Equal(ModelFit.Ok, ModelCatalog.FitOf(mid, Pc(16)));
        Assert.Equal(ModelFit.Slow, ModelCatalog.FitOf(gemma12, Pc(16)));
        Assert.Equal(ModelFit.Optimal, ModelCatalog.FitOf(mid, Pc(32, 8)));
        Assert.Equal(ModelFit.Ok, ModelCatalog.FitOf(gemma12, Pc(32, 6)));
        Assert.Equal(ModelFit.Slow, ModelCatalog.FitOf(big, Pc(64, 6)));
        Assert.False(Pc(16, 1).HasUsableGpu);   // integrada: cuenta como sin grafica
        Assert.Equal(ModelFit.Ok, ModelCatalog.FitOf(mid, Pc(16, 1)));
    }

    [Fact]
    public void Recommended_is_the_biggest_optimal_or_the_biggest_that_fits()
    {
        Assert.Equal("Qwen3.5 4B", ModelCatalog.Recommended(Pc(16))!.Name);
        Assert.Equal("Qwen3.8 27B", ModelCatalog.Recommended(Pc(64, 24))!.Name);
        Assert.Equal("Gemma 3 1B", ModelCatalog.Recommended(Pc(3))!.Name);
        Assert.Null(ModelCatalog.Recommended(Pc(1)));
        Assert.Empty(ModelCatalog.Fitting(0));
        Assert.Equal(ModelCatalog.All.Length, ModelCatalog.Fitting(long.MaxValue).Count());
    }

    [Theory]
    [InlineData("C:\\m\\Qwen3.5-4B-Q4_K_M.gguf", "Qwen3.5 4B")]
    [InlineData("gemma-3-1b-it.Q8_0.gguf", "gemma 3 1b it")]
    [InlineData("Modelo_Raro-F16.gguf", "Modelo Raro")]
    [InlineData("phi-4-mini-IQ4_XS.gguf", "phi 4 mini")]
    [InlineData("sin-cuantizado.gguf", "sin cuantizado")]
    public void Name_from_file(string path, string name) => Assert.Equal(name, ModelCatalog.NameFromFile(path));

    [Fact]
    public void Mmproj_sits_next_to_the_model() => Assert.Equal("C:\\m\\a.gguf.mmproj.gguf", ModelCatalog.MmprojPathFor("C:\\m\\a.gguf"));

    [Fact]
    public void Total_ram_is_positive() => Assert.True(ModelCatalog.TotalRamBytes() > GiB);

    [Fact]
    public void Installed_files_use_the_known_card_the_catalog_or_the_file_name()
    {
        var s = Sandbox.ResetSettings();
        var folder = Sandbox.NewFolder("modelos");
        s.ModelsFolder = folder;
        foreach (var f in new[] { "Qwen3.5-4B-Q4_K_M.gguf", "propio-Q5_K_M.gguf", "conocido.gguf", "no-es-modelo.txt" })
            File.WriteAllText(Path.Combine(folder, f), "");
        s.Remember(new InstalledModel { Name = "Mi IA", Path = Path.Combine(folder, "conocido.gguf"), License = "MIT" });
        var list = ModelCatalog.InstalledFiles();
        Assert.Equal(["Mi IA", "propio", "Qwen3.5 4B"], list.Select(m => m.Name).ToArray());
        Assert.Equal("Apache-2.0", list[2].License);
        Assert.Null(list[1].License);

        s.ModelsFolder = Path.Combine(folder, "no-existe");
        Assert.Empty(ModelCatalog.InstalledFiles());
        Sandbox.ResetSettings();
    }

    [Fact]
    public void Delete_removes_the_file_and_forgets_the_active_model()
    {
        var s = Sandbox.ResetSettings();
        var file = Path.Combine(Sandbox.NewFolder("borrar"), "a.gguf");
        File.WriteAllText(file, "");
        var model = new InstalledModel { Name = "A", Path = file };
        s.Remember(model);
        s.ModelPath = file;
        s.ModelName = "A";
        ModelCatalog.Delete(model);
        Assert.False(File.Exists(file));
        Assert.Null(s.ModelPath);
        Assert.Null(s.ModelName);
        Assert.Empty(s.Installed);

        var other = Path.Combine(Sandbox.NewFolder("borrar"), "b.gguf");
        File.WriteAllText(other, "");
        s.ModelPath = "C:\\otro.gguf";
        ModelCatalog.Delete(new InstalledModel { Path = other });
        Assert.Equal("C:\\otro.gguf", s.ModelPath);
        Sandbox.ResetSettings();
    }
}

public class ModelLibraryTests
{
    [Fact]
    public async Task Move_takes_the_ggufs_and_points_the_settings_to_them()
    {
        var s = Sandbox.ResetSettings();
        var from = Sandbox.NewFolder("desde");
        s.ModelsFolder = from;
        var a = Path.Combine(from, "a.gguf");
        File.WriteAllText(a, "modelo a");
        File.WriteAllText(Path.Combine(from, "nota.txt"), "se queda");
        s.ModelPath = a;
        s.Remember(new InstalledModel { Name = "A", Path = a });
        var to = Path.Combine(Sandbox.Root, "hasta-" + Guid.NewGuid().ToString("N")[..6]);
        var reports = new List<ModelLibrary.MoveProgress>();
        await ModelLibrary.MoveAsync(to, new SyncProgress<ModelLibrary.MoveProgress>(reports.Add), CancellationToken.None);
        var moved = Path.Combine(to, "a.gguf");
        Assert.Equal("modelo a", File.ReadAllText(moved));
        Assert.False(File.Exists(a));
        Assert.True(File.Exists(Path.Combine(from, "nota.txt")));
        Assert.Equal(moved, s.ModelPath);
        Assert.Equal(moved, s.Installed.Single().Path);
        Assert.Equal(to, s.ModelsFolder);
        Assert.Equal(new ModelLibrary.MoveProgress("a.gguf", 8, 8), reports[^1]);

        // A la carpeta por defecto: el ajuste vuelve a null.
        await ModelLibrary.MoveAsync(Paths.DefaultModels, null, CancellationToken.None);
        Assert.Null(s.ModelsFolder);
        Assert.True(File.Exists(Path.Combine(Paths.DefaultModels, "a.gguf")));
        Sandbox.ResetSettings();
    }

    [Fact]
    public async Task Move_to_the_same_folder_does_nothing_and_inside_itself_is_refused()
    {
        var s = Sandbox.ResetSettings();
        var from = Sandbox.NewFolder("mismo");
        s.ModelsFolder = from;
        await ModelLibrary.MoveAsync(from + "\\", null, CancellationToken.None);
        Assert.Equal(from, s.ModelsFolder);
        await Assert.ThrowsAsync<InvalidOperationException>(() => ModelLibrary.MoveAsync(Path.Combine(from, "dentro"), null, CancellationToken.None));
        Sandbox.ResetSettings();
    }

    [Fact]
    public async Task Move_honours_cancellation()
    {
        var s = Sandbox.ResetSettings();
        var from = Sandbox.NewFolder("cancelar");
        s.ModelsFolder = from;
        File.WriteAllText(Path.Combine(from, "a.gguf"), "x");
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => ModelLibrary.MoveAsync(Sandbox.NewFolder("destino"), null, cts.Token));
        Assert.True(File.Exists(Path.Combine(from, "a.gguf")));
        Sandbox.ResetSettings();
    }
}

internal sealed class SyncProgress<T>(Action<T> report) : IProgress<T>
{
    public void Report(T value) => report(value);
}

public class EnginePinTests
{
    [Fact]
    public void Archives_are_pinned_with_sha256_and_point_to_the_release()
    {
        foreach (var a in new[] { EnginePin.WinVulkanX64, EnginePin.WinCudaX64, EnginePin.WinCudaX64.Companion!, EnginePin.WinCpuArm64 })
        {
            Assert.Matches("^[0-9a-f]{64}$", a.Sha256);
            Assert.StartsWith("https://github.com/ggml-org/llama.cpp/releases/download/" + EnginePin.Build + "/", a.Url);
            Assert.EndsWith("/" + a.FileName, a.Url);
        }
        Assert.Equal(0, EnginePin.GpuLayers(EnginePin.WinCpuArm64));
        Assert.Equal(99, EnginePin.GpuLayers(EnginePin.WinVulkanX64));
    }

    [Fact]
    public void For_this_machine_picks_cuda_only_with_an_nvidia_driver()
    {
        Assert.Equal(EnginePin.WinVulkanX64, EnginePin.ForThisMachine(allowCuda: false));
        Assert.Equal(EnginePin.HasNvidiaDriver() ? EnginePin.WinCudaX64 : EnginePin.WinVulkanX64, EnginePin.ForThisMachine());
    }

    [Fact]
    public void Pc_profile_reads_the_registry()
    {
        var pc = PcProfile.Detect("cpu");
        Assert.False(string.IsNullOrWhiteSpace(pc.Cpu));
        Assert.Equal(Environment.ProcessorCount, pc.Cores);
        Assert.Null(pc.Gpu);
        Assert.Equal(0, pc.VramBytes);
        var withGpu = PcProfile.Detect("vulkan");
        Assert.True(withGpu.VramBytes >= 0);
    }

    [Fact]
    public void Image_engine_paths_live_in_the_sandbox_and_uninstall_cleans_them()
    {
        Sandbox.ResetSettings();
        Assert.StartsWith(Sandbox.Root, ImageEngine.ModelPath);
        Assert.StartsWith(Sandbox.Root, ImageEngine.ImagesFolder);
        foreach (var a in new[] { ImageEngine.WinVulkanX64, ImageEngine.WinCpuX64 })
            Assert.Matches("^[0-9a-f]{64}$", a.Sha256);
        Directory.CreateDirectory(Paths.Models);
        File.WriteAllBytes(ImageEngine.ModelPath, new byte[10]);
        var sd = Path.Combine(Paths.Engine, "sd-prueba");
        Directory.CreateDirectory(sd);
        File.WriteAllBytes(Path.Combine(sd, "x.dll"), new byte[5]);
        Assert.True(ImageEngine.ModelInstalled);
        Assert.Equal(15, ImageEngine.InstalledBytes());
        Assert.False(ImageEngine.EngineInstalled);
        ImageEngine.Uninstall();
        Assert.False(ImageEngine.ModelInstalled);
        Assert.False(Directory.Exists(sd));
        Assert.Equal(0, ImageEngine.InstalledBytes());
    }

    [Fact]
    public async Task Engine_host_without_a_model_refuses_to_start()
    {
        Sandbox.ResetSettings();
        using var host = new EngineHost();
        var changed = 0;
        host.StateChanged += () => changed++;
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => host.EnsureReadyAsync(null, CancellationToken.None));
        Assert.Equal(SocLucia.Localization.Loc.Get("NoModelYet"), ex.Message);
        Assert.Equal(EngineState.NoModel, host.State);
        Assert.Equal(1, changed);
        Assert.Equal("-", host.Accelerator);
        Assert.False(EngineHost.VisionAvailable(AppSettings.Current));
        host.Stop();
    }

    [Fact]
    public void Engine_log_appends_lines()
    {
        EngineHost.Log("linea de prueba 123");
        Assert.Contains("linea de prueba 123", File.ReadAllText(Paths.EngineLog));
    }
}
