# sOC Lucia

> Hasta la versión 2026.9.20.3 se llamó **sOC AI Chat**; los datos se trasladan solos al primer arranque.

IA privada para Windows: un modelo de lenguaje que se ejecuta en tu PC y con el que hablas desde
una ventana de chat. Nada de lo que escribes sale del ordenador. Con una **puerta local para
editores de código**, VS Code (u otro cliente compatible con la API de OpenAI) puede programar con
esa misma IA.

## Dónde conseguirla

- **Releases de GitHub** (EXE autocontenido, MSIX y la extensión de VS Code): https://github.com/donki/Lucia/releases
- No está en la Microsoft Store.

## Qué hace

- **Chat con una IA local.** El motor es `llama-server` de [llama.cpp](https://github.com/ggml-org/llama.cpp)
  (MIT), que la aplicación descarga la primera vez de sus releases oficiales, comprueba (SHA-256) y
  arranca como proceso hijo en `127.0.0.1` y un puerto libre. Con GPU NVIDIA usa la versión CUDA
  (y si no arranca, cae a Vulkan); en cualquier otra x64, Vulkan; en Windows ARM, CPU.
- **Elegir la IA.** Un catálogo corto de modelos con licencia abierta (GGUF publicados en Hugging
  Face). La aplicación **analiza el PC** (procesador, RAM, gráfica y su memoria de vídeo): solo se
  enseñan los que caben, llevan **★ los óptimos** (caben enteros en la gráfica, o son ligeros para
  el procesador), se avisa de los que irán lentos y se recomienda el mayor de los óptimos. La
  **descarga sigue en segundo plano** con Ajustes cerrado y **se retoma** donde iba si se cierra la
  aplicación a medias. Un **buscador de Hugging Face** encuentra más IA (repositorios GGUF), las pesa y
  las valora para este PC con el mismo criterio. Las IA **instaladas se pueden poner en uso o borrar**. También se puede **importar un GGUF** que ya tengas (se usa donde está, sin
  copiarlo). Cada modelo muestra su licencia antes de descargarse. La **carpeta de los modelos** se
  puede cambiar de sitio (otro disco): al cambiarla se mueven los que ya hay y el activo se apunta
  a su nueva ruta.
- **Conversaciones** guardadas en el PC (`%LOCALAPPDATA%\sOCLucia\threads`), con renombrar y
  borrar en cada fila; al abrir se empieza una nueva. Respuestas en streaming, Markdown (títulos,
  listas, negrita, bloques de código con botón de copiar), botón de parar; cada pregunta se puede
  copiar, editar y reenviar. Encima del redactor, el modo (Preguntas/Agente) y la IA en uso, que se
  cambia entre las instaladas sin pasar por Ajustes.
- **Instrucciones fijas** (el «system prompt»), opción de dejar que el modelo **razone** antes de
  responder (el razonamiento sale plegado), tamaño de letra, español/inglés, tema claro/oscuro
  siguiendo a Windows.
- **Modo agente (cowork) con permisos.** Con «Agente» elegido encima del redactor (por conversación; «Preguntas» es solo responder), la IA puede
  usar el PC a través de herramientas (*tool calling* de llama.cpp): **órdenes de PowerShell**,
  **leer/listar/buscar ficheros**, **escribir ficheros**, **descargar de internet**,
  **portapapeles** y **abrir cosas** con su programa habitual, más los datos básicos del PC. Cada
  acción se muestra con su motivo y se aprueba antes de hacerse —o se deja de preguntar por ese
  recurso en la conversación, o siempre—. En Ajustes › Permisos cada recurso se pone en
  **Preguntar, Siempre o Nunca** (con «Nunca» la IA no ve esa herramienta). Carpeta de trabajo
  configurable, 3 minutos de tope por orden, registro en `logsctions.log`. Hace falta un modelo
  con soporte de herramientas (los del catálogo lo tienen).
- **Bandeja y arranque**: al minimizar se queda en el área de notificación (ajustable) y puede
  **arrancar con Windows** escondida (`--tray`).
- **Editores de código (VS Code).** En Ajustes, una puerta `http://127.0.0.1:41417/v1` compatible
  con la API de OpenAI, protegida con un token y solo en loopback, que reenvía al motor
  (`/v1/models`, `/v1/chat/completions`, `/v1/completions`, `/v1/embeddings`, con streaming; la
  primera petición arranca la IA). Sin indicación del cliente, el «pensamiento» del modelo va
  apagado, para que no se gaste la respuesta razonando. La extensión **sOC Lucia Code**
  (`vscode/`, `.vsix` en cada release) añade a VS Code un chat lateral con «Insertar» que **ve el
  workspace** (la IA lista, lee y busca ficheros y mira el editor activo por sí misma; escribir un
  fichero pide confirmación), acciones sobre la selección (preguntar, explicar, mejorar, tests) y
  un proveedor de modelo para el *Manage models…* de Copilot Chat. Continue, Cline y similares funcionan con la misma dirección y
  token. Detalles en [vscode/README.md](vscode/README.md).

## Privacidad

Las conversaciones y la IA se quedan en el PC. La red se usa solo para descargar el motor y el
modelo que elijas, y solo cuando lo pides. Sin cuenta, sin anuncios, sin rastreadores ni analítica.
La puerta para editores escucha únicamente en `127.0.0.1` y rechaza (`401`) cualquier petición sin
el token que se muestra en Ajustes.

## Estructura

- `App.xaml`: sistema visual sOCratic (paleta índigo, tarjetas, botones con icono); `Services/ThemeManager.cs` sigue al tema de Windows.
- `MainWindow`: conversaciones y chat. `SettingsWindow`: la IA (catálogo, importar), instrucciones, razonamiento, letra, puerta de editores, idioma, diagnóstico. `AboutWindow`: la «Acerca de» del catálogo. `PromptWindow`: los diálogos pequeños.
- `Engine/`: `EnginePin` (versión fijada de llama.cpp y sus SHA), `EngineHost` (descarga, arranque, salud, parada), `ModelCatalog` (catálogo, elección del cuantizado, descarga), `ChatClient` (SSE), `Downloader`.
- `Chat/`: `ThreadStore` (JSON por conversación) y `Markdown` (render a WPF).
- `Editor/EditorDoor.cs`: la puerta para editores (`HttpListener`).
- `Agent/`: `CommandTool` (definición de la herramienta, ejecución con PowerShell y registro) y `CommandConfirmWindow`; el bucle de vueltas vive en `MainWindow.AnswerLoopAsync`.
- `Services/TrayIcon.cs`: icono de bandeja (Shell_NotifyIcon) y `WindowsStartup` (HKCU\Run).
- `Localization/Loc.cs`: todos los textos, es/en.
- `vscode/`: la extensión de VS Code (JavaScript plano, sin compilación).
- `Package/` + `tools/empaquetar-msix.ps1`: el MSIX. `tools/entregar.ps1`: EXE + MSIX + `.vsix` + OneDrive + release.
- `constitution/`: las reglas del catálogo (submódulo).

## Compilar

```powershell
dotnet build -c Debug
dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true   # exe de un solo fichero
.\tools\empaquetar-msix.ps1                                                             # MSIX sin firmar
.\tools\entregar.ps1 -Version 2026.9.20.0 -Mensaje "…"                                 # todo lo anterior + OneDrive + release
```

Requisitos: .NET 10 SDK. Para el MSIX, el SDK de Windows (MakeAppx). En Debug, `sOCLucia.exe --ask "pregunta"` envía esa pregunta al abrir, `--work` (y `--auto`) activa el modo trabajo (sin confirmaciones), `--move-models <carpeta>` mueve la carpeta de modelos y `--settings` abre Ajustes (para probar y capturar; no existe en Release). `--tray` existe también en Release: arranca escondida en la bandeja.

## Licencia

[MIT](LICENSE). Componentes de terceros en [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md).
