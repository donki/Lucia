using System.IO;
using System.Text;

namespace SocLucia.Engine;

/// <summary>
/// Lo que dice la cabecera de un GGUF sobre la memoria de la conversacion: el contexto con el que se
/// entreno y lo que ocupa cada token en la cache (capas, cabezas KV y su tamaño). Solo se lee la
/// cabecera (los metadatos), nunca los pesos; los arrays largos (el vocabulario) se saltan.
/// </summary>
public sealed record GgufInfo(string Architecture, int? ContextLength, int? Layers, int? HeadsKv, int? KeyLength, int? ValueLength, int? FullAttentionInterval, string? Type = null)
{
    /// <summary>Bytes de cache (K y V en F16) por token de contexto; null si la cabecera no lo dice.</summary>
    public long? KvBytesPerToken
    {
        get
        {
            if (Layers is not > 0 || HeadsKv is not > 0 || KeyLength is not > 0) return null;
            // En los modelos hibridos solo una de cada N capas es de atencion completa (las demas no guardan K y V por token).
            var layers = FullAttentionInterval is > 1 ? (Layers.Value + FullAttentionInterval.Value - 1) / FullAttentionInterval.Value : Layers.Value;
            return (long)layers * HeadsKv.Value * (KeyLength.Value + (ValueLength ?? KeyLength.Value)) * 2;
        }
    }

    private static readonly Dictionary<string, (DateTime Written, long Length, GgufInfo? Info)> Cache = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>La cabecera del fichero (guardada mientras no cambie); null si no es un GGUF legible.</summary>
    public static GgufInfo? Read(string? path)
    {
        if (string.IsNullOrEmpty(path) || !File.Exists(path)) return null;
        var file = new FileInfo(path);
        lock (Cache)
            if (Cache.TryGetValue(path, out var hit) && hit.Written == file.LastWriteTimeUtc && hit.Length == file.Length)
                return hit.Info;
        GgufInfo? info;
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 1 << 16);
            info = Parse(stream);
        }
        catch (Exception)
        {
            info = null;
        }
        lock (Cache)
            Cache[path] = (file.LastWriteTimeUtc, file.Length, info);
        return info;
    }

    /// <summary>Lee los metadatos de un GGUF v2/v3; null si no lo es o esta roto.</summary>
    public static GgufInfo? Parse(Stream stream)
    {
        try
        {
            using var r = new BinaryReader(stream, Encoding.UTF8, leaveOpen: true);
            if (r.ReadUInt32() != 0x46554747) return null;   // "GGUF"
            var version = r.ReadUInt32();
            if (version < 2) return null;
            r.ReadUInt64();   // tensores
            var count = r.ReadUInt64();
            if (count > 100_000) return null;
            var values = new Dictionary<string, object?>(StringComparer.Ordinal);
            for (ulong i = 0; i < count; i++)
            {
                var name = ReadString(r);
                var type = r.ReadUInt32();
                values[name] = ReadValue(r, type);
            }
            if (values.GetValueOrDefault("general.architecture") is not string arch || arch.Length == 0) return null;
            int? Int(string name) => values.GetValueOrDefault($"{arch}.{name}") switch
            {
                long l when l is > 0 and <= int.MaxValue => (int)l,
                long[] list when list.Length > 0 => (int)Math.Clamp(list.Max(), 0, int.MaxValue),   // por capa: la mayor
                _ => null,
            };
            var heads = Int("attention.head_count");
            var embedding = Int("embedding_length");
            var key = Int("attention.key_length") ?? (heads is > 0 && embedding is > 0 ? embedding / heads : null);
            return new GgufInfo(arch, Int("context_length"), Int("block_count"), Int("attention.head_count_kv") ?? heads, key,
                Int("attention.value_length") ?? key, Int("full_attention_interval"), values.GetValueOrDefault("general.type") as string);
        }
        catch (Exception ex) when (ex is EndOfStreamException or IOException or InvalidDataException or OverflowException)
        {
            return null;
        }
    }

    private static string ReadString(BinaryReader r)
    {
        var length = r.ReadUInt64();
        if (length > 1 << 20) throw new InvalidDataException("cadena demasiado larga");
        return Encoding.UTF8.GetString(r.ReadBytes((int)length));
    }

    /// <summary>Los enteros se devuelven como long; los arrays de enteros, como long[]; lo demas que no hace falta, se salta.</summary>
    private static object? ReadValue(BinaryReader r, uint type)
    {
        switch (type)
        {
            case 0: return (long)r.ReadByte();
            case 1: return (long)r.ReadSByte();
            case 2: return (long)r.ReadUInt16();
            case 3: return (long)r.ReadInt16();
            case 4: return (long)r.ReadUInt32();
            case 5: return (long)r.ReadInt32();
            case 6: r.ReadSingle(); return null;
            case 7: r.ReadByte(); return null;
            case 8: return ReadString(r);
            case 10: return checked((long)r.ReadUInt64());
            case 11: return r.ReadInt64();
            case 12: r.ReadDouble(); return null;
            case 9:
                var inner = r.ReadUInt32();
                var n = r.ReadUInt64();
                var size = inner switch { 0 or 1 or 7 => 1, 2 or 3 => 2, 4 or 5 or 6 => 4, 10 or 11 or 12 => 8, _ => 0 };
                if (inner is <= 5 or 10 or 11 && n <= 4096)
                {
                    var list = new long[n];
                    for (ulong i = 0; i < n; i++) list[i] = (long)ReadValue(r, inner)!;
                    return list;
                }
                if (size > 0)
                {
                    r.BaseStream.Seek(checked((long)n * size), SeekOrigin.Current);
                    return null;
                }
                for (ulong i = 0; i < n; i++)
                    ReadValue(r, inner);   // cadenas (el vocabulario) o arrays anidados: una a una
                return null;
            default:
                throw new InvalidDataException($"tipo GGUF desconocido {type}");
        }
    }
}
