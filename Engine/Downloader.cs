using System.IO;
using System.Net.Http;
using System.Security.Cryptography;

namespace SocLucia.Engine;

/// <summary>Descargas grandes (motor y modelos) con progreso, cancelacion y comprobacion opcional del SHA-256.</summary>
public static class Downloader
{
    public static readonly HttpClient Http = new(new HttpClientHandler { AllowAutoRedirect = true })
    {
        Timeout = Timeout.InfiniteTimeSpan,
        DefaultRequestHeaders = { { "User-Agent", "sOCLucia/1.0 (+https://github.com/donki/Lucia)" } },
    };

    public sealed record Progress(long Received, long? Total)
    {
        public double? Fraction => Total is > 0 ? (double)Received / Total : null;
    }

    /// <summary>
    /// Baja a un fichero temporal al lado y lo renombra al final: nunca queda un fichero a medias con
    /// el nombre bueno. Si ya hay un .part (la aplicacion se cerro a medias), sigue desde donde iba
    /// con una peticion Range; si el servidor no lo admite, empieza de cero.
    /// </summary>
    public static async Task DownloadAsync(string url, string destination, string? sha256, IProgress<Progress>? progress, CancellationToken cancel)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        var temp = destination + ".part";
        var already = File.Exists(temp) ? new FileInfo(temp).Length : 0;
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        if (already > 0)
            request.Headers.Range = new System.Net.Http.Headers.RangeHeaderValue(already, null);
        using (var response = await Http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancel))
        {
            if (already > 0 && response.StatusCode == System.Net.HttpStatusCode.RequestedRangeNotSatisfiable)
            {
                // El .part ya esta completo o el fichero cambio: se mira el tamaño real y se decide.
                using var head = await Http.SendAsync(new HttpRequestMessage(HttpMethod.Head, url), cancel);
                if (!(head.IsSuccessStatusCode && head.Content.Headers.ContentLength == already))
                {
                    File.Delete(temp);
                    await DownloadAsync(url, destination, sha256, progress, cancel);
                    return;
                }
                progress?.Report(new Progress(already, already));
            }
            else
            {
                response.EnsureSuccessStatusCode();
                var resumed = already > 0 && response.StatusCode == System.Net.HttpStatusCode.PartialContent;
                if (already > 0) EngineHost.Log(resumed ? $"descarga retomada en el byte {already:N0}: {Path.GetFileName(destination)}" : $"el servidor no admite retomar; se baja de cero: {Path.GetFileName(destination)}");
                if (!resumed) already = 0;
                var total = response.Content.Headers.ContentLength is { } len ? len + already : (long?)null;
                await using var source = await response.Content.ReadAsStreamAsync(cancel);
                await using var target = new FileStream(temp, resumed ? FileMode.Append : FileMode.Create, FileAccess.Write, FileShare.None, 1 << 20, useAsync: true);
                var buffer = new byte[1 << 20];
                long received = already;
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
