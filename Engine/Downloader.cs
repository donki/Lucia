using System.IO;
using System.Net.Http;
using System.Security.Cryptography;

namespace SocAiChat.Engine;

/// <summary>Descargas grandes (motor y modelos) con progreso, cancelacion y comprobacion opcional del SHA-256.</summary>
public static class Downloader
{
    public static readonly HttpClient Http = new(new HttpClientHandler { AllowAutoRedirect = true })
    {
        Timeout = Timeout.InfiniteTimeSpan,
        DefaultRequestHeaders = { { "User-Agent", "sOCAIChat/1.0 (+https://github.com/donki/AIChat)" } },
    };

    public sealed record Progress(long Received, long? Total)
    {
        public double? Fraction => Total is > 0 ? (double)Received / Total : null;
    }

    /// <summary>Baja a un fichero temporal al lado y lo renombra al final: nunca queda un fichero a medias con el nombre bueno.</summary>
    public static async Task DownloadAsync(string url, string destination, string? sha256, IProgress<Progress>? progress, CancellationToken cancel)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        var temp = destination + ".part";
        using (var response = await Http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancel))
        {
            response.EnsureSuccessStatusCode();
            var total = response.Content.Headers.ContentLength;
            await using var source = await response.Content.ReadAsStreamAsync(cancel);
            await using var target = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None, 1 << 20, useAsync: true);
            var buffer = new byte[1 << 20];
            long received = 0;
            var lastReport = DateTime.UtcNow;
            int read;
            while ((read = await source.ReadAsync(buffer, cancel)) > 0)
            {
                await target.WriteAsync(buffer.AsMemory(0, read), cancel);
                received += read;
                if ((DateTime.UtcNow - lastReport).TotalMilliseconds > 150)
                {
                    progress?.Report(new Progress(received, total));
                    lastReport = DateTime.UtcNow;
                }
            }
            progress?.Report(new Progress(received, total ?? received));
        }
        if (sha256 is not null)
        {
            var actual = await HashAsync(temp, cancel);
            if (!actual.Equals(sha256, StringComparison.OrdinalIgnoreCase))
            {
                File.Delete(temp);
                throw new InvalidDataException($"SHA-256 no coincide: esperado {sha256}, obtenido {actual}");
            }
        }
        File.Move(temp, destination, overwrite: true);
    }

    public static async Task<string> HashAsync(string path, CancellationToken cancel)
    {
        await using var stream = File.OpenRead(path);
        var hash = await SHA256.HashDataAsync(stream, cancel);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}
