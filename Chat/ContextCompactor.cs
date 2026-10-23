using System.Text;
using System.Text.Json.Nodes;
using SocLucia.Engine;

namespace SocLucia.Chat;

/// <summary>
/// Comprime una conversación larga para que siga cabiendo en el contexto: resume los mensajes antiguos
/// con el propio modelo y conserva tal cual los últimos. Así, en una charla o una tarea de agente con
/// muchas vueltas, Lucia no pierde el hilo ni choca con el límite de la IA, sólo recuerda lo viejo
/// resumido. El resumen se guarda en la conversación (<see cref="ChatThread.Summary"/>) y sólo se rehace
/// cuando vuelve a hacer falta.
/// </summary>
public static class ContextCompactor
{
    /// <summary>Tokens a ojo (≈ 4 caracteres por token). No hace falta más precisión para decidir cuándo comprimir.</summary>
    public static int Tokens(string? text) => string.IsNullOrEmpty(text) ? 0 : (text.Length + 3) / 4;

    public static int Tokens(IEnumerable<StoredMessage> messages) => messages.Sum(m => Tokens(m.Content) + Tokens(m.ToolCalls) + 4);

    /// <summary>Mensajes del final que nunca se resumen (las dos últimas vueltas, más margen).</summary>
    public const int KeepTail = 6;

    public sealed record Plan(bool Compact, int SummarizeUpTo)
    {
        public static readonly Plan None = new(false, 0);
    }

    /// <summary>
    /// Decide hasta dónde resumir. Se resume sólo si lo que iría al modelo (resumen + mensajes desde
    /// <paramref name="summarizedCount"/>) pasa del presupuesto; el corte cae justo antes de una pregunta
    /// del usuario (así la parte que queda empieza en turno limpio, sin un «tool» huérfano) y deja al menos
    /// <see cref="KeepTail"/> mensajes al final.
    /// </summary>
    public static Plan PlanFor(IReadOnlyList<StoredMessage> messages, string? summary, int summarizedCount, int budgetTokens)
    {
        summarizedCount = Math.Clamp(summarizedCount, 0, messages.Count);
        var tail = messages.Skip(summarizedCount).ToList();
        var used = Tokens(summary) + Tokens(tail);
        if (used <= budgetTokens) return Plan.None;
        // Cortes posibles: el inicio de cada pregunta del usuario por delante de la cola conservada.
        var limit = Math.Max(summarizedCount, messages.Count - KeepTail);
        var cut = -1;
        for (var i = summarizedCount + 1; i <= limit; i++)
            if (messages[i].Role == "user")
                cut = i;   // el más adelantado que aún deja cola: así se recorta lo máximo de una vez
        if (cut <= summarizedCount) return Plan.None;   // no hay dónde cortar sin romper la cola
        return new Plan(true, cut);
    }

    /// <summary>El presupuesto para la historia: el contexto menos lo que reserva la respuesta, el sistema y un margen.</summary>
    public static int Budget(int contextTokens, int answerTokens, bool thinking, int systemTokens)
        => Math.Max(512, contextTokens - (thinking ? answerTokens * 3 : answerTokens) - systemTokens - 512);

    /// <summary>Cómo se obtiene el resumen (el modelo). Las pruebas lo sustituyen.</summary>
    public static Func<string, string?, IReadOnlyList<StoredMessage>, CancellationToken, Task<string>> Summarizer = SummarizeWithModelAsync;

    /// <summary>
    /// Comprime la conversación si hace falta: actualiza <see cref="ChatThread.Summary"/> y
    /// <see cref="ChatThread.SummarizedCount"/>. Devuelve true si comprimió (para avisarlo en el chat).
    /// </summary>
    public static async Task<bool> CompactAsync(ChatThread thread, string baseUrl, int budgetTokens, CancellationToken cancel)
    {
        var plan = PlanFor(thread.Messages, thread.Summary, thread.SummarizedCount, budgetTokens);
        if (!plan.Compact) return false;
        var fold = thread.Messages.Skip(thread.SummarizedCount).Take(plan.SummarizeUpTo - thread.SummarizedCount).ToList();
        var summary = await Summarizer(baseUrl, thread.Summary, fold, cancel);
        if (string.IsNullOrWhiteSpace(summary)) return false;
        thread.Summary = summary.Trim();
        thread.SummarizedCount = plan.SummarizeUpTo;
        return true;
    }

    /// <summary>El resumen que se añade al sistema para que el modelo tenga lo viejo a mano.</summary>
    public static string SummaryBlock(ChatThread thread)
        => string.IsNullOrEmpty(thread.Summary) ? string.Empty
         : "[Summary of the earlier part of this conversation, so you can continue]\n" + thread.Summary;

    private static async Task<string> SummarizeWithModelAsync(string baseUrl, string? existing, IReadOnlyList<StoredMessage> messages, CancellationToken cancel)
    {
        var sb = new StringBuilder();
        if (!string.IsNullOrEmpty(existing)) sb.AppendLine("Summary so far:").AppendLine(existing).AppendLine();
        sb.AppendLine("New messages to fold into the summary:");
        foreach (var m in messages)
        {
            var role = m.Role switch { "user" => "User", "assistant" => "Assistant", "tool" => "Tool result", _ => m.Role };
            var content = m.Content;
            if (m.Role == "tool" && content.Length > 500) content = content[..500] + "…";
            if (!string.IsNullOrWhiteSpace(content)) sb.AppendLine($"{role}: {content}");
            else if (m.ToolCalls is { Length: > 0 }) sb.AppendLine($"{role}: (used tools)");
        }
        var prompt = new List<ChatMessage>
        {
            new("system", "You compress a conversation so it can continue without losing what matters. Write a concise summary that keeps the facts, decisions, names, numbers, files, URLs and the state of the task, and what the user wants. Keep the user's language. Output only the summary, no preamble."),
            new("user", sb.ToString()),
        };
        var result = new StringBuilder();
        await foreach (var delta in ChatClient.StreamAsync(baseUrl, prompt, thinking: false, maxTokens: 600, tools: null, cancel))
            if (delta.Content is { Length: > 0 } c) result.Append(c);
        return result.ToString();
    }
}
