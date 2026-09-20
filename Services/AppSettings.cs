using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace SocAiChat.Services;

/// <summary>Ajustes de la aplicacion, en <c>settings.json</c>. Sin secretos: el token de la puerta de editores es local.</summary>
public sealed class AppSettings
{
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull };

    /// <summary>Ruta completa del GGUF activo (puede estar fuera de la carpeta de modelos si se importo sin copiar).</summary>
    public string? ModelPath { get; set; }
    /// <summary>Nombre visible del modelo («Qwen3.5 4B»).</summary>
    public string? ModelName { get; set; }
    /// <summary>Licencia del modelo, tal como la anuncia su ficha.</summary>
    public string? ModelLicense { get; set; }

    /// <summary>Instrucciones fijas que van con cada conversacion (el «system prompt»).</summary>
    public string Instructions { get; set; } = string.Empty;

    /// <summary>Dejar que el modelo «piense» antes de responder (mas lento, a veces mejor).</summary>
    public bool Thinking { get; set; }

    /// <summary>Tokens de contexto que se piden al motor.</summary>
    public int ContextTokens { get; set; } = 8192;

    /// <summary>Longitud maxima de una respuesta, en tokens.</summary>
    public int MaxAnswerTokens { get; set; } = 2048;

    /// <summary>Idioma de la interfaz: «es», «en» o null (el del sistema).</summary>
    public string? Language { get; set; }

    /// <summary>Tamaño de letra del chat.</summary>
    public double FontSize { get; set; } = 14;

    /// <summary>Puerta local para editores de codigo (VS Code): apagada por defecto.</summary>
    public EditorDoorSettings EditorDoor { get; set; } = new();

    /// <summary>Modo trabajo: carpeta en la que se ejecutan las ordenes (por defecto, Documentos).</summary>
    public string WorkFolder { get; set; } = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);

    /// <summary>Al minimizar, esconderse en el area de notificacion.</summary>
    public bool TrayOnMinimize { get; set; } = true;

    public static AppSettings Current { get; private set; } = new();

    public static void Load()
    {
        try
        {
            if (File.Exists(Paths.SettingsFile))
                Current = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(Paths.SettingsFile), Json) ?? new AppSettings();
        }
        catch (Exception)
        {
            Current = new AppSettings();
        }
        if (string.IsNullOrEmpty(Current.EditorDoor.Token))
        {
            Current.EditorDoor.Token = EditorDoorSettings.NewToken();
            Current.Save();
        }
    }

    public void Save()
    {
        Paths.Ensure();
        var tmp = Paths.SettingsFile + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(this, Json));
        File.Move(tmp, Paths.SettingsFile, overwrite: true);
    }

    public bool HasModel => !string.IsNullOrEmpty(ModelPath) && File.Exists(ModelPath);
}

public sealed class EditorDoorSettings
{
    public const int DefaultPort = 41417;

    public bool Enabled { get; set; }
    public int Port { get; set; } = DefaultPort;
    /// <summary>El token que el editor tiene que mandar (Bearer). Se genera una vez; se puede renovar.</summary>
    public string Token { get; set; } = string.Empty;

    public static string NewToken() => Guid.NewGuid().ToString("N");
}
