using SocLucia.Agent;
using SocLucia.Engine;

namespace SocLucia.Tests;

/// <summary>El orquestador multi-modelo con modelos de mentira (un «asker» falso), sin red.</summary>
public class MultiModelTests
{
    private static MultiModelMember M(string name) => new() { Name = name };
    private static IReadOnlyList<ChatMessage> Msgs => [new ChatMessage("user", "¿capital de Francia?")];

    /// <summary>Asker falso: cada modelo responde según su nombre y lo que se le pide (para ver el flujo).</summary>
    private static MultiModel.Asker Scripted(Func<string, string, string> reply) =>
        (member, messages, _) => Task.FromResult(reply(member.Name, messages[^1].Content));

    [Fact]
    public async Task Pipeline_drafts_then_each_refines_the_previous()
    {
        var ask = Scripted((name, last) => name == "A" ? "borrador" : $"{name} mejoró: {Extract(last)}");
        var result = await MultiModel.RunAsync([M("A"), M("B"), M("C")], MultiModelStrategy.Pipeline, Msgs, ask, default);
        Assert.Equal("C mejoró: B mejoró: borrador", result.Text);
        Assert.Equal(["draft", "refine", "refine"], result.Steps.Select(s => s.Role));
    }

    [Fact]
    public async Task Debate_proposes_critiques_and_revises_with_the_first_model()
    {
        var ask = Scripted((name, last) =>
            name == "B" ? "falta la fuente"
            : last.Contains("reviewer said") ? "respuesta final" : "propuesta");
        var result = await MultiModel.RunAsync([M("A"), M("B")], MultiModelStrategy.Debate, Msgs, ask, default);
        Assert.Equal("respuesta final", result.Text);
        Assert.Equal(["propose", "critique", "revise"], result.Steps.Select(s => s.Role));
    }

    [Fact]
    public async Task BestOf_runs_all_and_a_judge_picks()
    {
        var ask = Scripted((name, last) => last.Contains("candidate answers") ? "elegida: la de B" : $"resp-{name}");
        var result = await MultiModel.RunAsync([M("A"), M("B"), M("C")], MultiModelStrategy.BestOf, Msgs, ask, default);
        Assert.Equal("elegida: la de B", result.Text);
        Assert.Equal(3, result.Steps.Count(s => s.Role == "candidate"));
        Assert.Contains(result.Steps, s => s.Role == "judge");
    }

    [Fact]
    public async Task BestOf_with_one_working_model_skips_the_judge()
    {
        MultiModel.Asker ask = (member, _, _) => member.Name == "A"
            ? Task.FromResult("la buena")
            : throw new InvalidOperationException("429");
        var result = await MultiModel.RunAsync([M("A"), M("B")], MultiModelStrategy.BestOf, Msgs, ask, default);
        Assert.Equal("la buena", result.Text);
    }

    [Fact]
    public async Task Fallback_uses_the_next_model_when_one_fails()
    {
        var calls = new List<string>();
        MultiModel.Asker ask = (member, _, _) =>
        {
            calls.Add(member.Name);
            return member.Name switch
            {
                "A" => throw new InvalidOperationException("429 rate limit"),
                "B" => Task.FromResult("   "),   // vacío: también pasa al siguiente
                _ => Task.FromResult("respuesta de C"),
            };
        };
        var result = await MultiModel.RunAsync([M("A"), M("B"), M("C")], MultiModelStrategy.Fallback, Msgs, ask, default);
        Assert.Equal("respuesta de C", result.Text);
        Assert.Equal(["A", "B", "C"], calls);
    }

    [Fact]
    public async Task Fallback_throws_if_all_fail()
    {
        MultiModel.Asker ask = (_, _, _) => throw new InvalidOperationException("sin red");
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            MultiModel.RunAsync([M("A"), M("B")], MultiModelStrategy.Fallback, Msgs, ask, default));
    }

    [Fact]
    public async Task Debate_with_one_model_degrades_to_a_single_answer()
    {
        var ask = Scripted((name, _) => "solo");
        var result = await MultiModel.RunAsync([M("A")], MultiModelStrategy.Debate, Msgs, ask, default);
        Assert.Equal("solo", result.Text);
    }

    // La última línea de los mensajes trae la instrucción con el texto anterior incrustado; se saca lo útil.
    private static string Extract(string instruction)
    {
        var start = instruction.IndexOf("draft answer to the user's request:\n\n", StringComparison.Ordinal);
        if (start < 0) return instruction;
        start += "draft answer to the user's request:\n\n".Length;
        var end = instruction.IndexOf("\n\nImprove", start, StringComparison.Ordinal);
        return end < 0 ? instruction[start..] : instruction[start..end];
    }
}
