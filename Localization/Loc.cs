using System.Globalization;
using System.Windows.Markup;

namespace SocAiChat.Localization;

/// <summary>
/// Textos de la aplicacion en español e ingles (constitucion, seccion 7). Ningun texto va en el
/// XAML ni en el codigo: todo pasa por aqui.
/// </summary>
public static class Loc
{
    private static readonly Dictionary<string, string> English = new()
    {
        ["AppTitle"] = "sOC AI Chat",
        ["AboutTitle"] = "About",
        ["AboutTooltip"] = "About",
        ["AboutDescription"] = "A private AI that runs on this PC. Ask anything: the answer is generated here, and nothing leaves your computer.",
        ["Publisher"] = "Socratic",
        ["Contact"] = "Contact",
        ["WriteAuthor"] = "Write to the author",
        ["ContactHint"] = "Suggestions, bugs and ideas: all of it gets read.",
        ["LanguageTitle"] = "Language",
        ["LanguageHint"] = "The language applies right away.",
        ["Privacy"] = "Privacy",
        ["PrivacyText"] = "Conversations and the AI stay on this PC, in your user profile; the AI runs here and answers here. The network is used only to download the engine (llama.cpp) and the AI you choose from Hugging Face, and only when you ask for it. No account, no ads, no trackers, no analytics. The optional door for code editors listens on this computer only and needs a token.",
        ["License"] = "License",
        ["LicenseText"] = "Free software under the MIT licence. The engine is llama.cpp (MIT), downloaded at first use. Each AI carries its own licence, shown before the download.",
        ["LicenseLine"] = "MIT License · Copyright © 2026 Socratic",
        ["LegalTitle"] = "Legal notice",
        ["LegalText1"] = "This software is provided \"as is\", without warranty of any kind, express or implied. AI answers can be wrong: check them before acting on them.",
        ["LegalText2"] = "In no event shall the authors be liable for any claim, damages or other liability arising from the use of this software. By using it, the user releases the developer from all liability.",
        ["WarningText"] = "⚠️ Use at your own risk",
        ["Close"] = "Close",
        ["LanguageTooltip"] = "Español / English",

        ["NewChat"] = "New conversation",
        ["Conversations"] = "Conversations",
        ["DeleteChat"] = "Delete conversation",
        ["DeleteChatConfirm"] = "Delete this conversation? This cannot be undone.",
        ["RenameChat"] = "Rename",
        ["RenamePrompt"] = "Title of the conversation",
        ["ComposerHint"] = "Ask something… (Enter sends, Shift+Enter new line)",
        ["Send"] = "Send",
        ["Stop"] = "Stop",
        ["Copy"] = "Copy",
        ["Copied"] = "Copied",
        ["Thinking"] = "Reasoning",
        ["Settings"] = "Settings",
        ["EmptyTitle"] = "Ask the AI on this PC",
        ["EmptyHint"] = "Nothing you write leaves this computer. The first answer after opening takes a while: the AI is loading into memory.",
        ["EngineNoModel"] = "No AI installed yet",
        ["EngineDownloading"] = "Downloading the engine…",
        ["EngineStarting"] = "Loading the AI into memory…",
        ["EngineReady"] = "AI ready",
        ["EngineStopped"] = "AI stopped",
        ["EngineError"] = "The AI could not start",
        ["NoModelYet"] = "No AI installed yet. Choose one in Settings.",
        ["EngineExited"] = "The engine exited with code {0}. See the engine log in Settings.",
        ["EngineTimeout"] = "The engine did not answer in time. See the engine log in Settings.",
        ["AnswerFailed"] = "Could not answer: {0}",

        // Ajustes
        ["SettingsTitle"] = "Settings",
        ["ModelTitle"] = "The AI",
        ["ModelNone"] = "No AI installed. Pick one below, or import a GGUF file you already have.",
        ["ModelCurrent"] = "Installed: {0}",
        ["ModelLicenseLine"] = "Licence: {0}",
        ["ModelFile"] = "File: {0}",
        ["ModelRam"] = "This PC has {0} of memory. Only the AIs that fit are shown.",
        ["ModelRecommended"] = "Recommended",
        ["ModelInstall"] = "Install",
        ["ModelInstalling"] = "Downloading {0}… {1}",
        ["ModelImport"] = "Import a GGUF file…",
        ["ModelImportHint"] = "Any llama.cpp GGUF (one file, no shards). It is used in place: it is not copied.",
        ["ModelCancel"] = "Cancel download",
        ["ModelInstalled"] = "Installed. The next question will use it.",
        ["ModelSize"] = "{0} download",
        ["CatalogBig"] = "The most capable one. Needs a PC with plenty of memory.",
        ["CatalogMid"] = "Good answers and a manageable download.",
        ["CatalogSmall"] = "Fast, many languages, fits an 8 GB PC.",
        ["CatalogTiny"] = "Small file, short answers.",
        ["InstructionsTitle"] = "Standing instructions",
        ["InstructionsHint"] = "Sent with every conversation: tone, language, what the AI should never do.",
        ["InstructionsPlaceholder"] = "For example: answer in Spanish, briefly, and say when you are not sure.",
        ["ThinkingTitle"] = "Let the AI reason before answering",
        ["ThinkingHint"] = "Slower; sometimes better on hard questions. The reasoning is shown folded above the answer.",
        ["FontSizeTitle"] = "Text size",
        ["DoorTitle"] = "Code editors (VS Code)",
        ["DoorHint"] = "Lets a code editor on this PC program with this AI. Opens a local door (this computer only), compatible with the OpenAI API and protected with a token. Nothing leaves the PC.",
        ["DoorEnable"] = "Turn on the door for editors",
        ["DoorUrl"] = "Address",
        ["DoorToken"] = "Token",
        ["DoorPort"] = "Port",
        ["DoorNewToken"] = "New token",
        ["DoorListening"] = "Listening.",
        ["DoorStopped"] = "Stopped.",
        ["DoorHow"] = "In VS Code: install the extension from the release (\"sOC AI Chat Code\", .vsix) and paste the token in its settings; or point Continue, Cline or any OpenAI-compatible client at this address and token.",
        ["DoorBadToken"] = "Missing or wrong token. Copy it from sOC AI Chat > Settings > Code editors.",
        ["DiagnosticsTitle"] = "Diagnostics",
        ["OpenEngineLog"] = "Open the engine log",
        ["OpenDataFolder"] = "Open the data folder",
        ["Accelerator"] = "Engine: llama.cpp {0} · {1}",
        ["Save"] = "Save",
        ["Saved"] = "Saved",
        ["Ok"] = "OK",
        ["Cancel"] = "Cancel",
        ["Yes"] = "Yes",
        ["No"] = "No",
        ["Error"] = "Error",
    };

    private static readonly Dictionary<string, string> Spanish = new()
    {
        ["AppTitle"] = "sOC AI Chat",
        ["AboutTitle"] = "Acerca de",
        ["AboutTooltip"] = "Acerca de",
        ["AboutDescription"] = "Una IA privada que se ejecuta en este PC. Pregunta lo que quieras: la respuesta se genera aquí y nada sale de tu ordenador.",
        ["Publisher"] = "Socratic",
        ["Contact"] = "Contacto",
        ["WriteAuthor"] = "Escribir al autor",
        ["ContactHint"] = "Sugerencias, errores e ideas: se lee todo.",
        ["LanguageTitle"] = "Idioma",
        ["LanguageHint"] = "El idioma se aplica de inmediato.",
        ["Privacy"] = "Privacidad",
        ["PrivacyText"] = "Las conversaciones y la IA se quedan en este PC, en tu perfil de usuario; la IA se ejecuta aquí y responde aquí. La red solo se usa para descargar el motor (llama.cpp) y la IA que elijas de Hugging Face, y solo cuando lo pides. Sin cuenta, sin anuncios, sin rastreadores ni analítica. La puerta opcional para editores de código solo escucha en este equipo y exige un token.",
        ["License"] = "Licencia",
        ["LicenseText"] = "Software libre bajo licencia MIT. El motor es llama.cpp (MIT), que se descarga al primer uso. Cada IA trae su propia licencia, que se muestra antes de descargarla.",
        ["LicenseLine"] = "MIT License · Copyright © 2026 Socratic",
        ["LegalTitle"] = "Aviso legal",
        ["LegalText1"] = "Este software se entrega «tal cual», sin garantías de ningún tipo, expresas o implícitas. Las respuestas de una IA pueden ser erróneas: compruébalas antes de actuar.",
        ["LegalText2"] = "En ningún caso los autores serán responsables de reclamaciones, daños u otras responsabilidades derivadas del uso de este software. Al usarlo, el usuario exime al desarrollador de toda responsabilidad.",
        ["WarningText"] = "⚠️ Uso bajo su propio riesgo",
        ["Close"] = "Cerrar",
        ["LanguageTooltip"] = "Español / English",

        ["NewChat"] = "Nueva conversación",
        ["Conversations"] = "Conversaciones",
        ["DeleteChat"] = "Borrar conversación",
        ["DeleteChatConfirm"] = "¿Borrar esta conversación? No se puede deshacer.",
        ["RenameChat"] = "Renombrar",
        ["RenamePrompt"] = "Título de la conversación",
        ["ComposerHint"] = "Pregunta algo… (Enter envía, Mayús+Enter salto de línea)",
        ["Send"] = "Enviar",
        ["Stop"] = "Parar",
        ["Copy"] = "Copiar",
        ["Copied"] = "Copiado",
        ["Thinking"] = "Razonamiento",
        ["Settings"] = "Ajustes",
        ["EmptyTitle"] = "Pregunta a la IA de este PC",
        ["EmptyHint"] = "Nada de lo que escribas sale de este ordenador. La primera respuesta tras abrir tarda un poco: la IA se está cargando en memoria.",
        ["EngineNoModel"] = "Sin IA instalada",
        ["EngineDownloading"] = "Descargando el motor…",
        ["EngineStarting"] = "Cargando la IA en memoria…",
        ["EngineReady"] = "IA lista",
        ["EngineStopped"] = "IA parada",
        ["EngineError"] = "La IA no ha podido arrancar",
        ["NoModelYet"] = "Todavía no hay ninguna IA instalada. Elige una en Ajustes.",
        ["EngineExited"] = "El motor ha terminado con el código {0}. Mira el registro del motor en Ajustes.",
        ["EngineTimeout"] = "El motor no ha respondido a tiempo. Mira el registro del motor en Ajustes.",
        ["AnswerFailed"] = "No se ha podido responder: {0}",

        ["SettingsTitle"] = "Ajustes",
        ["ModelTitle"] = "La IA",
        ["ModelNone"] = "No hay ninguna IA instalada. Elige una de abajo o importa un fichero GGUF que ya tengas.",
        ["ModelCurrent"] = "Instalada: {0}",
        ["ModelLicenseLine"] = "Licencia: {0}",
        ["ModelFile"] = "Fichero: {0}",
        ["ModelRam"] = "Este PC tiene {0} de memoria. Solo se muestran las IA que caben.",
        ["ModelRecommended"] = "Recomendada",
        ["ModelInstall"] = "Instalar",
        ["ModelInstalling"] = "Descargando {0}… {1}",
        ["ModelImport"] = "Importar un fichero GGUF…",
        ["ModelImportHint"] = "Cualquier GGUF de llama.cpp (un solo fichero, sin trozos). Se usa donde está: no se copia.",
        ["ModelCancel"] = "Cancelar la descarga",
        ["ModelInstalled"] = "Instalada. La siguiente pregunta ya la usará.",
        ["ModelSize"] = "{0} de descarga",
        ["CatalogBig"] = "La más capaz. Necesita un PC con mucha memoria.",
        ["CatalogMid"] = "Buenas respuestas con una descarga razonable.",
        ["CatalogSmall"] = "Rápida, muchos idiomas, cabe en un PC de 8 GB.",
        ["CatalogTiny"] = "Fichero pequeño, respuestas cortas.",
        ["InstructionsTitle"] = "Instrucciones fijas",
        ["InstructionsHint"] = "Van con cada conversación: tono, idioma, lo que la IA nunca debe hacer.",
        ["InstructionsPlaceholder"] = "Por ejemplo: responde en español, breve, y di cuándo no estás seguro.",
        ["ThinkingTitle"] = "Dejar que la IA razone antes de responder",
        ["ThinkingHint"] = "Más lento; a veces mejor en preguntas difíciles. El razonamiento sale plegado encima de la respuesta.",
        ["FontSizeTitle"] = "Tamaño del texto",
        ["DoorTitle"] = "Editores de código (VS Code)",
        ["DoorHint"] = "Deja que un editor de código de este PC programe con esta IA. Abre una puerta local (solo en este equipo), compatible con la API de OpenAI y protegida con un token. Nada sale del PC.",
        ["DoorEnable"] = "Activar la puerta para editores",
        ["DoorUrl"] = "Dirección",
        ["DoorToken"] = "Token",
        ["DoorPort"] = "Puerto",
        ["DoorNewToken"] = "Nuevo token",
        ["DoorListening"] = "Escuchando.",
        ["DoorStopped"] = "Parada.",
        ["DoorHow"] = "En VS Code: instala la extensión de la release («sOC AI Chat Code», .vsix) y pega el token en sus ajustes; o configura Continue, Cline o cualquier cliente compatible con OpenAI con esta dirección y este token.",
        ["DoorBadToken"] = "Falta el token o no es el correcto. Cópialo de sOC AI Chat › Ajustes › Editores de código.",
        ["DiagnosticsTitle"] = "Diagnóstico",
        ["OpenEngineLog"] = "Abrir el registro del motor",
        ["OpenDataFolder"] = "Abrir la carpeta de datos",
        ["Accelerator"] = "Motor: llama.cpp {0} · {1}",
        ["Save"] = "Guardar",
        ["Saved"] = "Guardado",
        ["Ok"] = "Aceptar",
        ["Cancel"] = "Cancelar",
        ["Yes"] = "Sí",
        ["No"] = "No",
        ["Error"] = "Error",
    };

    public static string Language { get; private set; } =
        CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "es" ? "es" : "en";

    public static event Action? LanguageChanged;

    public static void Use(string language)
    {
        if (language is not ("es" or "en") || Language == language)
            return;
        Language = language;
        LanguageChanged?.Invoke();
    }

    public static string Get(string key)
    {
        var table = Language == "es" ? Spanish : English;
        return table.TryGetValue(key, out var value) ? value
            : English.TryGetValue(key, out var fallback) ? fallback
            : key;
    }

    public static string Format(string key, params object[] args) =>
        string.Format(CultureInfo.CurrentCulture, Get(key), args);
}

/// <summary><c>{loc:T Clave}</c> en el XAML.</summary>
[MarkupExtensionReturnType(typeof(string))]
public sealed class TExtension : MarkupExtension
{
    public TExtension()
    {
    }

    public TExtension(string key) => Key = key;

    [ConstructorArgument("key")]
    public string Key { get; set; } = string.Empty;

    public override object ProvideValue(IServiceProvider serviceProvider) => Loc.Get(Key);
}
