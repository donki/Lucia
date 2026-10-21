using System.Text;
using SocLucia.Engine;
using SocLucia.Localization;

namespace SocLucia.Chat;

/// <summary>
/// Por donde va una respuesta, de menos a mas: arrancando la IA, preparando la pregunta (documentos,
/// adjuntos), esperando el primer trozo, razonando y escribiendo. Solo avanza.
/// </summary>
public enum AnswerPhase { Starting, Preparing, Waiting, Reasoning, Writing }

/// <summary>
/// La respuesta que se esta generando. La llena el hilo que lee el streaming (nunca el de la
/// interfaz) y la interfaz la lee cuando le toca repintar: asi un modelo rapido no inunda la cola
/// del hilo de la interfaz con un trabajo por trozo, y un modelo lento no la deja sin indicador.
/// </summary>
public sealed class PendingAnswer
{
    /// <summary>Lo que hay en un momento dado. <see cref="Version"/> cambia con cada trozo, para no repintar si nada ha cambiado.</summary>
    public sealed record Snapshot(string Content, string? Reasoning, IReadOnlyList<ToolCall>? Calls, string? Finish, AnswerPhase Phase, int Version);

    /// <summary>Repintar no mas a menudo que esto…</summary>
    public static readonly TimeSpan MinRepaint = TimeSpan.FromMilliseconds(100);

    /// <summary>…ni menos: aunque una respuesta enorme tarde en pintarse, se sigue viendo avanzar.</summary>
    public static readonly TimeSpan MaxRepaint = TimeSpan.FromMilliseconds(1500);

    /// <summary>El tiempo que se tarda en llegar a esperar antes de enseñar los segundos que lleva.</summary>
    public static readonly TimeSpan ShowSecondsAfter = TimeSpan.FromSeconds(3);

    private readonly object _gate = new();
    private readonly StringBuilder _content = new();
    private readonly StringBuilder _reasoning = new();
    private IReadOnlyList<ToolCall>? _calls;
    private string? _finish;
    private AnswerPhase _phase;
    private int _version;

    /// <param name="keptReasoning">El razonamiento de un intento anterior que se sigue enseñando (al repetir sin pensar).</param>
    public PendingAnswer(string? keptReasoning = null)
    {
        if (keptReasoning is { Length: > 0 })
            _reasoning.Append(keptReasoning);
    }

    public AnswerPhase Phase
    {
        get { lock (_gate) return _phase; }
    }

    /// <summary>Pasa a esa fase si va por delante de la actual; hacia atras no se vuelve.</summary>
    public void Advance(AnswerPhase phase)
    {
        lock (_gate)
        {
            if (phase <= _phase) return;
            _phase = phase;
            _version++;
        }
    }

    /// <summary>Un trozo del streaming (desde cualquier hilo).</summary>
    public void Add(ChatDelta delta)
    {
        lock (_gate)
        {
            if (delta.Content is { Length: > 0 } content)
            {
                _content.Append(content);
                if (_phase < AnswerPhase.Writing) _phase = AnswerPhase.Writing;
            }
            if (delta.Reasoning is { Length: > 0 } reasoning)
            {
                _reasoning.Append(reasoning);
                if (_phase < AnswerPhase.Reasoning) _phase = AnswerPhase.Reasoning;
            }
            if (delta.ToolCalls is not null) _calls = delta.ToolCalls;
            if (delta.FinishReason is not null) _finish = delta.FinishReason;
            _version++;
        }
    }

    public Snapshot Read()
    {
        lock (_gate)
            return new Snapshot(_content.ToString(), _reasoning.Length > 0 ? _reasoning.ToString() : null, _calls, _finish, _phase, _version);
    }

    /// <summary>
    /// El texto del indicador de espera. Mientras la IA arranca se dice si se esta bajando el motor o
    /// cargando el modelo (lo que mas tarda); luego, preparando, pensando o razonando. Pasados unos
    /// segundos se añaden los que lleva, para que se vea que sigue viva.
    /// </summary>
    public static string Status(AnswerPhase phase, EngineState engine, TimeSpan elapsed)
    {
        var key = phase switch
        {
            AnswerPhase.Reasoning => "AnswerReasoning",
            AnswerPhase.Starting when engine == EngineState.DownloadingEngine => "AnswerDownloadingEngine",
            AnswerPhase.Starting when engine == EngineState.Starting => "AnswerLoadingModel",
            AnswerPhase.Starting or AnswerPhase.Preparing => "AnswerPreparing",
            _ => "AnswerWaiting",
        };
        var text = Loc.Get(key);
        return elapsed >= ShowSecondsAfter ? Loc.Format("AnswerElapsed", text, (int)elapsed.TotalSeconds) : text;
    }

    /// <summary>
    /// Cada cuanto repintar segun lo que costo el ultimo repintado: una respuesta larga (Markdown
    /// entero otra vez) se repinta menos a menudo, para que pintar no se coma el hilo de la interfaz
    /// (como mucho una quinta parte del tiempo).
    /// </summary>
    public static TimeSpan NextRepaint(TimeSpan lastPaint)
    {
        var next = TimeSpan.FromTicks(lastPaint.Ticks * 5);
        return next < MinRepaint ? MinRepaint : next > MaxRepaint ? MaxRepaint : next;
    }
}
