using SocLucia.Chat;

namespace SocLucia.Tests;

public class ContextCompactorTests
{
    private static StoredMessage M(string role, string content) => new() { Role = role, Content = content };

    private static List<StoredMessage> Conversation(int turns, int chars)
    {
        var list = new List<StoredMessage>();
        for (var i = 0; i < turns; i++)
        {
            list.Add(M("user", $"pregunta {i} " + new string('a', chars)));
            list.Add(M("assistant", $"respuesta {i} " + new string('b', chars)));
        }
        return list;
    }

    [Fact]
    public void Tokens_estimate_is_about_chars_over_four()
    {
        Assert.Equal(0, ContextCompactor.Tokens((string?)null));
        Assert.Equal(25, ContextCompactor.Tokens(new string('x', 100)));
    }

    [Fact]
    public void No_compacta_si_cabe()
    {
        var msgs = Conversation(3, 40);
        Assert.False(ContextCompactor.PlanFor(msgs, null, 0, 100_000).Compact);
    }

    [Fact]
    public void Compacta_cortando_antes_de_una_pregunta_y_dejando_cola()
    {
        var msgs = Conversation(10, 400);   // 20 mensajes grandes
        var plan = ContextCompactor.PlanFor(msgs, null, 0, 800);
        Assert.True(plan.Compact);
        // El corte cae en un mensaje de usuario y deja al menos KeepTail al final
        Assert.Equal("user", msgs[plan.SummarizeUpTo].Role);
        Assert.True(msgs.Count - plan.SummarizeUpTo >= ContextCompactor.KeepTail);
        // Lo que queda tras resumir cabe mejor que todo
        Assert.True(ContextCompactor.Tokens(msgs.Skip(plan.SummarizeUpTo)) < ContextCompactor.Tokens(msgs));
    }

    [Fact]
    public void No_rompe_la_cola_si_no_hay_donde_cortar()
    {
        // Sólo una pregunta al principio: no se puede cortar antes de otra sin quedarse sin cola
        var msgs = new List<StoredMessage> { M("user", new string('a', 4000)), M("assistant", new string('b', 4000)) };
        Assert.False(ContextCompactor.PlanFor(msgs, null, 0, 100).Compact);
    }

    [Fact]
    public void El_presupuesto_descuenta_respuesta_sistema_y_margen()
    {
        Assert.Equal(8192 - 2048 - 100 - 512, ContextCompactor.Budget(8192, 2048, thinking: false, systemTokens: 100));
        Assert.Equal(8192 - 2048 * 3 - 100 - 512, ContextCompactor.Budget(8192, 2048, thinking: true, systemTokens: 100));
        Assert.Equal(512, ContextCompactor.Budget(1000, 2048, thinking: true, systemTokens: 100));   // nunca menos de 512
    }

    [Fact]
    public async Task Compact_async_resume_y_marca_lo_cubierto()
    {
        var thread = new ChatThread { Messages = Conversation(8, 400) };
        var original = ContextCompactor.Summarizer;
        string? seenExisting = null;
        int folded = 0;
        ContextCompactor.Summarizer = (_, existing, msgs, _) => { seenExisting = existing; folded = msgs.Count; return Task.FromResult("RESUMEN"); };
        try
        {
            Assert.True(await ContextCompactor.CompactAsync(thread, "http://x", 800, CancellationToken.None));
            Assert.Equal("RESUMEN", thread.Summary);
            Assert.True(thread.SummarizedCount > 0);
            Assert.Equal(thread.SummarizedCount, folded);
            Assert.Null(seenExisting);   // primera vez, sin resumen previo
            Assert.Contains("RESUMEN", ContextCompactor.SummaryBlock(thread));
            // Segunda compresión: parte del resumen anterior
            thread.Messages.AddRange(Conversation(8, 400));
            ContextCompactor.Summarizer = (_, existing, _, _) => { seenExisting = existing; return Task.FromResult("RESUMEN2"); };
            await ContextCompactor.CompactAsync(thread, "http://x", 800, CancellationToken.None);
            Assert.Equal("RESUMEN", seenExisting);   // se le pasa el resumen anterior para fundirlo
            Assert.Equal("RESUMEN2", thread.Summary);
        }
        finally { ContextCompactor.Summarizer = original; }
    }

    [Fact]
    public async Task Un_resumen_vacio_no_cambia_nada()
    {
        var thread = new ChatThread { Messages = Conversation(8, 400) };
        var original = ContextCompactor.Summarizer;
        ContextCompactor.Summarizer = (_, _, _, _) => Task.FromResult("   ");
        try
        {
            Assert.False(await ContextCompactor.CompactAsync(thread, "http://x", 800, CancellationToken.None));
            Assert.Null(thread.Summary);
            Assert.Equal(0, thread.SummarizedCount);
        }
        finally { ContextCompactor.Summarizer = original; }
    }
}
