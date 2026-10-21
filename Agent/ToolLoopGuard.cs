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
public enum GuardStop { None, Cap, Repeats, Failures }

/// <summary>
/// Freno del modo agente para una respuesta. Sin el, un modelo atascado repetia la misma orden de PowerShell
/// que fallaba (siempre con el mismo error de sintaxis) sin parar. Reglas:
/// <list type="bullet">
/// <item>Como mucho <see cref="MaxCalls"/> acciones por respuesta (Ajustes › Modo trabajo).</item>
/// <item>Una accion identica (misma herramienta y mismos argumentos, sin contar espacios, mayusculas ni el
/// «porque») a otra que ya fallo en esta respuesta no se vuelve a ejecutar; si el modelo la pide otra vez
/// tras avisarle, se para.</item>
/// <item>Tres fallos seguidos de la misma herramienta (aunque cambie un poco la orden) tambien la paran.</item>
/// </list>
/// Lo que haga el usuario con los permisos no cambia: esto se comprueba antes de pedirlo.
/// </summary>
public sealed class ToolLoopGuard(int maxCalls)
{
    public const int DefaultMaxCalls = 15;
    public const int MinMaxCalls = 1;
    public const int MaxMaxCalls = 100;
    public const int MaxRepeats = 2;
    public const int MaxConsecutiveFailures = 3;

    public int MaxCalls { get; } = Math.Clamp(maxCalls, MinMaxCalls, MaxMaxCalls);
    public int Calls { get; private set; }
    public GuardStop Stopped { get; private set; }
    /// <summary>La ultima accion que fallo y su error (para decirselo al usuario).</summary>
    public string? LastFailedLabel { get; private set; }
    public string? LastError { get; private set; }

    private readonly Dictionary<string, string> _failed = new(StringComparer.Ordinal);
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
