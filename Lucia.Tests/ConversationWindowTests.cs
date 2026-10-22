using SocLucia.Chat;
using SocLucia.Engine;

namespace SocLucia.Tests;

public class ConversationWindowTests
{
    private static StoredMessage Sys() => new() { Role = "system", Content = "s" };
    private static StoredMessage User(string c = "haz X") => new() { Role = "user", Content = c };
    private static StoredMessage Assistant() => new() { Role = "assistant", Content = "", ToolCalls = "[{\"type\":\"function\"}]" };
    private static StoredMessage ToolResult() => new() { Role = "tool", Content = "[exit code 1] error", ToolCallId = "c" };

    [Fact]
    public void Conserva_la_ultima_pregunta_aunque_se_salga_de_la_ventana()
    {
        // Como en modo agente: una pregunta y luego muchas vueltas de herramienta (asistente + resultado).
        var all = new List<StoredMessage> { User("la pregunta original") };
        for (var i = 0; i < 20; i++) { all.Add(Assistant()); all.Add(ToolResult()); }
        var window = ConversationWindow.Select(all, keep: 30);
        Assert.Equal("user", window[0].Role);                 // la pregunta, reinsertada al principio
        Assert.Equal("la pregunta original", window[0].Content);
        Assert.Contains(window, m => m.Role == "user");
        Assert.True(window.Count <= 31);                        // la ventana + la pregunta recuperada
    }

    [Fact]
    public void Si_la_pregunta_ya_esta_en_la_ventana_no_la_duplica()
    {
        var all = new List<StoredMessage> { Sys(), User("p1"), Assistant(), ToolResult(), User("p2"), Assistant(), ToolResult() };
        var window = ConversationWindow.Select(all, keep: 30);
        Assert.Equal(all.Count, window.Count);
        Assert.Equal(2, window.Count(m => m.Role == "user"));
    }

    [Fact]
    public void Casos_limite()
    {
        Assert.Empty(ConversationWindow.Select([]));
        // Sin ninguna pregunta en toda la conversacion (no deberia pasar): no inventa nada.
        var noUser = new List<StoredMessage> { Sys(), Assistant(), ToolResult() };
        Assert.DoesNotContain(ConversationWindow.Select(noUser), m => m.Role == "user");
        // La ventana corta justo despues de la pregunta: se conserva.
        var all = new List<StoredMessage> { User("vieja") };
        for (var i = 0; i < 5; i++) { all.Add(Assistant()); all.Add(ToolResult()); }
        Assert.Equal("vieja", ConversationWindow.Select(all, keep: 4)[0].Content);
    }
}

public class ChatTemplateErrorTests
{
    [Theory]
    [InlineData("500: ------ Error: Jinja Exception: No user query found in messages.", true)]
    [InlineData("While executing the chat template: raise_exception", true)]
    [InlineData("System message must be at the beginning.", true)]
    [InlineData("apply_chat_template failed", true)]
    [InlineData("500: out of memory", false)]
    [InlineData("connection refused", false)]
    [InlineData("the model is still loading", false)]
    public void Distingue_un_error_de_plantilla(string message, bool template)
        => Assert.Equal(template, ChatClient.IsTemplateError(message));

    [Fact]
    public async Task Un_500_de_plantilla_llega_como_ChatTemplateException_no_como_HttpRequestException()
    {
        using var server = new LocalServer(ctx => LocalServer.Write(ctx, 500, "application/json",
            "{\"error\":{\"message\":\"While executing CallExpression at line 79: Jinja Exception: No user query found in messages.\",\"code\":500}}"));
        var live = new SocLucia.Chat.PendingAnswer();
        var ex = await Assert.ThrowsAsync<ChatTemplateException>(() => ChatClient.PumpAsync(server.BaseUrl, [new ChatMessage("system", "s")], false, 100, null, live, default));
        Assert.Contains("No user query found", ex.Message);
        // Un 500 normal sigue siendo un HttpRequestException.
        using var other = new LocalServer(ctx => LocalServer.Write(ctx, 500, "application/json", "{\"error\":{\"message\":\"out of memory\"}}"));
        await Assert.ThrowsAsync<HttpRequestException>(() => ChatClient.PumpAsync(other.BaseUrl, [new ChatMessage("user", "x")], false, 100, null, live, default));
    }
}
