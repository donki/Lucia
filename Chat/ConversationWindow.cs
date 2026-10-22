namespace SocLucia.Chat;

/// <summary>
/// Elige qué mensajes de la conversación se le mandan al modelo: los últimos, sin pasarse del contexto,
/// pero <b>siempre con la última pregunta del usuario dentro</b>. En modo agente, tras muchas vueltas de
/// herramienta (asistente + resultado, dos mensajes por vuelta), una ventana ciega de los últimos N dejaba
/// caer la pregunta original y quedaba «sistema + asistente + herramienta…» sin ningún mensaje de usuario.
/// La plantilla de chat de algunos modelos (Qwen3: la rama <c>multi_step_tool</c>) recorre los mensajes
/// hacia atrás buscando la última pregunta y, si no la encuentra, lo rechaza con un 500
/// («No user query found in messages»). Conservar la pregunta lo evita.
/// </summary>
public static class ConversationWindow
{
    public const int DefaultKeep = 30;

    /// <summary>
    /// Los últimos <paramref name="keep"/> mensajes; si en ese tramo no hay ninguno de usuario, se añade
    /// delante la última pregunta del usuario (esté donde esté), para que el modelo siempre la vea.
    /// </summary>
    public static List<StoredMessage> Select(IReadOnlyList<StoredMessage> all, int keep = DefaultKeep)
    {
        var start = Math.Max(0, all.Count - keep);
        var window = new List<StoredMessage>(all.Count - start);
        for (var i = start; i < all.Count; i++)
            window.Add(all[i]);
        if (window.Any(m => m.Role == "user"))
            return window;
        // No ha quedado ninguna pregunta en la ventana: se recupera la última que haya antes del tramo.
        for (var i = start - 1; i >= 0; i--)
            if (all[i].Role == "user")
            {
                window.Insert(0, all[i]);
                break;
            }
        return window;
    }
}
