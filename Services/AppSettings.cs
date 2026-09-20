using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace SocLucia.Services;

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

    /// <summary>Carpeta de los modelos descargados (null = la de la aplicacion). Los GGUF pesan gigas: se puede llevar a otro disco.</summary>
    public string? ModelsFolder { get; set; }

    /// <summary>Al minimizar, esconderse en el area de notificacion.</summary>
    public bool TrayOnMinimize { get; set; } = true;

    /// <summary>Permiso por recurso del PC (Agent.Resource → Ask/Allow/Deny). Lo que no este apuntado, pregunta.</summary>
    public Dictionary<string, string> Permissions { get; set; } = new();

    public Agent.Permission PermissionFor(Agent.Resource resource)
        => Permissions.TryGetValue(resource.ToString(), out var v) && Enum.TryParse<Agent.Permission>(v, out var p) ? p : Agent.Permission.Ask;

    public void SetPermission(Agent.Resource resource, Agent.Permission permission)
    {
        if (permission == Agent.Permission.Ask) Permissions.Remove(resource.ToString());
        else Permissions[resource.ToString()] = permission.ToString();
    }

    /// <summary>IAs descargadas o importadas, con su nombre visible: los GGUF por si solos no dicen como se llaman.</summary>
    public List<InstalledModel> Installed { get; set; } = [];

    /// <summary>Apunta (o reemplaza) la ficha de un GGUF instalado.</summary>
    public void Remember(InstalledModel model)
    {
        Installed.RemoveAll(i => string.Equals(i.Path, model.Path, StringComparison.OrdinalIgnoreCase));
        Installed.Add(model);
    }

    /// <summary>Olvida la ficha de un GGUF (borrado o desaparecido).</summary>
    public void Forget(string path) => Installed.RemoveAll(i => string.Equals(i.Path, path, StringComparison.OrdinalIgnoreCase));

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

/// <summary>Ficha de un GGUF instalado: nombre visible, ruta y licencia (si se conoce).</summary>
public sealed class InstalledModel
{
    public string Name { get; set; } = string.Empty;
    public string Path { get; set; } = string.Empty;
    public string? License { get; set; }
}
