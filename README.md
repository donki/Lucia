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
- **Adjuntos**: ficheros de texto, código, .docx o PDF (van dentro de la pregunta) e **imágenes** (botón 📎,
  arrastrar o Ctrl+V). Las imágenes las entiende la IA si tiene **visión**: a las del catálogo que la
  tienen (Gemma 4, Qwen3.5, Ministral 3, Qwen3.8) se les descarga su parte de visión (`mmproj`) al
  adjuntar la primera imagen y el motor arranca con `--mmproj`.
- **Imágenes generadas**: «dibuja…» y la IA genera la imagen en el PC con `stable-diffusion.cpp`
  (`Engine/ImageEngine.cs`, versión fijada con SHA como llama.cpp) y Stable Diffusion 1.5 (GGUF); sale
  en la conversación con abrir y guardar como. Mientras se genera se para llama-server (no caben las
  dos en la gráfica); si la gráfica no puede, CPU. Ajustes › Imágenes instala o quita el motor y el modelo.
- **Instrucciones fijas** (el «system prompt»), la casilla **Pensar** en el chat para dejar que el
  modelo razone antes de responder (el razonamiento sale plegado), tamaño de letra, español/inglés,
  tema claro/oscuro siguiendo a Windows. Una sola instancia: volver a ejecutarla trae la abierta.
- **Herramientas con permisos.** La IA puede usar el PC: **órdenes de PowerShell**,
  **leer/listar/buscar ficheros**, **escribir ficheros**, **buscar y leer en internet** (ajuste
  «acceder a internet», marcado por defecto), **portapapeles** y **abrir cosas**, más los datos del
  PC. En Ajustes › Permisos cada recurso se pone en **Preguntar, Siempre o Nunca**; cada acción se
  muestra con su motivo y se aprueba (una vez, en la conversación o siempre). En **modo preguntas**
  las usa solo si la pregunta lo pide; en **modo agente** resuelve la tarea paso a paso. Registro en
  `logs\actions.log`.
- **Tus documentos.** Una carpeta (`Documentos\Lucia` por defecto) con texto, Markdown, CSV, JSON,
  código, HTML, .docx o **PDF** (texto extraído con PdfPig; un PDF escaneado sin capa de texto se avisa como tal) que la IA tiene en cuenta: con cada pregunta recibe los pasajes que encajan y
  puede listarlos y leerlos; también **genera documentos** ahí (.md, .txt, .html, .csv, **.docx**).
- **Memoria sobre ti.** Frases que la IA guarda cuando le cuentas algo duradero; en Ajustes se ven
  y se borran (o se apaga). Solo en este PC.
- **Tareas programadas.** «Cada mañana a las 9…», «el viernes a las 18:00…», «cada 30 minutos…»: la
  IA las programa y se ejecutan solas como conversaciones ⏰ mientras la aplicación esté abierta
  (también en la bandeja). Desatendidas solo usan los recursos en «Siempre».
- **Bandeja y arranque**: al minimizar se queda en el área de notificación (ajustable) y puede
  **arrancar con Windows** escondida (`--tray`).
- **Editores de código (VS Code).** En Ajustes, una puerta `http://127.0.0.1:41417/v1` compatible
  con la API de OpenAI, protegida con un token y solo en loopback, que reenvía al motor
  (`/v1/models`, `/v1/chat/completions`, `/v1/completions`, `/v1/embeddings`, con streaming; la
  primera petición arranca la IA). Sin indicación del cliente, el «pensamiento» del modelo va
  apagado, para que no se gaste la respuesta razonando. La extensión **sOC Lucia Code**
  (`vscode/`, `.vsix` en cada release) añade a VS Code un chat en la **barra de actividad** (icono
  propio) con lo del chat de escritorio: conversaciones guardadas, Preguntas/Agente, «Pensar»,
  adjuntos (fichero, selección, imágenes pegadas), parar, copiar/editar/reenviar, y en cada bloque de
  código copiar, insertar y guardar. En modo agente **ve y cambia el workspace** (lista, lee y busca
  ficheros, mira el editor activo; escribir un fichero o ejecutar una orden pide confirmación),
  acciones sobre la selección (preguntar, explicar, mejorar, tests) y un proveedor de modelo para el
  *Manage models…* de Copilot Chat, **con herramientas** (sirve en el modo agente de Copilot) e
  **imágenes** cuando la IA cargada tiene visión (la puerta lo dice en `/v1/models › capabilities`). Continue, Cline y similares funcionan con la misma dirección y
  token. Detalles en [vscode/README.md](vscode/README.md).

## Privacidad

Las conversaciones y la IA se quedan en el PC. La red se usa solo para descargar el motor y el
modelo que elijas, y solo cuando lo pides. Sin cuenta, sin anuncios, sin rastreadores ni analítica.
La puerta para editores escucha únicamente en `127.0.0.1` y rechaza (`401`) cualquier petición sin
el token que se muestra en Ajustes.

## Estructura

- `App.xaml`: sistema visual sOCratic (paleta índigo, tarjetas, botones con icono); `Services/ThemeManager.cs` sigue al tema de Windows.
- `MainWindow`: conversaciones y chat. `SettingsWindow`: la IA (catálogo, importar), instrucciones, razonamiento, letra, puerta de editores, idioma, diagnóstico. `AboutWindow`: la «Acerca de» del catálogo. `PromptWindow`: los diálogos pequeños.
- `Engine/`: `EnginePin` (versión fijada de llama.cpp y sus SHA), `EngineHost` (descarga, arranque, salud, parada; `--mmproj` si el modelo tiene visión), `ModelCatalog` (catálogo, elección del cuantizado, descarga; `PickMmprojAsync`/`DownloadMmprojAsync` para la parte de visión), `ChatClient` (SSE; imágenes como partes `image_url`), `Downloader`, `ImageEngine` (stable-diffusion.cpp fijado con SHA, modelo SD 1.5 y generación con `sd-cli`).
- `Services/DocumentIndex.cs`: la carpeta de documentos (trozos por palabras; PDF con PdfPig, .docx a mano).
- `App.xaml.cs`: arranque, instancia única y el gestor global de errores (un fallo inesperado se registra en `logs\engine.log`, se avisa y la aplicación sigue).
- `Chat/`: `ThreadStore` (JSON por conversación) y `Markdown` (render a WPF).
- `Editor/EditorDoor.cs`: la puerta para editores (`HttpListener`); `/v1/models` dice el contexto y si el modelo admite herramientas y visión.
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

## Pruebas

`Lucia.Tests` (xUnit, 2026-10-03): **252 pruebas**, todas pasan; el banco tarda **unos 18 s** (sin
contar la compilación). Cobertura de líneas: **91,5 %** de lo instrumentado (se instrumenta el
ensamblado `sOCLucia` entero) y **91,5 %** sobre toda la app (3152 de 3444 líneas ejecutables de
C#; antes 49,2 % con la misma medida).

Se prueba la lógica (documentos de texto, HTML, RTF, .docx y PDF; herramientas del agente; el
cliente de chat en streaming; la puerta de editores; descargas con reanudación y SHA-256; catálogo
y búsqueda de Hugging Face; tareas; memoria; Markdown; textos es/en) y **las ventanas de verdad**
en un hilo STA: el chat contra un **llama-server de mentira** (`FakeLlama`, un exe que solo se
queda vivo donde `EngineHost` busca el motor) y una **red de mentira** (`Downloader.Http`) que
contesta `/health` y el chat en streaming como llama-server, con razonamiento y llamadas a
herramientas; permisos, tareas desatendidas, adjuntos, visión (se baja su parte y la imagen va al
modelo), Ajustes entero, «Acerca de», avisos, bandeja y el arranque con sus opciones. Lo que se
pide a Windows (abrir, diálogos, portapapeles, registro) pasa por `Services/Desktop.cs`, que en las
pruebas es un doble. Los datos van a una carpeta temporal (`SOC_LUCIA_ROOT`, por eso en Debug),
nunca a los de verdad, y nada sale a la red.

**Cómo se cuenta «toda la app»** (`tools/cobertura-app.py`, General §8.6): todos los `.cs` de la
aplicación salvo `obj/`, `bin/`, generados y el proyecto de pruebas; solo las líneas con
sentencias (fuera llaves sueltas, `using`, declaraciones sin cuerpo, campos sin inicializar,
atributos, interfaces y comentarios). Lo instrumentado cuenta lo que marca coverlet, sin excluir
`CompilerGeneratedAttribute`. Sin cubrir quedan sobre todo el motor de imágenes (stable-diffusion,
que necesita la GPU y 1,7 GB), los diálogos de Windows de la plataforma de verdad, el menú nativo
de la bandeja y el caso de una segunda copia abierta.

```powershell
dotnet build Lucia.Tests\Lucia.Tests.csproj
dotnet test Lucia.Tests\Lucia.Tests.csproj --no-build
# cobertura
dotnet test Lucia.Tests\Lucia.Tests.csproj --no-build --collect:"XPlat Code Coverage" --settings Lucia.Tests\coverage.runsettings --results-directory cov
dotnet tool restore; dotnet tool run reportgenerator -reports:"cov\*\coverage.cobertura.xml" -targetdir:cov\informe -reporttypes:TextSummary
python tools\cobertura-app.py cov --app . --detalle
```

## Licencia

[MIT](LICENSE). Componentes de terceros en [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md).
