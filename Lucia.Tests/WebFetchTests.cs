using System.IO.Compression;
using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using SocLucia.Agent;
using SocLucia.Engine;
using SocLucia.Services;

namespace SocLucia.Tests;

/// <summary>Leer páginas web para investigar: cabeceras de navegador, gzip, título, y el repliegue a navegador headless.</summary>
public class WebFetchTests
{
    private static Task<string> Fetch(string url) => ToolBox.Find(ToolBox.FetchUrl)!.Run(new JsonObject { ["url"] = url }, CancellationToken.None);

    [Fact]
    public void Html_to_text_keeps_the_title_and_drops_tags_and_scripts()
    {
        var text = ToolBox.HtmlToText("<html><head><title>Mi &amp; página</title><style>x{}</style></head><body><h1>Hola</h1><script>bad()</script><p>uno</p><p>dos</p></body></html>");
        Assert.StartsWith("Mi & página", text);
        Assert.Contains("Hola", text);
        Assert.Contains("uno", text);
        Assert.DoesNotContain("bad()", text);
        Assert.DoesNotContain("<", text);
    }

    [Fact]
    public async Task Fetch_sends_a_browser_user_agent_and_decompresses_gzip()
    {
        string? ua = null;
        using var server = new LocalServer(async ctx =>
        {
            ua = ctx.Request.UserAgent;
            var bytes = Encoding.UTF8.GetBytes("<html><head><title>T</title></head><body><p>contenido real</p></body></html>");
            var ms = new MemoryStream();
            using (var gz = new GZipStream(ms, CompressionLevel.Fastest, leaveOpen: true)) gz.Write(bytes);
            var gzipped = ms.ToArray();
            ctx.Response.StatusCode = 200;
            ctx.Response.ContentType = "text/html";
            ctx.Response.Headers["Content-Encoding"] = "gzip";
            ctx.Response.ContentLength64 = gzipped.Length;
            await ctx.Response.OutputStream.WriteAsync(gzipped);
            ctx.Response.Close();
        });
        var http = Downloader.Http;
        Downloader.Http = new HttpClient(new HttpClientHandler());
        try
        {
            var text = await Fetch(server.BaseUrl + "/p");
            Assert.Contains("contenido real", text);   // descomprimido
            Assert.Contains("T", text);
            Assert.NotNull(ua);
            Assert.Contains("Mozilla", ua!);   // user-agent de navegador
        }
        finally { Downloader.Http = http; }
    }

    [Fact]
    public async Task Si_la_web_bloquea_usa_el_navegador_headless()
    {
        using var server = new LocalServer(ctx => LocalServer.Write(ctx, 403, "text/html", "<html><body>Access denied</body></html>"));
        var http = Downloader.Http;
        Downloader.Http = new HttpClient(new HttpClientHandler());
        var original = ToolBox.HeadlessRenderer;
        var asked = new List<string>();
        ToolBox.HeadlessRenderer = (url, _) => { asked.Add(url); return Task.FromResult<string?>("<html><head><title>Real</title></head><body><p>" + new string('x', 400) + "</p></body></html>"); };
        try
        {
            var text = await Fetch(server.BaseUrl + "/blocked");
            Assert.StartsWith("[rendered with a headless browser]", text);
            Assert.Contains("Real", text);
            Assert.Single(asked);
        }
        finally { Downloader.Http = http; ToolBox.HeadlessRenderer = original; }
    }

    [Fact]
    public async Task Una_pagina_de_javascript_casi_vacia_se_rellena_con_el_headless()
    {
        using var server = new LocalServer(ctx => LocalServer.Write(ctx, 200, "text/html", "<html><body><div id=\"app\"></div></body></html>"));
        var http = Downloader.Http;
        Downloader.Http = new HttpClient(new HttpClientHandler());
        var original = ToolBox.HeadlessRenderer;
        ToolBox.HeadlessRenderer = (_, _) => Task.FromResult<string?>("<html><body><p>" + new string('y', 500) + "</p></body></html>");
        try
        {
            Assert.Contains("yyyy", await Fetch(server.BaseUrl + "/spa"));
        }
        finally { Downloader.Http = http; ToolBox.HeadlessRenderer = original; }
    }

    [Fact]
    public async Task Sin_navegador_headless_disponible_lo_dice_claro()
    {
        using var server = new LocalServer(ctx => LocalServer.Write(ctx, 403, "text/plain", "no"));
        var http = Downloader.Http;
        Downloader.Http = new HttpClient(new HttpClientHandler());
        var original = ToolBox.HeadlessRenderer;
        ToolBox.HeadlessRenderer = null;   // como si no hubiera navegador
        try
        {
            var text = await Fetch(server.BaseUrl + "/x");
            Assert.StartsWith("[403]", text);
            Assert.Contains("another source", text);
        }
        finally { Downloader.Http = http; ToolBox.HeadlessRenderer = original; }
    }

    [Fact]
    public void Headless_browser_args_have_the_key_flags()
    {
        var args = HeadlessBrowser.BuildArgs("https://example.com/a", @"C:\temp\profile");
        Assert.Contains("--headless=new", args);
        Assert.Contains("--dump-dom", args);
        Assert.Contains("--proxy-server=direct://", args);   // sin esto no carga con política de proxy
        Assert.Contains(@"--user-data-dir=C:\temp\profile", args);
        Assert.Equal("https://example.com/a", args[^1]);
    }
}
