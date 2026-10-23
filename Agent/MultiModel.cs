using System.Text;
using SocLucia.Engine;

namespace SocLucia.Agent;

/// <summary>Cómo cooperan varios modelos para llegar al objetivo.</summary>
public enum MultiModelStrategy
{
    /// <summary>El primero redacta; cada siguiente mejora/critica lo anterior.</summary>
    Pipeline,
    /// <summary>Uno propone, otro revisa, el primero corrige con la crítica.</summary>
    Debate,
    /// <summary>Todos responden y un juez elige/funde la mejor.</summary>
    BestOf,
    /// <summary>Se prueba uno tras otro; el siguiente solo si el anterior falla o está al límite.</summary>
    Fallback,
}

/// <summary>Un modelo del conjunto multi-modelo: local (el motor de este PC) o en línea (proveedor + id).</summary>
public sealed class MultiModelMember
{
    public bool Remote { get; set; }
    public string ProviderId { get; set; } = string.Empty;
    public string ModelId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;

    public string Display => Name.Length > 0 ? Name : Remote ? $"{ProviderId}:{ModelId}" : "local";
}

/// <summary>Un paso del proceso multi-modelo (para enseñarlo o registrarlo).</summary>
public sealed record MultiModelStep(string Member, string Role, string Text);

/// <summary>El resultado: la respuesta final y los pasos que llevaron a ella.</summary>
public sealed record MultiModelResult(string Text, IReadOnlyList<MultiModelStep> Steps);

/// <summary>
/// Combina varios modelos (locales y/o en línea) para una tarea. Es lógica pura sobre un
/// <see cref="Asker"/> (preguntar a un modelo y leer su respuesta entera), así que se prueba con
/// modelos de mentira sin red. Cada paso puede ir a cualquier <see cref="Endpoint"/> local o remoto:
/// quien construye el <see cref="Asker"/> (la ventana) resuelve el destino de cada miembro.
/// </summary>
public static class MultiModel
{
    /// <summary>Pregunta a un miembro con unos mensajes y devuelve su respuesta entera. Lanza si el modelo falla.</summary>
    public delegate Task<string> Asker(MultiModelMember member, IReadOnlyList<ChatMessage> messages, CancellationToken cancel);

    public static Task<MultiModelResult> RunAsync(IReadOnlyList<MultiModelMember> members, MultiModelStrategy strategy, IReadOnlyList<ChatMessage> messages, Asker ask, CancellationToken cancel)
        => strategy switch
        {
            MultiModelStrategy.Pipeline => PipelineAsync(members, messages, ask, cancel),
            MultiModelStrategy.Debate => DebateAsync(members, messages, ask, cancel),
            MultiModelStrategy.BestOf => BestOfAsync(members, messages, ask, cancel),
            _ => FallbackAsync(members, messages, ask, cancel),
        };

    private static async Task<MultiModelResult> PipelineAsync(IReadOnlyList<MultiModelMember> members, IReadOnlyList<ChatMessage> messages, Asker ask, CancellationToken cancel)
    {
        var steps = new List<MultiModelStep>();
        var current = await ask(members[0], messages, cancel).ConfigureAwait(false);
        steps.Add(new MultiModelStep(members[0].Display, "draft", current));
        for (var i = 1; i < members.Count; i++)
        {
            cancel.ThrowIfCancellationRequested();
            var refined = await ask(members[i], With(messages,
                $"Here is a draft answer to the user's request:\n\n{current}\n\nImprove it: fix any errors, fill gaps, and make it clear and correct. Reply with only the improved answer, in the user's language."), cancel).ConfigureAwait(false);
            if (refined.Trim().Length > 0) current = refined;
            steps.Add(new MultiModelStep(members[i].Display, "refine", current));
        }
        return new MultiModelResult(current, steps);
    }

    private static async Task<MultiModelResult> DebateAsync(IReadOnlyList<MultiModelMember> members, IReadOnlyList<ChatMessage> messages, Asker ask, CancellationToken cancel)
    {
        if (members.Count < 2)
            return await PipelineAsync(members, messages, ask, cancel).ConfigureAwait(false);
        var steps = new List<MultiModelStep>();
        var proposal = await ask(members[0], messages, cancel).ConfigureAwait(false);
        steps.Add(new MultiModelStep(members[0].Display, "propose", proposal));
        var critique = await ask(members[1], With(messages,
            $"Another assistant answered the user's request with:\n\n{proposal}\n\nCritique that answer: point out errors, missing parts or risks, specifically and briefly."), cancel).ConfigureAwait(false);
        steps.Add(new MultiModelStep(members[1].Display, "critique", critique));
        var final = await ask(members[0], With(messages,
            $"Your earlier answer:\n\n{proposal}\n\nA reviewer said:\n\n{critique}\n\nGive the final corrected answer addressing the critique. Reply with only the answer, in the user's language."), cancel).ConfigureAwait(false);
        steps.Add(new MultiModelStep(members[0].Display, "revise", final));
        return new MultiModelResult(final.Trim().Length > 0 ? final : proposal, steps);
    }

    private static async Task<MultiModelResult> BestOfAsync(IReadOnlyList<MultiModelMember> members, IReadOnlyList<ChatMessage> messages, Asker ask, CancellationToken cancel)
    {
        var tasks = members.Select(async m =>
        {
            try { return (m, text: await ask(m, messages, cancel).ConfigureAwait(false)); }
            catch (Exception) when (!cancel.IsCancellationRequested) { return (m, text: string.Empty); }
        }).ToList();
        var candidates = (await Task.WhenAll(tasks).ConfigureAwait(false)).Where(c => c.text.Trim().Length > 0).ToList();
        var steps = candidates.Select(c => new MultiModelStep(c.m.Display, "candidate", c.text)).ToList();
        if (candidates.Count == 0) throw new InvalidOperationException("No model produced an answer.");
        if (candidates.Count == 1) return new MultiModelResult(candidates[0].text, steps);

        var numbered = string.Join("\n\n", candidates.Select((c, i) => $"[{i + 1}]\n{c.text}"));
        var final = await ask(members[0], With(messages,
            $"Here are {candidates.Count} candidate answers to the user's request:\n\n{numbered}\n\nChoose the best one and return it as the final answer, merging in any clearly better details from the others. Reply with only the final answer, in the user's language."), cancel).ConfigureAwait(false);
        var text = final.Trim().Length > 0 ? final : candidates[0].text;
        steps.Add(new MultiModelStep(members[0].Display, "judge", text));
        return new MultiModelResult(text, steps);
    }

    private static async Task<MultiModelResult> FallbackAsync(IReadOnlyList<MultiModelMember> members, IReadOnlyList<ChatMessage> messages, Asker ask, CancellationToken cancel)
    {
        var steps = new List<MultiModelStep>();
        Exception? last = null;
        foreach (var member in members)
        {
            cancel.ThrowIfCancellationRequested();
            try
            {
                var answer = await ask(member, messages, cancel).ConfigureAwait(false);
                if (answer.Trim().Length > 0)
                {
                    steps.Add(new MultiModelStep(member.Display, "answer", answer));
                    return new MultiModelResult(answer, steps);
                }
                steps.Add(new MultiModelStep(member.Display, "empty", string.Empty));
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                last = ex;
                steps.Add(new MultiModelStep(member.Display, "failed", ex.Message));
            }
        }
        throw last ?? new InvalidOperationException("No model produced an answer.");
    }

    private static IReadOnlyList<ChatMessage> With(IReadOnlyList<ChatMessage> baseMessages, string instruction)
        => [.. baseMessages, new ChatMessage("user", instruction)];
}
