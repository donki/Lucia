using System.Text;

namespace SocLucia.Tests;

/// <summary>Un tramo OTLP leido: lo que mira MLflow al recibirlo.</summary>
public sealed record OtlpSpan(string TraceId, string SpanId, string? ParentId, string Name, ulong StartNanos, ulong EndNanos,
    Dictionary<string, object?> Attributes, List<(string Name, Dictionary<string, object?> Attributes)> Events, int StatusCode, string? StatusMessage)
{
    public string? Type => Attributes.GetValueOrDefault("mlflow.spanType") as string;
}

/// <summary>
/// Lector minimo de protobuf para comprobar lo que Lucia manda a /v1/traces (ExportTraceServiceRequest de
/// opentelemetry-proto): independiente del codificador de la aplicacion, para que un error en uno no lo tape el otro.
/// </summary>
internal static class OtlpReader
{
    private sealed class Reader(byte[] data)
    {
        private int _pos;
        public bool More => _pos < data.Length;

        public ulong Varint()
        {
            ulong v = 0;
            for (var shift = 0; ; shift += 7)
            {
                if (shift > 63) throw new FormatException("varint largo");
                var b = data[_pos++];
                v |= (ulong)(b & 0x7F) << shift;
                if (b < 0x80) return v;
            }
        }

        public (int Field, int Wire) Tag() { var t = Varint(); return ((int)(t >> 3), (int)(t & 7)); }
        public byte[] Bytes() { var n = (int)Varint(); var b = data[_pos..(_pos + n)]; _pos += n; return b; }
        public ulong Fixed64() { var v = BitConverter.ToUInt64(data, _pos); _pos += 8; return v; }

        public void Skip(int wire)
        {
            switch (wire)
            {
                case 0: Varint(); break;
                case 1: _pos += 8; break;
                case 2: Bytes(); break;
                case 5: _pos += 4; break;
                default: throw new FormatException("tipo de cable " + wire);
            }
        }
    }

    private static IEnumerable<(int Field, int Wire, Reader R)> Fields(byte[] message)
    {
        var r = new Reader(message);
        while (r.More)
        {
            var (field, wire) = r.Tag();
            yield return (field, wire, r);
        }
    }

    public static List<OtlpSpan> Spans(byte[] request)
    {
        var spans = new List<OtlpSpan>();
        foreach (var (f, w, r) in Fields(request))
        {
            if (f != 1 || w != 2) { r.Skip(w); continue; }
            foreach (var (f2, w2, r2) in Fields(r.Bytes()))   // ResourceSpans
            {
                if (f2 != 2 || w2 != 2) { r2.Skip(w2); continue; }
                foreach (var (f3, w3, r3) in Fields(r2.Bytes()))   // ScopeSpans
                {
                    if (f3 != 2 || w3 != 2) { r3.Skip(w3); continue; }
                    spans.Add(Span(r3.Bytes()));
                }
            }
        }
        return spans;
    }

    private static OtlpSpan Span(byte[] bytes)
    {
        string trace = "", id = "", name = "";
        string? parent = null, statusMessage = null;
        ulong start = 0, end = 0;
        var status = 0;
        var attributes = new Dictionary<string, object?>();
        var events = new List<(string, Dictionary<string, object?>)>();
        foreach (var (f, w, r) in Fields(bytes))
        {
            switch (f)
            {
                case 1: trace = Convert.ToHexString(r.Bytes()).ToLowerInvariant(); break;
                case 2: id = Convert.ToHexString(r.Bytes()).ToLowerInvariant(); break;
                case 4: parent = Convert.ToHexString(r.Bytes()).ToLowerInvariant(); break;
                case 5: name = Encoding.UTF8.GetString(r.Bytes()); break;
                case 7: start = r.Fixed64(); break;
                case 8: end = r.Fixed64(); break;
                case 9: { var (k, v) = KeyValue(r.Bytes()); attributes[k] = v; break; }
                case 11:
                {
                    var eventName = "";
                    var eventAttributes = new Dictionary<string, object?>();
                    foreach (var (ef, ew, er) in Fields(r.Bytes()))
                    {
                        if (ef == 2) eventName = Encoding.UTF8.GetString(er.Bytes());
                        else if (ef == 3) { var (k, v) = KeyValue(er.Bytes()); eventAttributes[k] = v; }
                        else er.Skip(ew);
                    }
                    events.Add((eventName, eventAttributes));
                    break;
                }
                case 15:
                    foreach (var (sf, sw, sr) in Fields(r.Bytes()))
                    {
                        if (sf == 2) statusMessage = Encoding.UTF8.GetString(sr.Bytes());
                        else if (sf == 3) status = (int)sr.Varint();
                        else sr.Skip(sw);
                    }
                    break;
                default: r.Skip(w); break;
            }
        }
        return new OtlpSpan(trace, id, parent, name, start, end, attributes, events, status, statusMessage);
    }

    private static (string Key, object? Value) KeyValue(byte[] bytes)
    {
        string key = "";
        object? value = null;
        foreach (var (f, w, r) in Fields(bytes))
        {
            if (f == 1) key = Encoding.UTF8.GetString(r.Bytes());
            else if (f == 2) value = AnyValue(r.Bytes());
            else r.Skip(w);
        }
        return (key, value);
    }

    private static object? AnyValue(byte[] bytes)
    {
        object? value = null;
        foreach (var (f, w, r) in Fields(bytes))
        {
            value = f switch
            {
                1 => Encoding.UTF8.GetString(r.Bytes()),
                2 => r.Varint() != 0,
                3 => unchecked((long)r.Varint()),
                4 => BitConverter.UInt64BitsToDouble(r.Fixed64()),
                5 => Fields(r.Bytes()).Where(x => x.Field == 1).Select(x => AnyValue(x.R.Bytes())).ToList(),
                6 => Fields(r.Bytes()).Where(x => x.Field == 1).Select(x => KeyValue(x.R.Bytes())).ToDictionary(x => x.Key, x => x.Value),
                _ => SkipValue(r, w),
            };
        }
        return value;
    }

    private static object? SkipValue(Reader r, int w) { r.Skip(w); return null; }
}
