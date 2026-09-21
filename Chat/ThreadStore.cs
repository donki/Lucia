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
    /// <summary>Ficheros e imagenes que acompañan a la pregunta (copiados a <c>threads\&lt;id&gt;\</c>).</summary>
    public List<Attachment>? Attachments { get; set; }
}

/// <summary>Un adjunto de una pregunta: la copia que guarda la aplicacion y si es una imagen (va al modelo como tal) o un texto (va dentro de la pregunta).</summary>
public sealed class Attachment
{
    public string Name { get; set; } = string.Empty;
    public string Path { get; set; } = string.Empty;
    public bool IsImage { get; set; }
    public long Bytes { get; set; }
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
    /// <summary>Recursos (Agent.Resource) que el usuario dejo de confirmar en esta conversacion.</summary>
    public List<string> Approved { get; set; } = [];
    /// <summary>Ejecucion desatendida (tarea programada): las herramientas que piden permiso se rechazan.</summary>
    [System.Text.Json.Serialization.JsonIgnore] public bool Unattended { get; set; }
    /// <summary>Id de la tarea programada que la creo, si es el caso.</summary>
    public string? TaskId { get; set; }
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
        try { if (Directory.Exists(FilesFolder(thread))) Directory.Delete(FilesFolder(thread), recursive: true); } catch (Exception) { }
    }

    /// <summary>Carpeta de los adjuntos de una conversacion.</summary>
    public static string FilesFolder(ChatThread thread) => Path.Combine(Paths.Threads, thread.Id);

    private static readonly HashSet<string> ImageExtensions = new(StringComparer.OrdinalIgnoreCase) { ".png", ".jpg", ".jpeg", ".gif", ".bmp", ".webp" };

    /// <summary>Copia un fichero a la carpeta de la conversacion y devuelve su ficha.</summary>
    public static Attachment Attach(ChatThread thread, string sourcePath)
    {
        Directory.CreateDirectory(FilesFolder(thread));
        var name = Path.GetFileName(sourcePath);
        var target = Path.Combine(FilesFolder(thread), $"{DateTime.Now:HHmmssfff}-{name}");
        File.Copy(sourcePath, target, overwrite: true);
        return new Attachment { Name = name, Path = target, IsImage = ImageExtensions.Contains(Path.GetExtension(name)), Bytes = new FileInfo(target).Length };
    }

    /// <summary>Guarda una imagen pegada del portapapeles como PNG en la conversacion.</summary>
    public static Attachment AttachImage(ChatThread thread, byte[] png)
    {
        Directory.CreateDirectory(FilesFolder(thread));
        var target = Path.Combine(FilesFolder(thread), $"{DateTime.Now:HHmmssfff}-pegado.png");
        File.WriteAllBytes(target, png);
        return new Attachment { Name = "imagen.png", Path = target, IsImage = true, Bytes = png.Length };
    }

    public static bool IsImageFile(string path) => ImageExtensions.Contains(Path.GetExtension(path));

    /// <summary>Titulo a partir de la primera pregunta: la primera linea, recortada.</summary>
    public static string TitleFrom(string text)
    {
        var line = text.Trim().Split('\n')[0].Trim();
        return line.Length > 60 ? line[..57].TrimEnd() + "…" : line;
    }
}
