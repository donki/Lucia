using System.IO;
using System.Text;

namespace SocLucia.Services.Mlflow;

/// <summary>Un tramo (span) de una traza: nombre, tiempos, padre, atributos, eventos y estado.</summary>
public sealed class SpanData
{
    public required byte[] SpanId { get; init; }
    public byte[]? ParentId { get; init; }
    public required string Name { get; init; }
    public DateTimeOffset Start { get; set; }
    public DateTimeOffset End { get; set; }
    /// <summary>Valores: string, long, int, double, bool, IDictionary&lt;string, object?&gt; (mapa) o IEnumerable&lt;object?&gt; (lista).</summary>
    public List<KeyValuePair<string, object?>> Attributes { get; } = [];
    public List<(DateTimeOffset At, string Name, List<KeyValuePair<string, object?>> Attributes)> Events { get; } = [];
    /// <summary>null: sin estado; true: OK; false: ERROR.</summary>
    public bool? Ok { get; set; }
    public string? StatusMessage { get; set; }

    public SpanData Set(string key, object? value)
    {
        if (value is null) return this;
        Attributes.RemoveAll(a => a.Key == key);
        Attributes.Add(new(key, value));
        return this;
    }
}

/// <summary>Una traza: su id (16 bytes) y sus tramos (el primero, la raiz).</summary>
public sealed class TraceData
{
    public required byte[] TraceId { get; init; }
    public List<SpanData> Spans { get; } = [];
    public string Hex => Convert.ToHexString(TraceId).ToLowerInvariant();
}

/// <summary>
/// OTLP/HTTP en protobuf (<c>ExportTraceServiceRequest</c>, opentelemetry-proto v1) escrito a mano: es lo
/// que acepta el <c>/v1/traces</c> de MLflow desde la 3.6 (la 3.6 solo protobuf; JSON llego despues). Solo
/// los campos que hacen falta; sin dependencias.
/// </summary>
public static class Otlp
{
    public const string ServiceName = "sOC Lucia";

    public static byte[] Encode(IEnumerable<TraceData> traces, string serviceVersion)
    {
        var resource = Message(w =>
        {
            KeyValue(w, 1, "service.name", ServiceName);
            KeyValue(w, 1, "service.version", serviceVersion);
        });
        var scope = Message(w => { w.String(1, "sOCLucia"); w.String(2, serviceVersion); });
        var spans = Message(w =>
        {
            w.Bytes(1, scope);
            foreach (var trace in traces)
                foreach (var span in trace.Spans)
                    w.Bytes(2, Span(trace, span));
        });
        var resourceSpans = Message(w => { w.Bytes(1, resource); w.Bytes(2, spans); });
        return Message(w => w.Bytes(1, resourceSpans));
    }

    private static byte[] Span(TraceData trace, SpanData s) => Message(w =>
    {
        w.Bytes(1, trace.TraceId);
        w.Bytes(2, s.SpanId);
        if (s.ParentId is { } parent) w.Bytes(4, parent);
        w.String(5, s.Name);
        w.Varint(6, s.ParentId is null ? 2UL : 1UL);   // SERVER la raiz, INTERNAL los demas
        w.Fixed64(7, Nanos(s.Start));
        w.Fixed64(8, Nanos(s.End < s.Start ? s.Start : s.End));
        foreach (var a in s.Attributes) KeyValue(w, 9, a.Key, a.Value);
        foreach (var e in s.Events)
            w.Bytes(11, Message(ev =>
            {
                ev.Fixed64(1, Nanos(e.At));
                ev.String(2, e.Name);
                foreach (var a in e.Attributes) KeyValue(ev, 3, a.Key, a.Value);
            }));
        if (s.Ok is { } ok)
            w.Bytes(15, Message(st =>
            {
                if (s.StatusMessage is { Length: > 0 } m) st.String(2, m);
                st.Varint(3, ok ? 1UL : 2UL);
            }));
    });

    private static ulong Nanos(DateTimeOffset t) => (ulong)(t.ToUnixTimeMilliseconds() * 1_000_000L + t.Ticks % TimeSpan.TicksPerMillisecond * 100);

    private static void KeyValue(Writer w, int field, string key, object? value)
        => w.Bytes(field, Message(kv => { kv.String(1, key); kv.Bytes(2, AnyValue(value)); }));

    private static byte[] AnyValue(object? value) => Message(w =>
    {
        switch (value)
        {
            case null: break;
            case string s: w.String(1, s); break;
            case bool b: w.Varint(2, b ? 1UL : 0UL); break;
            case int i: w.Varint(3, unchecked((ulong)(long)i)); break;
            case long l: w.Varint(3, unchecked((ulong)l)); break;
            case double d: w.Double(4, d); break;
            case float f: w.Double(4, f); break;
            case IDictionary<string, object?> map:
                w.Bytes(6, Message(kvl => { foreach (var (k, v) in map) KeyValue(kvl, 1, k, v); }));
                break;
            case System.Collections.IEnumerable list:
                w.Bytes(5, Message(arr => { foreach (var item in list) arr.Bytes(1, AnyValue(item)); }));
                break;
            default: w.String(1, value.ToString() ?? string.Empty); break;
        }
    });

    private static byte[] Message(Action<Writer> build)
    {
        var w = new Writer();
        build(w);
        return w.ToArray();
    }

    /// <summary>Lo minimo de protobuf: varint, longitud delimitada, fixed64 y double.</summary>
    private sealed class Writer
    {
        private readonly MemoryStream _ms = new();

        private void Raw(ulong v)
        {
            while (v >= 0x80) { _ms.WriteByte((byte)(v | 0x80)); v >>= 7; }
            _ms.WriteByte((byte)v);
        }

        public void Varint(int field, ulong v) { Raw((ulong)field << 3); Raw(v); }

        public void Bytes(int field, byte[] data) { Raw(((ulong)field << 3) | 2); Raw((ulong)data.Length); _ms.Write(data); }

        public void String(int field, string s) => Bytes(field, Encoding.UTF8.GetBytes(s));

        public void Fixed64(int field, ulong v) { Raw(((ulong)field << 3) | 1); Span<byte> b = stackalloc byte[8]; BitConverter.TryWriteBytes(b, v); _ms.Write(b); }

        public void Double(int field, double d) => Fixed64(field, BitConverter.DoubleToUInt64Bits(d));

        public byte[] ToArray() => _ms.ToArray();
    }
}
