if (Environment.GetEnvironmentVariable("FAKE_LLAMA_LOG") is { Length: > 0 } log)
{
    try { File.AppendAllText(log, string.Join('|', args) + "\n"); } catch (IOException) { }
}
// Como «python lucia_train.py config.json» (Enseñar a Lucia): cuenta etapas y pasos como el script y deja el GGUF.
if (Environment.GetEnvironmentVariable("FAKE_PYTHON") is { Length: > 0 } mode && args.Length == 2 && File.Exists(args[1]))
{
    var config = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(args[1]))!.AsObject();
    void Say(string json) => Console.WriteLine("@lucia " + json);
    Console.WriteLine("registro normal del entrenamiento");
    if (mode == "error") { Say("""{"event":"error","kind":"oom","message":"CUDA out of memory"}"""); return 1; }
    if (config["mode"]?.GetValue<string>() == "convert")
    {
        var outfile = config["outfile"]!.GetValue<string>();
        File.WriteAllBytes(outfile, [7, 7]);
        Say("""{"event":"stage","stage":"converting"}""");
        Say(System.Text.Json.JsonSerializer.Serialize(new Dictionary<string, string> { ["event"] = "done", ["outfile"] = outfile }));
        return 0;
    }
    Say("""{"event":"stage","stage":"loading"}""");
    Say("""{"event":"stage","stage":"training","examples":12,"total":3}""");
    for (var step = 1; step <= 3; step++)
    {
        if (mode == "slow") Thread.Sleep(400);
        Say($$"""{"event":"progress","step":{{step}},"total":3,"loss":{{4 - step}}.5,"epoch":1,"elapsed":{{step * 2}}}""");
        if (File.Exists(config["stop_file"]!.GetValue<string>())) { Say("""{"event":"stopped"}"""); return 0; }
    }
    var adapter = Path.Combine(config["output_dir"]!.GetValue<string>(), "adapter.gguf");
    File.WriteAllBytes(adapter, [1, 2, 3, 4]);
    Say(System.Text.Json.JsonSerializer.Serialize(new Dictionary<string, object> { ["event"] = "done", ["adapter"] = adapter, ["examples"] = 12 }));
    return 0;
}
if (int.TryParse(Environment.GetEnvironmentVariable("FAKE_LLAMA_EXIT"), out var code))
    return code;
Thread.Sleep(Timeout.Infinite);
return 0;
