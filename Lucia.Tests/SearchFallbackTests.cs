using System.IO.Compression;
using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using System.Windows.Controls.Primitives;
using SocLucia.Agent;
using SocLucia.Chat;
using SocLucia.Engine;
using SocLucia.Localization;
using SocLucia.Services;

namespace SocLucia.Tests;

/// <summary>
/// El buscador bloqueado y la IA buscando en bucle (2026-10-22): DuckDuckGo devolvia su pagina anti-robots
/// a todo, la IA reformulaba la misma busqueda sin fin. Ahora se prueba en Stack Overflow y Wikipedia, y
/// el freno pide otro enfoque a la segunda busqueda inutil y deja de buscar a la cuarta.
/// </summary>
public class SearchFallbackTests
{
    private static ToolCall Search(string query) => new() { Id = Guid.NewGuid().ToString("N"), Name = ToolBox.WebSearch, Arguments = new JsonObject { ["query"] = query }.ToJsonString() };

    private const string Blocked = "<html><body><form id=\"challenge-form\">anomaly detected, please solve the captcha</form></body></html>";

    private static HttpResponseMessage Text(string text) => new(HttpStatusCode.OK) { Content = new StringContent(text) };

    private static HttpResponseMessage Gzip(string json)
    {
        var ms = new MemoryStream();
        using (var gz = new GZipStream(ms, CompressionLevel.Fastest, leaveOpen: true)) gz.Write(Encoding.UTF8.GetBytes(json));
        return new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(ms.ToArray()) };
    }

    private static async Task<string> RunSearch(string query, Func<string, HttpResponseMessage?> answer)
    {
        var http = Downloader.Http;
        Downloader.Http = new HttpClient(new FakeHttp { Other = r => answer(r.RequestUri!.ToString()) });
        try { return await ToolBox.Find(ToolBox.WebSearch)!.Run(new JsonObject { ["query"] = query }, CancellationToken.None); }
        finally { Downloader.Http = http; }
    }

    [Fact]
    public async Task Con_DuckDuckGo_bloqueado_busca_en_Stack_Overflow_y_Wikipedia()
    {
        var text = await RunSearch("Crypto++ HexDecoder", url =>
            url.StartsWith("https://html.duckduckgo.com/", StringComparison.Ordinal) ? Text(Blocked)
            : url.StartsWith("https://api.stackexchange.com/", StringComparison.Ordinal) ? Gzip("""{"items":[{"title":"Crypto&#43;&#43; HexDecoder &amp; AES","link":"https://stackoverflow.com/q/1","is_answered":true,"tags":["c++","crypto++"]},{"title":"","link":"x"}]}""")
            : url.Contains("wikipedia.org/w/api.php", StringComparison.Ordinal) ? Text("""{"query":{"search":[{"title":"Crypto++","snippet":"biblioteca <span class=\"searchmatch\">C++</span>  de cifrado"}]}}""")
            : null);
        Assert.StartsWith("(The web search engine blocked", text);
        Assert.Contains("- Stack Overflow (answered): Crypto++ HexDecoder & AES\n   https://stackoverflow.com/q/1\n   tags: c++, crypto++", text);
        Assert.Contains("https://", text);
        Assert.Contains("Wikipedia", text);
        Assert.Contains("biblioteca C++ de cifrado", text);
    }

    [Fact]
    public async Task Si_nada_da_resultados_lo_dice_y_pide_otro_enfoque()
    {
        var text = await RunSearch("algo rarísimo", url =>
            url.StartsWith("https://html.duckduckgo.com/", StringComparison.Ordinal) ? Text(Blocked)
            : url.StartsWith("https://api.stackexchange.com/", StringComparison.Ordinal) ? new HttpResponseMessage(HttpStatusCode.BadRequest)
            : url.Contains("wikipedia", StringComparison.Ordinal) ? Text("""{"query":{"search":[]}}""")
            : null);
        Assert.StartsWith(ToolBox.NoResults, text);
        Assert.Contains("blocking", text);
        Assert.Contains("Do not repeat the search", text);
        Assert.True(ToolLoopGuard.IsFruitless(text));
    }

    [Fact]
    public async Task Con_resultados_de_DuckDuckGo_no_se_va_a_otros_sitios()
    {
        var others = 0;
        var text = await RunSearch("paella", url =>
        {
            if (url.StartsWith("https://html.duckduckgo.com/", StringComparison.Ordinal))
                return Text("<a class=\"result__a\" href=\"https://ejemplo.com/paella\">Paella</a><a class=\"result__snippet\" href=\"x\">arroz</a>");
            others++;
            return null;
        });
        Assert.StartsWith("1. Paella", text);
        Assert.Equal(0, others);
        // Sin bloqueo y sin resultados, tampoco dice que este bloqueado
        var none = await RunSearch("zzz", url => url.StartsWith("https://html.duckduckgo.com/", StringComparison.Ordinal) ? Text("<html>nada</html>") : Text("{}"));
        Assert.DoesNotContain("blocking", none);
        Assert.True(ToolBox.IsSearchBlocked(Blocked));
        Assert.False(ToolBox.IsSearchBlocked("<html>nada</html>"));
        Assert.Equal(string.Empty, ToolBox.StackOverflowText("""{"error_id":400}"""));
        Assert.Equal(string.Empty, ToolBox.WikipediaText("""{"batchcomplete":""}""", "es"));
    }

    [Fact]
    public void El_freno_pide_otro_enfoque_a_la_segunda_y_para_a_la_cuarta()
    {
        var guard = new ToolLoopGuard(ToolLoopGuard.DefaultMaxCalls);
        var first = Search("cifrado cryptopp dll hexadecimal");
        Assert.Equal(GuardVerdict.Run, guard.Before(first));
        Assert.False(guard.After(first, "web_search", ToolBox.NoResults));
        Assert.Null(guard.TakeNudge());
        // La misma exacta ya no se ejecuta
        Assert.Equal(GuardVerdict.Repeat, guard.Before(Search("Cifrado  CRYPTOPP dll hexadecimal")));
        var second = Search("transformación hexadecimal Crypto++");
        Assert.Equal(GuardVerdict.Run, guard.Before(second));
        Assert.False(guard.After(second, "web_search", ToolBox.NoResults + " blocked"));
        Assert.StartsWith("[Lucia] This is the second action", guard.TakeNudge());
        Assert.Null(guard.TakeNudge());   // una sola vez
        // Casi la misma busqueda aunque traiga resultados tambien cuenta como no avanzar
        var third = Search("cryptopp dll cifrado hexadecimal ejemplo");
        guard.Before(third);
        Assert.False(guard.After(third, "web_search", "1. Algo\n   https://x\n   y"));
        Assert.Equal(3, guard.Fruitless);
        var fourth = Search("carpeta vacía");
        guard.Before(fourth);
        Assert.True(guard.After(fourth, "list_folder", "[empty folder]"));
        Assert.Equal(GuardStop.NoProgress, guard.Stopped);
        Assert.Equal(GuardVerdict.Stop, guard.Before(Search("otra")));
    }

    [Fact]
    public void Un_resultado_util_reinicia_la_cuenta()
    {
        var guard = new ToolLoopGuard(ToolLoopGuard.DefaultMaxCalls);
        var a = Search("uno dos tres");
        guard.Before(a);
        guard.After(a, "web_search", ToolBox.NoResults);
        var b = Search("cuatro cinco seis");
        guard.Before(b);
        guard.After(b, "web_search", "1. Resultado\n   https://x\n   y");
        Assert.Equal(0, guard.Fruitless);
        Assert.True(ToolLoopGuard.IsFruitless("   "));
        Assert.True(ToolLoopGuard.IsFruitless("[no matches]"));
        Assert.False(ToolLoopGuard.IsFruitless("Written 3 bytes"));
        Assert.Equal(["crypto++", "dll", "hexadecimal"], ToolLoopGuard.Words("Crypto++ DLL, hexadecimal de").Order().ToArray());
    }
}
