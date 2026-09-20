using System.IO;
using System.Text.Json;
using SocLucia.Services;

namespace SocLucia.Chat;

public sealed class StoredMessage
{
    public string Role { get; set; } = "user";
    public string Content { get; set; } = string.Empty;
    /// <summary>Razonamiento del modelo, si lo hubo y se dejo pensar. No se reenvia en turnos siguientes.</summary>
    public string? Reasoning { get; set; }
    /// <summary>Asistente en modo trabajo: las herramientas que pidio (JSON de tool_calls), para que el historial sea coherente.</summary>
    public string? ToolCalls { get; set; }
    /// <summary>Mensaje «tool»: a que llamada responde y que orden se ejecuto (para enseñarla).</summary>
    public string? ToolCallId { get; set; }
    public string? Command { get; set; }
    public DateTimeOffset At { get; set; } = DateTimeOffset.Now;
}

public sealed class ChatThread
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N")[..12];
    public string Title { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.Now;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.Now;
    public List<StoredMessage> Messages { get; set; } = [];
    /// <summary>Modo trabajo: la IA puede ejecutar ordenes en este PC (con confirmacion).</summary>
    public bool WorkMode { get; set; }
    /// <summary>El usuario quito la confirmacion para esta conversacion.</summary>
    public bool AutoApprove { get; set; }
}

/// <summary>Conversaciones en <c>threads\&lt;id&gt;.json</c>, un fichero por conversacion. Solo en este PC.</summary>
public static class ThreadStore
{
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    public static List<ChatThread> LoadAll()
    {
        Paths.Ensure();
        var list = new List<ChatThread>();
        foreach (var file in Directory.EnumerateFiles(Paths.Threads, "*.json"))
        {
            try
            {
                if (JsonSerializer.Deserialize<ChatThread>(File.ReadAllText(file), Json) is { } thread)
                    list.Add(thread);
            }
            catch (Exception) { }
        }
        return list.OrderByDescending(t => t.UpdatedAt).ToList();
    }

    public static void Save(ChatThread thread)
    {
        Paths.Ensure();
        thread.UpdatedAt = DateTimeOffset.Now;
        var path = Path.Combine(Paths.Threads, thread.Id + ".json");
        File.WriteAllText(path + ".tmp", JsonSerializer.Serialize(thread, Json));
        File.Move(path + ".tmp", path, overwrite: true);
    }

    public static void Delete(ChatThread thread)
    {
        var path = Path.Combine(Paths.Threads, thread.Id + ".json");
        if (File.Exists(path))
            File.Delete(path);
    }

    /// <summary>Titulo a partir de la primera pregunta: la primera linea, recortada.</summary>
    public static string TitleFrom(string text)
    {
        var line = text.Trim().Split('\n')[0].Trim();
        return line.Length > 60 ? line[..57].TrimEnd() + "…" : line;
    }
}
