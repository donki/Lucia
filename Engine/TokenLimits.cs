using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using SocLucia.Services;

namespace SocLucia.Engine;

/// <summary>
/// Los dos tamaños que se ajustan en Ajustes › Respuestas y contexto, con sus limites:
/// <list type="bullet">
/// <item>Tokens maximos por respuesta (<c>max_tokens</c>): lo mas largo que escribe la IA de una vez.</item>
/// <item>Contexto (<c>-c</c> de llama-server): cuanto texto tiene en cuenta a la vez. Su tope sale de la
/// IA (el contexto con el que se entreno, en la cabecera del GGUF) y de la memoria: cada token de
/// contexto ocupa cache en la grafica.</item>
/// </list>
/// </summary>
public static class TokenLimits
{
    public const int AnswerMin = 256;
    public const int AnswerMax = 32768;
    public const int AnswerDefault = 2048;

    public const int ContextMin = 2048;
    public const int ContextDefault = 8192;

    /// <summary>Nunca mas que esto, aunque la IA y el PC den para mas: la cache crece con el contexto y la respuesta se hace lenta.</summary>
    public const int ContextCeiling = 131072;

    /// <summary>Si la cabecera de la IA no se puede leer, el tope de siempre.</summary>
    public const int ContextUnknownMax = 32768;

    private const long MiB = 1024L * 1024;
    private const long GiB = 1024 * MiB;

    /// <summary>
    /// Hasta donde se puede llevar el contexto con esta IA en este PC: <see cref="Max"/> (lo que se
    /// admite), <see cref="Fast"/> (lo que cabe entero en la grafica, o en la memoria sin apretar si
    /// no hay grafica: hasta ahi va rapido; si se desconoce, null) y <see cref="Trained"/> (el de la IA).
    /// </summary>
    public sealed record ContextRange(int Max, int? Fast, int? Trained);

    public static ContextRange RangeFor(GgufInfo? info, long modelBytes, PcProfile pc)
    {
        var trained = info?.ContextLength;
        var upper = Math.Clamp(trained ?? ContextUnknownMax, ContextMin, ContextCeiling);
        if (info?.KvBytesPerToken is not { } perToken)
            return new ContextRange(upper, null, trained);
        long fast, hard;
        if (pc.HasUsableGpu)
        {
            // La grafica lleva el modelo entero (-ngl 99) y lo que sobra, menos un margen para el computo, es para la cache.
            fast = pc.VramBytes - modelBytes * 105 / 100 - 768 * MiB;
            // Windows deja que la grafica tire de la memoria compartida si se pasa: funciona, pero lento. Hasta una cuarta parte de la RAM.
            hard = Math.Max(fast, 0) + pc.RamBytes / 4;
        }
        else
        {
            fast = Math.Min(pc.RamBytes - modelBytes - 4 * GiB, 8 * GiB);
            hard = pc.RamBytes - modelBytes - 2 * GiB;
        }
        var max = Math.Clamp(Floor(hard / perToken), ContextMin, upper);
        var quick = Math.Clamp(Floor(fast / perToken), ContextMin, max);
        return new ContextRange(max, quick, trained);
    }

    /// <summary>El rango de la IA activa en este PC (lee la cabecera del GGUF, guardada mientras no cambie).</summary>
    public static ContextRange RangeFor(AppSettings settings)
    {
        var path = settings.ModelPath;
        long bytes = 0;
        try { if (path is { Length: > 0 } && System.IO.File.Exists(path)) bytes = new System.IO.FileInfo(path).Length; } catch (Exception) { }
        return RangeFor(GgufInfo.Read(path), bytes, Pc);
    }

    /// <summary>El contexto con el que arranca el motor: el ajustado, dentro de lo que admiten la IA y el PC.</summary>
    public static int Context(AppSettings settings) => Math.Clamp(settings.ContextTokens, ContextMin, RangeFor(settings).Max);

    /// <summary>El tope por respuesta ajustado, dentro de sus limites.</summary>
    public static int Answer(AppSettings settings) => Math.Clamp(settings.MaxAnswerTokens, AnswerMin, AnswerMax);

    private static PcProfile? _pc;

    /// <summary>El PC se mira una vez (registro): la grafica y la memoria no cambian con la aplicacion abierta.</summary>
    public static PcProfile Pc
    {
        get => _pc ??= PcProfile.Detect("auto");
        internal set => _pc = value;
    }

    private static int Floor(long tokens) => tokens <= 0 ? 0 : (int)Math.Min(tokens / 1024 * 1024, int.MaxValue);

    /// <summary>
    /// Un numero escrito a mano: admite separadores de miles («8 192», «8.192», «8,192»). Null si no es
    /// un entero o se sale de [min, max].
    /// </summary>
    public static int? Parse(string? text, int min, int max)
    {
        if (text is null) return null;
        var digits = new StringBuilder();
        foreach (var c in text.Trim())
        {
            if (char.IsAsciiDigit(c)) digits.Append(c);
            else if (c is ' ' or ' ' or ' ' or '.' or ',' or '\'') continue;
            else return null;
        }
        if (digits.Length is 0 or > 9) return null;
        var value = int.Parse(digits.ToString(), System.Globalization.CultureInfo.InvariantCulture);
        return value >= min && value <= max ? value : null;
    }

    /// <summary>
    /// Peticiones de editores por la puerta (<c>/v1/chat/completions</c> y <c>/v1/completions</c>).
    /// Decision: si el cliente trae su propio maximo (<c>max_tokens</c>, <c>max_completion_tokens</c> o
    /// <c>n_predict</c>) se respeta, porque el editor sabe lo que necesita (un autocompletado corto, un
    /// fichero entero); solo se recorta al contexto, que no se puede pasar. Si no trae ninguno se pone
    /// el de Ajustes (el triple si pide razonar, como en el chat): sin tope, llama-server escribiria
    /// hasta llenar el contexto.
    /// </summary>
    public static byte[] ApplyToDoorRequest(byte[] body, int answer, int context)
    {
        try
        {
            if (JsonNode.Parse(body) is not JsonObject obj)
                return body;
            var changed = false;
            var own = false;
            foreach (var name in new[] { "max_tokens", "max_completion_tokens", "n_predict" })
            {
                if (obj[name] is not JsonValue v || !v.TryGetValue<double>(out var asked)) continue;
                if (asked <= 0) { obj.Remove(name); changed = true; continue; }   // «sin limite»: se trata como si no viniera
                own = true;
                if (asked > context) { obj[name] = context; changed = true; }
            }
            if (!own)
            {
                var thinking = obj["chat_template_kwargs"]?["enable_thinking"] is JsonValue t && t.TryGetValue<bool>(out var on) && on;
                obj["max_tokens"] = Math.Min(thinking ? answer * 3 : answer, context);
                changed = true;
            }
            return changed ? Encoding.UTF8.GetBytes(obj.ToJsonString()) : body;
        }
        catch (JsonException)
        {
            return body;
        }
    }
}
