if (Environment.GetEnvironmentVariable("FAKE_LLAMA_LOG") is { Length: > 0 } log)
{
    try { File.AppendAllText(log, string.Join('|', args) + "\n"); } catch (IOException) { }
}
if (int.TryParse(Environment.GetEnvironmentVariable("FAKE_LLAMA_EXIT"), out var code))
    return code;
Thread.Sleep(Timeout.Infinite);
return 0;
