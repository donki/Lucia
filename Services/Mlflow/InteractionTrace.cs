using System.Security.Cryptography;
using SocLucia.Engine;

namespace SocLucia.Services.Mlflow;

/// <summary>
/// La traza de una interaccion (una pregunta del chat o una peticion de un editor), como la enseña la
/// seccion GenAI de MLflow (pestaña Traces del experimento):
/// <list type="bullet">
/// <item>Raiz de tipo <c>CHAT_MODEL</c> («chat» o «editor gateway») con el modelo, la temperatura, el tope de
/// tokens, el contexto, el tiempo hasta el primer token, los tokens de entrada y salida, tokens/s y el modo.
/// Sus entradas y salidas llevan la pregunta y la respuesta solo si el usuario marco «mandar el texto»; si no,
/// solo longitudes.</item>
/// <item>Hijos: el motor (<c>TASK</c>: comprobar o arrancar y cargar), los documentos (<c>RETRIEVER</c>: nombres de
/// los ficheros; su contenido, solo con el texto), cada llamada al modelo (<c>LLM</c>) y cada herramienta
/// (<c>TOOL</c>: nombre y resultado; argumentos y salida, solo con el texto).</item>
/// <item>Estado final OK o ERROR con el mensaje; una cancelacion queda OK con <c>lucia.outcome = cancelled</c> y un
/// evento «cancelled» (no es un fallo del sistema).</item>
/// <item>Agrupacion: <c>session.id</c> = id aleatorio de la conversacion y <c>user.id</c> = id aleatorio de esta
/// instalacion (MLflow los convierte en los metadatos de sesion y usuario). Nada personal.</item>
/// </list>
/// Se arma en el hilo de la interfaz (solo objetos en memoria) y se encola entera al acabar.
/// </summary>
public sealed class InteractionTrace
{
    private readonly TraceData _trace = new() { TraceId = RandomNumberGenerator.GetBytes(16) };
    private readonly SpanData _root;
    private readonly bool _text;
    private SpanData? _llm;

    public bool IncludesText => _text;
    public SpanData Root => _root;
    public TraceData Data => _trace;

    public InteractionTrace(string mode, string conversationId, string installId, RunConfig config, bool includeText, string? question, int questionChars, int attachments)
    {
        _text = includeText;
        _root = new SpanData { SpanId = RandomNumberGenerator.GetBytes(8), Name = mode == "door" ? "editor gateway" : "chat", Start = DateTimeOffset.Now };
        _trace.Spans.Add(_root);
        _root.Set("mlflow.spanType", "CHAT_MODEL")
            .Set("gen_ai.operation.name", "chat")
            .Set("session.id", conversationId)
            .Set("user.id", installId)
            .Set("lucia.mode", mode)
            .Set("mlflow.llm.model", config.Model)
            .Set("mlflow.llm.provider", "llama.cpp")
            .Set("gen_ai.request.model", config.Model)
            .Set("gen_ai.request.temperature", config.Temperature)
            .Set("gen_ai.request.max_tokens", (long)config.MaxTokens)
            .Set("lucia.context_tokens", (long)config.Context)
            .Set("lucia.thinking", config.Thinking)
            .Set("lucia.accelerator", config.Accelerator)
            .Set("lucia.app_version", config.AppVersion)
            .Set("mlflow.spanInputs", Inputs(question, questionChars, attachments));
    }

    private Dictionary<string, object?> Inputs(string? question, int chars, int attachments)
    {
        var map = new Dictionary<string, object?> { ["question_chars"] = (long)chars, ["attachments"] = (long)attachments };
        if (_text && question is not null)
            map["messages"] = new List<object?> { new Dictionary<string, object?> { ["role"] = "user", ["content"] = question } };
        return map;
    }

    /// <summary>Un hijo de la raiz (o del que se diga) que empieza ahora.</summary>
    public SpanData Child(string name, string type, SpanData? parent = null)
    {
        var span = new SpanData { SpanId = RandomNumberGenerator.GetBytes(8), ParentId = (parent ?? _root).SpanId, Name = name, Start = DateTimeOffset.Now };
        span.Set("mlflow.spanType", type);
        lock (_trace.Spans) _trace.Spans.Add(span);
        return span;
    }

    public static void End(SpanData span, bool ok = true, string? error = null)
    {
        span.End = DateTimeOffset.Now;
        span.Ok = ok;
        span.StatusMessage = error;
    }

    /// <summary>El motor: comprobar que esta listo o arrancarlo y cargar el modelo.</summary>
    public SpanData Engine() => Child("engine", "TASK");

    /// <summary>Los documentos que se pasan con la pregunta: sus nombres; el texto, solo con la casilla.</summary>
    public void Documents(SpanData span, string context)
    {
        var names = System.Text.RegularExpressions.Regex.Matches(context, @"^\[(?!page )([^\]\r\n]+)\]\s*$", System.Text.RegularExpressions.RegexOptions.Multiline)
            .Select(m => (object?)m.Groups[1].Value).Distinct().ToList();
        var outputs = new Dictionary<string, object?> { ["documents"] = names, ["context_chars"] = (long)context.Length };
        if (_text) outputs["context"] = context;
        span.Set("mlflow.spanOutputs", outputs).Set("lucia.documents", (long)names.Count);
        End(span);
    }

    /// <summary>Una llamada al modelo (una vuelta: el modo agente puede hacer varias).</summary>
    public SpanData Llm(int round, int messages, bool thinking)
    {
        _llm = Child($"llama-server round {round}", "LLM");
        _llm.Set("gen_ai.operation.name", "chat").Set("lucia.round", (long)round).Set("lucia.thinking", thinking)
            .Set("mlflow.spanInputs", new Dictionary<string, object?> { ["messages"] = (long)messages });
        return _llm;
    }

    /// <summary>Cierra una vuelta del modelo con lo que llego: tiempos, tokens, longitudes y (con la casilla) el texto.</summary>
    public void EndLlm(SpanData span, double? ttftMs, ChatUsage? usage, string? content, int reasoningChars, int toolCalls, string? finish, bool ok, string? error)
    {
        span.Set("lucia.ttft_ms", ttftMs).Set("lucia.finish_reason", finish).Set("lucia.tool_calls", (long)toolCalls);
        Usage(span, usage);
        var outputs = new Dictionary<string, object?> { ["answer_chars"] = (long)(content?.Length ?? 0), ["reasoning_chars"] = (long)reasoningChars, ["tool_calls"] = (long)toolCalls };
        if (_text && content is not null) outputs["content"] = content;
        span.Set("mlflow.spanOutputs", outputs);
        End(span, ok, error);
    }

    private static void Usage(SpanData span, ChatUsage? usage)
    {
        if (usage is null) return;
        span.Set("lucia.tokens_per_s", usage.TokensPerSecond);
        if (usage.PromptTokens is null && usage.CompletionTokens is null) return;
        var input = (long)(usage.PromptTokens ?? 0);
        var output = (long)(usage.CompletionTokens ?? 0);
        span.Set("gen_ai.usage.input_tokens", input).Set("gen_ai.usage.output_tokens", output)
            .Set("mlflow.chat.tokenUsage", new Dictionary<string, object?> { ["input_tokens"] = input, ["output_tokens"] = output, ["total_tokens"] = input + output });
    }

    /// <summary>Una herramienta: nombre y resultado; sus argumentos y salida (rutas, ordenes, contenido), solo con la casilla.</summary>
    public SpanData Tool(string name, string? arguments)
    {
        var span = Child("tool " + name, "TOOL");
        span.Set("gen_ai.operation.name", "execute_tool").Set("gen_ai.tool.name", name);
        if (_text && arguments is not null) span.Set("mlflow.spanInputs", new Dictionary<string, object?> { ["arguments"] = arguments });
        return span;
    }

    public void EndTool(SpanData span, string? output, string outcome)
    {
        var outputs = new Dictionary<string, object?> { ["output_chars"] = (long)(output?.Length ?? 0), ["outcome"] = outcome };
        if (_text && output is not null) outputs["output"] = output;
        span.Set("mlflow.spanOutputs", outputs);
        End(span, outcome != "error", outcome == "error" ? output : null);
    }

    /// <summary>Una linea del registro de diagnostico durante la interaccion, como evento de la raiz (ya sin datos personales).</summary>
    public void Event(string area, string line)
    {
        lock (_root.Events) _root.Events.Add((DateTimeOffset.Now, area, [new("message", line)]));
    }

    /// <summary>Cierra la raiz: resultado, totales y (con la casilla) la respuesta. Devuelve la traza lista para enviar.</summary>
    public TraceData Finish(string outcome, string? error, string? answer, double? ttftMs, ChatUsage? usage, int answerChars, int toolCalls)
    {
        foreach (var open in _trace.Spans.Where(s => s.Ok is null && s != _root).ToList())
            End(open, outcome != "error", outcome == "error" ? error : "not finished");
        _root.Set("lucia.outcome", outcome).Set("lucia.ttft_ms", ttftMs).Set("lucia.tool_calls", (long)toolCalls)
            .Set("lucia.duration_ms", (DateTimeOffset.Now - _root.Start).TotalMilliseconds);
        Usage(_root, usage);
        var outputs = new Dictionary<string, object?> { ["answer_chars"] = (long)answerChars, ["outcome"] = outcome };
        if (_text && answer is not null)
            outputs["choices"] = new List<object?> { new Dictionary<string, object?> { ["message"] = new Dictionary<string, object?> { ["role"] = "assistant", ["content"] = answer } } };
        _root.Set("mlflow.spanOutputs", outputs);
        if (outcome == "cancelled") Event("cancelled", "the user stopped the answer");
        End(_root, outcome != "error", error);
        return _trace;
    }

    /// <summary>Una congelacion de la ventana como traza propia (sin interaccion): cuanto duro y en que fase.</summary>
    public static TraceData Freeze(double seconds, string phase, string installId, string appVersion)
    {
        var trace = new TraceData { TraceId = RandomNumberGenerator.GetBytes(16) };
        var end = DateTimeOffset.Now;
        var span = new SpanData { SpanId = RandomNumberGenerator.GetBytes(8), Name = "ui freeze", Start = end - TimeSpan.FromSeconds(seconds), End = end, Ok = false, StatusMessage = $"The window did not respond for {seconds:0.0} s" };
        span.Set("mlflow.spanType", "UNKNOWN").Set("lucia.freeze_s", seconds).Set("lucia.phase", phase).Set("user.id", installId).Set("lucia.app_version", appVersion);
        trace.Spans.Add(span);
        return trace;
    }
}
