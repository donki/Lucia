using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using SocLucia.Engine;

namespace SocLucia.Agent;

/// <summary>Que hacer con una accion que pide el modelo.</summary>
public enum GuardVerdict
{
    /// <summary>Se ejecuta (y, si hace falta, se pide permiso como siempre).</summary>
    Run,
    /// <summary>No se ejecuta: es identica a una que ya fallo. Se le dice al modelo que no la repita igual.</summary>
    Repeat,
    /// <summary>Se para la respuesta: tope de acciones, o el modelo insiste en repetir lo que falla.</summary>
    Stop,
}

/// <summary>Por que se paro.</summary>
public enum GuardStop { None, Cap, Repeats, Failures, NoProgress }

/// <summary>
/// Freno del modo agente para una respuesta. Sin el, un modelo atascado repetia la misma orden de PowerShell
/// que fallaba (siempre con el mismo error de sintaxis) sin parar. Reglas:
/// <list type="bullet">
/// <item>Como mucho <see cref="MaxCalls"/> acciones por respuesta (Ajustes › Modo trabajo), salvo «sin limite»
/// (<see cref="Unlimited"/>, guardado como 0): entonces solo frenan las dos reglas de abajo.</item>
/// <item>Una accion identica (misma herramienta y mismos argumentos, sin contar espacios, mayusculas ni el
/// «porque») a otra que ya fallo en esta respuesta no se vuelve a ejecutar; si el modelo la pide otra vez
/// tras avisarle, se para.</item>
/// <item>Tres fallos seguidos de la misma herramienta (aunque cambie un poco la orden) tambien la paran.</item>
/// <item>Acciones que no fallan pero no dan nada (busquedas sin resultados, carpetas vacias, sin coincidencias)
/// o busquedas casi iguales a otra de esta respuesta: a la <see cref="NudgeAfter"/>ª seguida se le pide al
/// modelo que cambie de enfoque; a la <see cref="NoProgressAfter"/>ª se deja de buscar y contesta con lo que
/// tiene. Paso el 2026-10-22: con el buscador bloqueado, la IA buscaba lo mismo con otras palabras sin fin.</item>
/// </list>
/// Lo que haga el usuario con los permisos no cambia: esto se comprueba antes de pedirlo.
/// </summary>
public sealed class ToolLoopGuard(int maxCalls)
{
    public const int DefaultMaxCalls = 15;
    public const int MinMaxCalls = 1;
    public const int MaxMaxCalls = 100;
    /// <summary>El valor del ajuste que quiere decir «sin limite de acciones».</summary>
    public const int UnlimitedCalls = 0;
    public const int MaxRepeats = 2;
    public const int MaxConsecutiveFailures = 3;
    public const int NudgeAfter = 2;
    public const int NoProgressAfter = 4;

    public bool Unlimited { get; } = maxCalls == UnlimitedCalls;
    public int MaxCalls { get; } = maxCalls == UnlimitedCalls ? int.MaxValue : Math.Clamp(maxCalls, MinMaxCalls, MaxMaxCalls);
    public int Calls { get; private set; }
    public GuardStop Stopped { get; private set; }
    /// <summary>La ultima accion que fallo y su error (para decirselo al usuario).</summary>
    public string? LastFailedLabel { get; private set; }
    public string? LastError { get; private set; }

    private readonly Dictionary<string, string> _failed = new(StringComparer.Ordinal);
    private readonly List<HashSet<string>> _queries = [];
    private int _fruitless;
    private string? _nudge;
    /// <summary>Acciones seguidas que no han dado nada util (para decirlo al parar).</summary>
    public int Fruitless => _fruitless;
    private int _repeats;
    private string? _failingTool;
    private int _consecutive;

    /// <summary>Antes de ejecutar una accion.</summary>
    public GuardVerdict Before(ToolCall call)
    {
        if (Stopped != GuardStop.None) return GuardVerdict.Stop;
        if (_failed.ContainsKey(Key(call)))
        {
            if (++_repeats >= MaxRepeats) { Stopped = GuardStop.Repeats; return GuardVerdict.Stop; }
            return GuardVerdict.Repeat;
        }
        if (Calls >= MaxCalls) { Stopped = GuardStop.Cap; return GuardVerdict.Stop; }
        Calls++;
        return GuardVerdict.Run;
    }

    /// <summary>Despues de ejecutarla, con lo que devolvio. Devuelve true si hay que parar.</summary>
    public bool After(ToolCall call, string label, string output)
    {
        if (!IsFailure(output))
        {
            if (_failingTool == call.Name) { _failingTool = null; _consecutive = 0; }
            var similar = call.Name == ToolBox.WebSearch && SimilarSearch(call);
            if (!IsFruitless(output) && !similar)
            {
                _fruitless = 0;
                return false;
            }
            // No ha fallado, pero no avanza: no se repite igual, y si sigue asi se cambia de enfoque o se para.
            _failed[Key(call)] = output;
            LastFailedLabel = label;
            LastError = similar && !IsFruitless(output) ? "almost the same search as before" : FirstErrorLine(output);
            _fruitless++;
            if (_fruitless >= NoProgressAfter) { Stopped = GuardStop.NoProgress; return true; }
            if (_fruitless == NudgeAfter)
                _nudge = "[Lucia] This is the second action in a row that gave nothing useful. Change strategy instead of rephrasing the same thing: " +
                         "search in English or with broader terms once, read a concrete page you know (official documentation, source code, a repository) with fetch_url, " +
                         "work it out from what you already know, or tell the user what you could not find and what they could try.";
            return false;
        }
        _failed[Key(call)] = output;
        LastFailedLabel = label;
        LastError = FirstErrorLine(output);
        if (_failingTool == call.Name) _consecutive++;
        else { _failingTool = call.Name; _consecutive = 1; }
        if (_consecutive >= MaxConsecutiveFailures) { Stopped = GuardStop.Failures; return true; }
        return false;
    }

    /// <summary>El aviso de «cambia de enfoque», una sola vez (se añade a la salida de la herramienta).</summary>
    public string? TakeNudge()
    {
        var nudge = _nudge;
        _nudge = null;
        return nudge;
    }

    /// <summary>Lo que se le dice al modelo al dejar de buscar: que conteste ya con lo que tiene.</summary>
    public const string NoProgressAnswer = "[Lucia] Stop using tools now: several actions in a row gave nothing useful. Answer the user with what you know, " +
                                           "say clearly what you could not find, and suggest how to go on (other sources, what they could give you).";

    /// <summary>Salidas que no son error pero no dan nada: sin resultados, sin coincidencias, carpeta vacia, nada.</summary>
    public static bool IsFruitless(string output)
    {
        var o = output.TrimStart();
        return o.Length == 0 || o.StartsWith(ToolBox.NoResults, StringComparison.Ordinal) || o.StartsWith("[no matches]", StringComparison.Ordinal)
               || o.StartsWith("[empty folder]", StringComparison.Ordinal) || o.StartsWith("[no documents", StringComparison.Ordinal);
    }

    /// <summary>
    /// Si una busqueda es casi la misma que otra de esta respuesta (la mitad o mas de sus palabras en comun):
    /// cambiar el orden o dos palabras no es buscar otra cosa.
    /// </summary>
    private bool SimilarSearch(ToolCall call)
    {
        string query;
        try { query = (JsonNode.Parse(call.Arguments) as JsonObject)?["query"]?.GetValue<string>() ?? string.Empty; }
        catch (Exception) { query = string.Empty; }
        var words = Words(query);
        if (words.Count == 0) return false;
        var similar = _queries.Any(q => (double)q.Intersect(words).Count() / q.Union(words).Count() >= 0.5);
        _queries.Add(words);
        return similar;
    }

    internal static HashSet<string> Words(string text)
        => Regex.Matches(text.ToLowerInvariant(), @"[\p{L}\p{N}+#.]{3,}").Select(m => m.Value.Trim('.')).Where(w => w.Length >= 3).ToHashSet();

    /// <summary>Lo que se le contesta al modelo en lugar de ejecutar una repeticion.</summary>
    public string RepeatAnswer(ToolCall call)
        => "Not run: this exact action already failed in this answer with: " + FirstErrorLine(_failed.GetValueOrDefault(Key(call)) ?? string.Empty)
           + "\nDo not repeat it unchanged. Fix the cause (another approach, other arguments) or explain the problem to the user.";

    /// <summary>
    /// Si una salida es un fallo: codigo de salida distinto de 0, tiempo agotado, «[error]» de la herramienta,
    /// o el error de PowerShell aunque el codigo diga 0.
    /// </summary>
    public static bool IsFailure(string output)
    {
        var o = output.TrimStart();
        if (o.StartsWith("[error]", StringComparison.Ordinal) || o.StartsWith("[timed out", StringComparison.Ordinal)) return true;
        var exit = Regex.Match(o, @"^\[exit code (-?\d+)\]");
        if (exit.Success && exit.Groups[1].Value != "0") return true;
        return Regex.IsMatch(o, @"ParserError|CategoryInfo\s*:|FullyQualifiedErrorId\s*:", RegexOptions.IgnoreCase);
    }

    private static string FirstErrorLine(string output)
    {
        var lines = output.Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0 && !l.StartsWith("[exit code", StringComparison.Ordinal)).ToList();
        var line = lines.FirstOrDefault(l => Regex.IsMatch(l, "error|no se|not |cannot|esperaba|expected|invalid", RegexOptions.IgnoreCase)) ?? lines.FirstOrDefault() ?? string.Empty;
        return line.Length > 300 ? line[..300] + "…" : line;
    }

    /// <summary>La huella de una accion: herramienta y argumentos, sin «reason», sin espacios de mas ni mayusculas.</summary>
    public static string Key(ToolCall call)
    {
        string args;
        try
        {
            if (JsonNode.Parse(call.Arguments) is JsonObject o)
            {
                o.Remove("reason");
                args = string.Join("\u0001", o.OrderBy(p => p.Key, StringComparer.Ordinal).Select(p => p.Key + "=" + (p.Value is JsonValue v && v.TryGetValue<string>(out var s) ? s : p.Value?.ToJsonString())));
            }
            else args = call.Arguments;
        }
        catch (Exception) { args = call.Arguments; }
        var sb = new StringBuilder(call.Name).Append('|');
        foreach (var c in args)
            if (!char.IsWhiteSpace(c)) sb.Append(char.ToLowerInvariant(c));
        return sb.ToString();
    }
}
