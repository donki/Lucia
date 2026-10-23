# Changelog — sOC Lucia

## 2026.10.23.3 — Habilidades y frameworks para IA / Skills and AI frameworks

**Español**

- **Habilidades (skills).** Instrucciones empaquetadas que Lucia carga cuando una tarea encaja (como
  las «skills» de los agentes). Cada una es una carpeta con un `SKILL.md` (cabecera con nombre y
  descripción, y debajo las instrucciones). La IA las ve, carga la que toca (`use_skill`) y puede
  instalar nuevas (`install_skill`) desde una carpeta, un `.zip`, un `SKILL.md` o una dirección de
  internet; solo se aceptan licencias de uso comercial. En Ajustes › una tarjeta para añadirlas,
  verlas, encenderlas o quitarlas.
- **Frameworks para IA, con preparación del entorno.** Un catálogo de frameworks de licencia libre que
  Lucia instala sola: prepara el motor que necesitan (Node.js, Python o uv; lo detecta y, si falta, lo
  instala con winget) y conecta el framework como servidor MCP. La IA dispone de `list_frameworks`,
  `install_framework` y `ensure_runtime`.
- **REA — «Reverse Engineer Anything».** Primer framework del catálogo: ingeniería inversa de binarios
  nativos, ensamblados .NET, APK de Android, apps JavaScript/Electron, firmware y capturas de red, todo
  en el PC, para software que es tuyo o estás autorizado a analizar. Se registra como servidor MCP
  (`npx -y rea-agents@latest mcp`, perfil de esquema compacto) y trae su propia habilidad. Necesita
  **Node.js 22 o más**; rinde mejor con un modelo potente y contexto amplio.
- Banco de pruebas: **549 pruebas**.

**English**

- **Skills.** Packaged instructions Lucia loads when a task matches (like agent skills). Each is a
  folder with a `SKILL.md` (a header with name and description, then the instructions). The AI lists
  them, loads the right one (`use_skill`) and can install new ones (`install_skill`) from a folder, a
  `.zip`, a `SKILL.md` or an internet address; only commercial-use licences are accepted. Settings has
  a card to add, view, enable or remove them.
- **AI frameworks, with environment setup.** A catalogue of free-licence frameworks Lucia sets up by
  itself: it prepares the runtime they need (Node.js, Python or uv; detected and, if missing, installed
  with winget) and connects the framework as an MCP server. The AI gets `list_frameworks`,
  `install_framework` and `ensure_runtime`.
- **REA — "Reverse Engineer Anything".** The first catalogue framework: reverse engineering of native
  binaries, .NET assemblies, Android APKs, JavaScript/Electron apps, firmware and packet captures, all
  on the PC, for software you own or are authorized to examine. It is registered as an MCP server
  (`npx -y rea-agents@latest mcp`, compact schema profile) and ships its own skill. It needs **Node.js
  22+** and works best with a capable model and a large context.
- Test bench: **549 tests**.

## 2026.10.23.2 — Comprimir el contexto para seguir / Compressing the context to keep going

**Español**

- **Al llenarse el contexto, Lucia lo comprime y sigue.** En una conversación larga o una tarea de
  agente con muchas vueltas, cuando se acerca al límite de contexto de la IA, resume con el propio
  modelo los mensajes antiguos y continúa con ese resumen más los últimos mensajes tal cual, sin perder
  el hilo ni chocar con el límite. Lo avisa en el chat y el resumen se guarda con la conversación.
- Se puede desactivar en el control de contexto (junto a «Pensar»): «Comprimir el contexto al llenarse».
- Banco de pruebas: **528 pruebas** y **92,5 %** de toda la aplicación.

**English**

- **When the context fills up, Lucia compresses it and carries on.** In a long conversation or an agent
  task with many rounds, as it nears the AI's context limit it summarises the older messages with the
  model itself and continues with that summary plus the latest messages verbatim, without losing the
  thread or hitting the limit. It says so in the chat and the summary is saved with the conversation.
- It can be turned off in the context control (next to «Think»): «Compress the context when it fills up».
- Test bench: **528 tests** and **92.5 %** of the whole application.

## 2026.10.23.1 — Leer páginas web para investigar / Reading web pages to research

**Español**

- **Lucia lee las páginas, no solo los resúmenes del buscador.** Al investigar, el agente abre con
  `fetch_url` los mejores resultados y los lee, y sigue enlaces dentro de una página cuando llevan a lo
  que busca. Si una fuente falla, prueba otra en vez de rendirse.
- **`fetch_url` mejorado**: va con cabeceras de navegador (muchas webs ya no lo rechazan con 403),
  descomprime gzip/deflate/brotli y saca el título y el texto legible.
- **Repliegue a navegador headless** (como Playwright): si una web bloquea la lectura o solo trae un
  esqueleto que rellena el JavaScript, Lucia la abre con el Edge o Chrome que ya tengas en modo
  headless y lee el resultado. No descarga nada; si no hay navegador, lo dice y sugiere otra fuente.
- Banco de pruebas: **520 pruebas** y **92,7 %** de toda la aplicación.

**English**

- **Lucia reads the pages, not just the search snippets.** When researching, the agent opens the best
  results with `fetch_url` and reads them, and follows a link inside a page when it leads to what it
  needs. If a source fails, it tries another instead of giving up.
- **Improved `fetch_url`**: it sends browser headers (many sites no longer reject it with 403),
  decompresses gzip/deflate/brotli, and pulls the title and readable text.
- **Headless-browser fallback** (like Playwright): if a site blocks the read or only returns a skeleton
  that JavaScript fills in, Lucia opens it with the Edge or Chrome you already have in headless mode and
  reads the result. It downloads nothing; if there is no browser, it says so and suggests another source.
- Test bench: **520 tests** and **92.7 %** of the whole application.

## 2026.10.23.0 — Servidores MCP: Lucia gana herramientas externas / MCP servers: Lucia gains external tools

**Español**

- **MCP (Model Context Protocol).** Lucia se conecta a servidores MCP —los mismos que usan Claude y
  VS Code— para dar al agente herramientas nuevas: la web, ficheros, bases de datos, APIs… En
  Ajustes › Extensiones (MCP) añades un servidor con su orden (p. ej. «uvx mcp-server-fetch» o
  «npx -y @modelcontextprotocol/server-filesystem C:/…»), Lucia lo arranca, descubre sus herramientas
  y el agente ya las usa, con el mismo permiso y registro que las de serie. Muchos servidores se
  instalan solos la primera vez con npx o uvx.
- **La propia IA puede ampliarse**: en modo agente tiene herramientas para **añadir un servidor MCP**
  (`add_mcp_server`) e **instalar un modelo** (`install_model`, del catálogo o de un repositorio GGUF
  de Hugging Face) cuando una tarea lo necesita, siempre con tu permiso (recurso «Extensiones»).
- Probado con un servidor real (mcp-server-fetch por uvx) y con un servidor de pruebas propio.
- Banco de pruebas: **514 pruebas** y **92,8 %** de toda la aplicación.

**English**

- **MCP (Model Context Protocol).** Lucia connects to MCP servers —the same ones Claude and VS Code
  use— to give the agent new tools: the web, files, databases, APIs… In Settings › Extensions (MCP)
  you add a server with its command (e.g. «uvx mcp-server-fetch» or
  «npx -y @modelcontextprotocol/server-filesystem C:/…»), Lucia starts it, discovers its tools and the
  agent uses them, with the same permission and logging as the built-in ones. Many servers install
  themselves the first time with npx or uvx.
- **The AI can extend itself**: in agent mode it has tools to **add an MCP server** (`add_mcp_server`)
  and **install a model** (`install_model`, from the catalogue or a Hugging Face GGUF repo) when a task
  needs it, always with your permission (the «Extensions» resource).
- Tested with a real server (mcp-server-fetch via uvx) and with a fake server of our own.
- Test bench: **514 tests** and **92.8 %** of the whole application.

## 2026.10.22.3 — Contexto ajustable encima del chat / Context adjustable above the chat

**Español**

- **Control de contexto encima del chat** (junto a «Pensar»): muestra el contexto en uso y, al pulsarlo,
  el **mínimo y el máximo** que admite la IA activa en tu PC, con un deslizador para ajustarlo sin pasar
  por Ajustes. El rango cambia al cambiar de IA. El automático sigue estando; moverlo a mano lo apaga.

**English**

- **Context control above the chat** (next to «Think»): shows the context in use and, when clicked, the
  **minimum and maximum** the active AI allows on your PC, with a slider to adjust it without opening
  Settings. The range changes when you switch AI. Automatic is still there; moving it by hand turns it off.

## 2026.10.22.2 — Errores del motor en claro, IA que sí se pueden usar y búsquedas que no se quedan en bucle / Clear engine errors, usable AIs and searches that do not loop

**Español**

- **Errores del motor en claro.** Si una IA no carga, Lucia ya no dice solo «el motor ha terminado con
  el código 1»: explica el motivo (arquitectura que el motor no conoce, fichero modificado o a medias,
  adaptador LoRA en vez de IA, datos que faltan, sin memoria) y qué hacer. La IA queda marcada con ⚠
  en Ajustes hasta que arranque bien, y un fallo del fichero ya no se reintenta inútilmente con Vulkan.
- **Se baja el fichero correcto de cada repositorio.** Con nombres de compresión propios (Q4_K_P,
  APEX…) Lucia cogía el fichero más pequeño, que a veces era un auxiliar (`imatrix`, ayudante
  `FastMTP`, parte de visión, adaptador LoRA) y por eso salía como «óptimo». Ahora reconoce esas
  compresiones, descarta los auxiliares y no instala (ni deja importar) lo que no es una IA de texto.
- **Búsquedas que no se quedan en bucle.** DuckDuckGo estaba devolviendo su página anti-robots y la
  IA buscaba lo mismo con otras palabras sin parar. Ahora, si DuckDuckGo no da nada, se busca en
  Stack Overflow y Wikipedia; y si dos acciones seguidas no dan nada útil (o son búsquedas casi
  iguales) se le pide a la IA otro enfoque, y a la cuarta deja de buscar y contesta con lo que sabe.
- **Acciones sin límite** en el modo agente (Ajustes › Modo trabajo › «Sin límite»): sigue hasta
  acabar o hasta que pulses parar; los frenos de repetir lo que falla y de no avanzar siguen.
- **Borrar todas las conversaciones** con un botón (papelera junto a «+»), con confirmación.
- Banco de pruebas: **497 pruebas** y **92,9 %** de toda la aplicación.

**English**

- **Clear engine errors.** If an AI does not load, Lucia no longer only says «the engine exited with
  code 1»: it explains why (an architecture the engine does not know, a modified or half-downloaded
  file, a LoRA adapter instead of an AI, missing data, out of memory) and what to do. The AI is marked
  with ⚠ in Settings until it starts fine, and a file problem is no longer pointlessly retried on Vulkan.
- **The right file is downloaded from each repository.** With custom quant names (Q4_K_P, APEX…) Lucia
  took the smallest file, sometimes an auxiliary one (`imatrix`, `FastMTP` helper, vision part, LoRA
  adapter), which is why it showed as «optimal». It now knows those quants, skips auxiliary files and
  does not install (or import) anything that is not a text AI.
- **Searches that do not loop.** DuckDuckGo was returning its anti-bot page and the AI kept searching
  the same thing in other words. Now, if DuckDuckGo gives nothing, Stack Overflow and Wikipedia are
  searched; and if two actions in a row give nothing useful (or are almost the same search) the AI is
  asked for another approach, and at the fourth it stops searching and answers with what it knows.
- **Unlimited actions** in agent mode (Settings › Work mode › «No limit»): it goes on until it finishes
  or you press stop; the brakes on repeating failures and on making no progress remain.
- **Delete all conversations** with one button (bin next to «+»), with confirmation.
- Test bench: **497 tests** and **92.9 %** of the whole application.

## 2026.10.22.1 — Varias descargas a la vez, enlaces a ficheros y más Markdown / Several downloads at once, file links and more Markdown

**Español**

- **Varias IA bajando a la vez.** En Ajustes, cada descarga tiene su barra y su botón de cancelar;
  cancelar una no toca las demás (ni su fichero a medias). Al acabar, queda en uso la última que
  pediste. Si cierras Lucia con varias a medias, todas se retoman al abrirla.
- **Borrar IA instaladas mientras se descarga otra**: ya no se bloquean los botones de borrar; solo
  queda bloqueada la que se está bajando.
- **Enlaces a ficheros y carpetas**: en las respuestas, las rutas que existen en tu PC (también con
  espacios, entre comillas, en código o con «:línea» al final) y las direcciones web salen como
  enlaces; un clic las abre. La IA escribe ahora las rutas completas para que funcionen.
- **Más Markdown** en las respuestas: tablas (con alineación), citas, reglas, tachado, listas
  anidadas y de tareas (☐/☑), enlaces `[texto](dirección)` e imágenes como enlace.
- Banco de pruebas: **468 pruebas** y **92,8 %** de toda la aplicación.

**English**

- **Several AIs downloading at once.** In Settings, each download has its own bar and cancel button;
  cancelling one does not touch the others (nor their half-downloaded file). When they finish, the last
  one you asked for is in use. If you close Lucia with several half-way, they all resume when you open it.
- **Delete installed AIs while another downloads**: the delete buttons are no longer locked; only the
  one being downloaded is.
- **Links to files and folders**: in the answers, paths that exist on your PC (also with spaces, in
  quotes, in code or with «:line» at the end) and web addresses are links; one click opens them. The AI
  now writes full paths so they work.
- **More Markdown** in the answers: tables (with alignment), quotes, rules, strikethrough, nested and
  task lists (☐/☑), `[text](address)` links and images as links.
- Test bench: **468 tests** and **92.8 %** of the whole application.

## 2026.10.22.0 — Enseñar a Lucia (fine-tuning guiado), modelos que no son GGUF y filtros en el buscador / Teach Lucia (guided fine-tuning), non-GGUF models and search filters

**Español**

- **Nuevo: Enseñar a Lucia** (Ajustes › Enseñar a Lucia…). Un asistente de cinco pasos para hacer
  fine-tuning sin saber de fine-tuning: eliges la IA, añades ejemplos de preguntas y respuestas (de tus
  conversaciones, de un fichero CSV o JSONL, o escritos a mano; con plantilla CSV), eliges cuánto
  entrenar (Rápido, Equilibrado o A fondo, con el tiempo estimado) y Lucia entrena **en tu PC**. El
  resultado es un complemento pequeño (un adaptador LoRA) que va encima de la IA y que activas,
  desactivas o borras en Ajustes. Lucia dice antes si la IA cabe para entrenar en tu gráfica.
- La primera vez instala sola, sin tocar el Python que puedas tener, las herramientas de entrenamiento
  (unos 4 GB con NVIDIA) y baja el modelo original de la IA. Se pueden quitar desde Ajustes.
- Probado en una RTX 3050 Ti de 4 GB con Qwen3.5 0.8B y 12 ejemplos: unos 80 s de entrenamiento, y
  en el chat responde lo enseñado al pie de la letra.
- **Nuevo: modelos que no son GGUF.** En el buscador, *Formato › GGUF y convertibles* enseña también
  modelos de texto en safetensors; un botón los descarga, los convierte a GGUF y los instala.
- **Nuevo: filtros en el buscador** de Hugging Face: tipo (texto, imagen a texto, texto a imagen,
  voz…), tamaño máximo en parámetros, formato, orden (descargas, valoración, tendencia, recientes) y
  solo licencias que permiten uso comercial.
- Banco de pruebas: **454 pruebas** y **92,5 %** de toda la aplicación.

**English**

- **New: Teach Lucia** (Settings › Teach Lucia…). A five-step assistant to fine-tune without knowing
  about fine-tuning: choose the AI, add examples of questions and answers (from your conversations, a
  CSV or JSONL file, or written by hand; with a CSV template), choose how much to train (Quick,
  Balanced or Deep, with the estimated time) and Lucia trains **on your PC**. The result is a small
  add-on (a LoRA adapter) that goes on top of the AI and that you switch on, off or delete in Settings.
  Lucia says beforehand whether the AI fits for training in your graphics card.
- The first time it installs the training tools by itself (about 4 GB with NVIDIA), without touching
  any Python you may have, and downloads the AI's original model. They can be removed from Settings.
- Tested on an RTX 3050 Ti with 4 GB with Qwen3.5 0.8B and 12 examples: about 80 s of training, and in
  the chat it answers what it was taught word for word.
- **New: models that are not GGUF.** In the search, *Format › GGUF and convertible* also shows text
  models in safetensors; one button downloads them, converts them to GGUF and installs them.
- **New: search filters** for Hugging Face: type (text, image to text, text to image, speech…), maximum
  size in parameters, format, order (downloads, likes, trending, recent) and only licences that allow
  commercial use.
- Test bench: **454 tests** and **92.5 %** of the whole application.

## 2026.10.21.5 — Conversaciones que el modelo rechazaba, y contexto automático / Conversations the model rejected, and automatic context

**Español**

- **Arreglo: «No se ha podido responder: 500… No user query found in messages».** En modo agente, tras
  muchas vueltas de herramienta, la pregunta original se salía de la ventana de mensajes que se le manda
  al modelo, y la plantilla de chat de Qwen3 rechazaba la conversación. Ahora la última pregunta del
  usuario **siempre viaja** con la conversación, así que no vuelve a pasar.
- Si aun así un modelo rechaza la conversación por su plantilla, Lucia ya **no enseña el error técnico**
  (la traza de Jinja): muestra una frase clara («el modelo rechazó la conversación; reformula o empieza un
  chat nuevo»), apunta el detalle en el registro y, si había herramientas, reintenta una vez sin ellas.
- **Nuevo: contexto automático** (Ajustes › Respuestas y contexto, activado por defecto). Lucia elige el
  contexto de los límites de la propia IA: lo más grande que cabe rápido en tu gráfica, dentro de lo que
  la IA admite. Enseña el valor elegido y el rango (mínimo–máximo) de la IA en tu PC. Desactívalo para
  ponerlo a mano, acotado por ese mismo rango. Cambiarlo reinicia la IA en la siguiente pregunta.
- Banco de pruebas: **404 pruebas** y **92,8 %** de toda la aplicación.

**English**

- **Fix: «Could not answer: 500… No user query found in messages».** In agent mode, after many tool
  rounds, the original question fell out of the message window sent to the model, and Qwen3's chat
  template rejected the conversation. Now the user's last question **always travels** with the
  conversation, so it no longer happens.
- If a model still rejects the conversation through its template, Lucia no longer **shows the technical
  error** (the Jinja stack): it shows a clear sentence («the model rejected the conversation; rephrase or
  start a new chat»), records the detail in the log and, if there were tools, retries once without them.
- **New: automatic context** (Settings › Answers and context, on by default). Lucia picks the context
  from the AI's own limits: the biggest that fits fast in your graphics card, within what the AI allows.
  It shows the chosen value and the AI's range (minimum–maximum) on your PC. Turn it off to set it by
  hand, bounded by that same range. Changing it restarts the AI at the next question.
- Test suite: **404 tests** and **92.8 %** of the whole app.

## 2026.10.21.4 — El modo agente no se atasca, y esperas con indicador / The agent no longer gets stuck, and waits with an indicator

**Español**

- **El modo agente ya no se queda en bucle.** Si una acción falla (por ejemplo una orden con un error
  de sintaxis), la IA no la repite igual una y otra vez: una acción idéntica que ya falló no se vuelve
  a ejecutar, y si insiste, Lucia para y lo explica. Hay un **máximo de acciones por respuesta**
  (Ajustes › Modo trabajo, 15 por defecto); al llegar, para y te dice qué intentó. Esto es lo que
  dejaba la ventana en «No responde».
- **Reconoce los callejones sin salida.** Si una orden apunta a un **programa de ventana** (una
  aplicación con interfaz, no de línea de órdenes), leer sus bytes o ejecutarla no da ninguna salida:
  Lucia lo detecta, se lo dice a la IA y esta propone otra vía en vez de repetir.
- **`run_command` sigue pidiendo permiso** en cada orden, y el modo agente sigue apagado hasta que lo
  enciendes: el freno no cambia nada de eso.
- **Indicador de espera en toda la aplicación.** Lo que tarda más de un momento muestra el mismo arco
  que gira de la respuesta, con un texto de lo que hace, y la ventana nunca parece muerta: abrir Ajustes
  (lee la IA y cuenta los documentos por detrás), «Probar la conexión» de MLflow, «Copiar registros»,
  mover la carpeta de modelos, buscar en Hugging Face, instalar el motor de imágenes, arrancar o cargar
  la IA. Los botones que lanzan un proceso se desactivan mientras dura.
- Banco de pruebas: **388 pruebas** y **92,7 %** de toda la aplicación.

**English**

- **The agent no longer loops.** If an action fails (for example a command with a syntax error), the AI
  does not repeat it unchanged over and over: an identical action that already failed is not run again,
  and if it insists, Lucia stops and explains. There is a **limit of actions per answer** (Settings ›
  Work mode, 15 by default); on reaching it, it stops and tells you what it tried. This is what left the
  window «Not responding».
- **It recognises dead ends.** If a command targets a **window program** (a GUI application, not a
  command-line tool), reading its bytes or running it produces no output: Lucia detects it, tells the AI,
  and the AI proposes another way instead of repeating.
- **`run_command` still asks permission** for each command, and agent mode is still off until you turn
  it on: the brake changes none of that.
- **Wait indicator across the whole app.** Anything that takes more than a moment shows the same spinning
  arc as the reply, with text saying what it is doing, and the window never looks dead: opening Settings
  (it reads the AI and counts the documents in the background), MLflow «Test connection», «Copy logs»,
  moving the models folder, searching Hugging Face, installing the image engine, starting or loading the
  AI. Buttons that launch a process are disabled while it runs.
- Test suite: **388 tests** and **92.7 %** of the whole app.

## 2026.10.21.3 — MLflow: cada pregunta como traza en GenAI / MLflow: each question as a GenAI trace

**Español**

- **Ajustes › MLflow** (apagado por defecto): dirección del servidor, experimento (se crea si no
  existe), usuario y contraseña o token (también Databricks), guardados cifrados para tu usuario de
  Windows y con el ojo para verlos, y **Probar la conexión**, que dice en palabras si conecta, por qué
  no, qué versión de MLflow tiene y cómo se van a ver las preguntas.
- **Cada pregunta es una traza** en la sección GenAI del experimento (pestaña *Traces*): el modelo y
  los ajustes, el tiempo hasta el primer trozo, los tokens de entrada y salida, tokens por segundo, el
  motor, los documentos usados (sus nombres), cada llamada al modelo y cada herramienta, y si acabó
  bien, se canceló o falló (con el mensaje). Las preguntas de los editores de código también. Las
  congelaciones de la ventana van como traza propia y el registro de diagnóstico, en una ejecución de
  la sesión. Con un MLflow 2.x van como métricas de esa ejecución, y la prueba de conexión lo avisa.
- **Tus textos no salen** salvo que marques «Enviar también el texto de preguntas y respuestas»
  (apagado y con aviso); sin él, las trazas solo muestran tiempos, tokens y errores. Nueva
  [política de privacidad](PRIVACY.md).
- Nunca frena a Lucia: se envía por detrás, con cola, reintentos y un solo aviso si el servidor no
  responde.
- Banco de pruebas: **370 pruebas** y **92,8 %** de toda la aplicación (antes 338 y 91,3 %).

**English**

- **Settings › MLflow** (off by default): server address, experiment (created if missing), user and
  password or token (Databricks too), stored encrypted for your Windows user and with the eye to reveal
  them, and **Test connection**, which says in words whether it connects, why not, which MLflow version
  it has and how questions will show up.
- **Each question is a trace** in the experiment's GenAI section (*Traces* tab): model and settings,
  time to the first piece, input and output tokens, tokens per second, the engine, the documents used
  (their names), each model call and each tool, and whether it ended well, was cancelled or failed
  (with the message). Code editor requests too. Window freezes are their own trace and the diagnostics
  log goes into a session run. With MLflow 2.x they go as metrics of that run, and the connection test
  says so.
- **Your texts do not leave** unless you tick «Also send the text of questions and answers» (off, with a
  warning); without it, traces only show times, tokens and errors. New [privacy policy](PRIVACY.md).
- It never slows Lucia down: sent in the background, with a queue, retries and a single notice if the
  server does not answer.
- Test suite: **370 tests** and **92.8 %** of the whole app (was 338 and 91.3 %).

## 2026.10.21.2 — Ajustes ya no deja la ventana bloqueada / Settings no longer leaves the window locked

**Español**

- **Arreglo**: Ajustes (y los avisos y permisos) se podía minimizar y, como no sale en la barra de
  tareas, desaparecía mientras la ventana principal seguía bloqueada esperándolo: parecía que Lucia
  se había congelado. Ahora los diálogos no tienen botón de minimizar; si se minimizan igualmente
  (Win+D), se minimiza la aplicación entera y, al volver desde el icono junto al reloj, el diálogo
  sale delante.
- Si aun así la ventana se queda bloqueada por un diálogo que no se ve, Lucia lo nota en unos
  segundos, lo apunta en el registro y vuelve a enseñar el diálogo.
- El registro de diagnóstico apunta también cada ventana que se abre y se cierra (y cuánto estuvo
  abierta), y el del motor lleva la fecha en cada línea.
- Los paquetes llevan la versión bien escrita en el nombre (`sOCLucia-2026.10.21.2.msix`,
  `soc-lucia-code-2026.10.21.2.vsix`); antes salía `2026.10.211`.
- Banco de pruebas: **338 pruebas**; cobertura de toda la aplicación **91,3 %** (antes 92,2 %): baja
  porque ya está dentro la parte de MLflow que se verá en Ajustes en la próxima versión, que llega con
  sus pruebas de ventana.

**English**

- **Fix**: Settings (and prompts and permissions) could be minimized and, as it has no taskbar button,
  it vanished while the main window stayed locked waiting for it: Lucia looked frozen. Dialogs no
  longer have a minimize button; if they are minimized anyway (Win+D), the whole app is minimized and,
  when you come back from the icon next to the clock, the dialog is in front.
- If the window is still left locked by a dialog that cannot be seen, Lucia notices within seconds,
  records it in the log and shows the dialog again.
- The diagnostics log also records each window that opens and closes (and how long it was open), and
  the engine log has the date on every line.
- Packages carry the version properly in their name (`sOCLucia-2026.10.21.2.msix`,
  `soc-lucia-code-2026.10.21.2.vsix`); before it came out as `2026.10.211`.
- Test suite: **338 tests**; whole-app coverage **91.3 %** (was 92.2 %): it drops because the MLflow
  part that will show in Settings in the next version is already in, and it comes with its window tests.

## 2026.10.21.1 — Longitud de las respuestas y del contexto, y registros para cuando se congela / Answer and context length, and logs for when it freezes

**Español**

- **Ajustes › Respuestas y contexto** (nuevo):
  - **Tokens máximos por respuesta**: lo más largo que escribe de una vez (antes fijo en 2048). Entre
    256 y 32 768, por defecto 2048; con «Pensar», el triple.
  - **Contexto**: cuánto texto tiene en cuenta a la vez (antes fijo en 8192). Entre 2048 y lo que
    admiten la IA y el PC: Lucia lee de la propia IA con cuánto contexto se entrenó y cuánta memoria
    gasta cada token, y lo compara con la memoria de la gráfica (dice también hasta dónde va rápido).
    Al cambiarlo, la IA se reinicia en la siguiente pregunta.
  - Se aplican al salir de la casilla o con Intro; un valor que no vale se avisa con el rango y no se guarda.
  - **Editores de código** (la puerta 127.0.0.1:41417): si el editor no pide su propio máximo se usa el
    de Ajustes (antes no había ninguno y la IA podía escribir hasta llenar el contexto); si lo pide, se
    respeta, recortado al contexto.
- **Registros para cuando se congela**: `logs\diagnostics.log` apunta, con hora y duración, cada
  pregunta (motor listo, pregunta preparada, primer trozo, fin, cancelación, error), el arranque y la
  parada del motor y la carga del modelo, las herramientas y la puerta de editores. Nunca el texto de
  las conversaciones ni datos personales (la carpeta del usuario se tapa). Rota a 1 MB y guarda cuatro
  anteriores.
- **Vigilante de congelaciones**: si la ventana deja de responder más de 2 s, se apunta qué estaba
  haciendo y qué operación se había quedado ejecutándose, y luego cuánto duró.
- **Ajustes › Diagnóstico**: «Copiar registros» (un zip con los registros y un resumen del PC y de la
  configuración, listo para pegar con Ctrl+V en un correo o un chat) y «Carpeta de registros».
- **Arreglo**: en Ajustes › Permisos salían las etiquetas «Perm_Free» y «PermHint_Free». Era la fila de
  lo que nunca pide permiso (memoria, documentos de Lucia, tareas, datos del PC, imágenes); ahora se
  explica así y ya no ofrece elegir, porque elegir no cambiaba nada. Si algún día falta un texto, sale
  vacío en vez de la clave interna.
- Banco de pruebas: **310 pruebas** y **92,2 %** de toda la aplicación (antes 278 y 91,8 %).

**English**

- **Settings › Answers and context** (new):
  - **Maximum tokens per answer**: the longest it writes in one go (was fixed at 2048). Between 256
    and 32,768, default 2048; with «Think», three times as much.
  - **Context**: how much text it takes into account at once (was fixed at 8192). Between 2048 and what
    the AI and the PC allow: Lucia reads from the AI itself the context it was trained with and how
    much memory each token uses, and compares it with the graphics memory (it also says up to where it
    stays fast). Changing it restarts the AI at the next question.
  - They apply when you leave the box or press Enter; a value that is not valid is reported with the
    range and not saved.
  - **Code editors** (the 127.0.0.1:41417 door): if the editor does not ask for its own maximum, the one
    in Settings is used (before there was none and the AI could write until the context was full); if it
    does, it is respected, capped to the context.
- **Logs for when it freezes**: `logs\diagnostics.log` records, with times and durations, each question
  (engine ready, question prepared, first piece, end, cancellation, error), engine start and stop, model
  loading, tools and the editor door. Never the text of the conversations or personal data (the user
  folder is masked). It rotates at 1 MB and keeps four previous files.
- **Freeze watchdog**: if the window stops responding for more than 2 s, it records what it was doing
  and which operation was stuck running, and then how long it lasted.
- **Settings › Diagnostics**: «Copy logs» (a zip with the logs and a summary of the PC and the settings,
  ready to paste with Ctrl+V into an email or a chat) and «Logs folder».
- **Fix**: Settings › Permissions showed the labels «Perm_Free» and «PermHint_Free». It was the row for
  what never asks (memory, Lucia's documents, tasks, PC details, pictures); it now says so and no longer
  offers a choice, since choosing changed nothing. If a text is ever missing, it shows empty instead of
  the internal key.
- Test suite: **310 tests** and **92.2 %** of the whole app (was 278 and 91.8 %).

## 2026.10.21.0 — Lucia ya no se queda bloqueada al preguntar / Lucia no longer freezes when you ask

**Español**

- Al enviar una pregunta sale **al momento** en el chat, la casilla se vacía y debajo aparece la
  respuesta con un **indicador de espera** que gira y dice por dónde va: «Cargando la IA…» (si el
  modelo aún se está cargando), «Preparando la pregunta…», «Lucia está pensando…» o «Razonando…»,
  con los segundos que lleva. En cuanto llega el primer trozo, la respuesta se va escribiendo.
- **La ventana ya no se congela** mientras Lucia piensa o escribe: se puede mover, desplazar, cambiar
  de conversación y seguir escribiendo. Antes, con documentos en la carpeta de Lucia (sobre todo
  PDF), cada pregunta los volvía a leer en el hilo de la ventana y la dejaba parada unos segundos
  antes de enseñar nada; y una respuesta larga y rápida saturaba la ventana. Ahora leer los
  documentos y los adjuntos, arrancar el motor, recibir la respuesta y ejecutar las herramientas
  (leer ficheros, buscar, internet) va por detrás, y la respuesta se repinta a un ritmo que se
  adapta a su tamaño.
- El botón de parar sigue funcionando también mientras espera: corta la respuesta y deja lo que ya
  había escrito.
- Banco de pruebas: **278 pruebas** (antes 252) y **91,8 %** de toda la aplicación (antes 91,5 %).
  De paso se han arreglado tres pruebas que fallaban a veces según el orden en que se ejecutaban.

**English**

- When you send a question it shows up in the chat **right away**, the box is cleared and the answer
  appears below with a spinning **waiting indicator** that says what is going on: "Loading the AI…"
  (if the model is still loading), "Getting the question ready…", "Lucia is thinking…" or
  "Reasoning…", with the seconds so far. As soon as the first piece arrives, the answer is written.
- **The window no longer freezes** while Lucia thinks or writes: you can move it, scroll, switch
  conversations and keep typing. Before, with documents in Lucia's folder (PDFs above all), every
  question read them again on the window's thread and stalled it for a few seconds before showing
  anything; and a long, fast answer flooded the window. Reading documents and attachments, starting
  the engine, receiving the answer and running tools (reading files, searching, the internet) now
  happen in the background, and the answer is repainted at a pace that adapts to its size.
- The stop button still works while waiting: it cuts the answer and keeps what was already written.
- Test suite: **278 tests** (was 252) and **91.8 %** of the whole app (was 91.5 %). Three tests that
  sometimes failed depending on the order they ran in are fixed too.

## 2026.10.16.0 — Menú de la bandeja / Tray menu

**Español**

- El menú del icono junto al reloj tiene el mismo aspecto que en el resto de aplicaciones sOC, con
  iconos, en tema claro y oscuro (General 6.14).

**English**

- The menu of the icon next to the clock looks the same as in the other sOC apps, with icons, in
  light and dark themes (General 6.14).

## 2026.10.3.0 — Pruebas de las ventanas / Window tests

**Español**

- El banco de pruebas maneja ahora también las ventanas de verdad (chat, Ajustes, «Acerca de»,
  permisos, avisos, bandeja y el arranque) con un llama-server de mentira y una red de mentira que
  contesta como él: preguntar, pensar, parar, herramientas con permisos, tareas programadas,
  adjuntos, visión, conversaciones, buscar e instalar IA, carpetas, memoria y puerta de editores.
  **252 pruebas** y **91,5 %** de cobertura sobre toda la aplicación (antes 216 y 49,2 % con la
  misma medida).
- Lo que las ventanas piden a Windows (abrir con su programa, elegir ficheros y carpetas,
  portapapeles, «Arrancar con Windows») pasa por una pieza que en las pruebas se cambia por un doble.
- Una ventana cerrada deja de escuchar los cambios de idioma, de motor y de descargas, y su
  temporizador de tareas se para.
- Sin cambios en lo que se ve ni en cómo se usa.

**English**

- The test suite now also drives the real windows (chat, Settings, About, permissions, prompts,
  tray and startup) against a fake llama-server and a fake network that answers like it: asking,
  thinking, stopping, tools with permissions, scheduled tasks, attachments, vision, conversations,
  searching and installing AIs, folders, memory and the editor door. **252 tests** and **91.5 %**
  line coverage of the whole app (was 216 and 49.2 % measured the same way).
- What the windows ask of Windows (open with its program, pick files and folders, clipboard, start
  with Windows) goes through one piece that tests replace with a double.
- A closed window stops listening to language, engine and download changes, and its task timer stops.
- Nothing changes in what you see or how you use it.

## 2026.9.30.0 — Pruebas automáticas y cinco arreglos que han salido con ellas

- **Pruebas automáticas** (`Lucia.Tests`, 216): documentos y PDF, herramientas, streaming, puerta
  de editores, descargas, catálogo, tareas, memoria, Markdown y textos. Ver el README.
- **La respuesta ya no se corta** si el motor manda un trozo final sin `choices` (solo el uso de
  tokens), como hacen los servidores compatibles con OpenAI: antes salía «Index was out of range».
- **Los errores del motor con la forma `{"error":"texto"}`** se enseñan como tales; antes se
  convertían en un error interno que tapaba el mensaje.
- **Tareas semanales en castellano con tilde** («weekly miércoles 18:00», «sábado»): antes se
  rechazaban. Y una hora imposible («daily 24:00», «9:60») se rechaza en vez de guardarse rara.
- **La parte de visión** se baja en F16 aunque el repositorio liste antes la BF16.
- **Mover los modelos** a la misma carpeta escrita con barra final ya no dice que «está dentro».

## 2026.9.27.0 — PDF en tus documentos, Copilot Chat con herramientas e imágenes, y los errores ya no cierran la aplicación

- **PDF en la carpeta de documentos** (y como adjunto, y con `read_file`): el texto se saca con
  [PdfPig](https://github.com/UglyToad/PdfPig) (Apache 2.0), página a página y en orden de lectura.
  Un PDF escaneado, sin capa de texto, se dice como tal para que la IA no se invente lo que pone.
  Hasta 64 MB y 1000 páginas por fichero. Probado con un libro de 4,5 MB: «según mis documentos PDF,
  ¿qué es un lakehouse?» lo lee y responde citando el fichero.
- **Copilot Chat con herramientas e imágenes.** El proveedor de modelo de la extensión de VS Code
  declara `toolCalling` (ya se puede elegir en el modo agente de Copilot: las llamadas y sus
  resultados van al modelo como `tool_calls` y mensajes `tool`) e `imageInput` cuando la IA cargada
  tiene visión. La puerta (`/v1/models`) dice ahora el contexto y si el modelo admite herramientas y
  visión; el tamaño de entrada que ve Copilot sale del contexto de Ajustes. Probado con Qwen3.5 4B:
  pide la herramienta, contesta con su resultado y lee una imagen.
- **Un error inesperado ya no cierra Lucia**: se apunta con su traza en `logs\engine.log`, sale un
  aviso en tu idioma y la aplicación sigue (también los errores de tareas en segundo plano).

## 2026.9.22.1 — Arreglo: la respuesta tras generar una imagen; y los botones se notan al pulsarlos

- **«No se ha podido responder: … 127.0.0.1:puerto»** después de dibujar. Generar una imagen mata el
  motor de texto (no caben los dos en la gráfica) y al volver escucha en **otro puerto**, pero la
  conversación seguía hablando con el viejo. Ahora, en cada vuelta de la respuesta, se vuelve a pedir
  la dirección del motor: si sigue en pie no cuesta nada y, si lo mató la imagen, lo levanta otra vez
  y la respuesta continúa. Probado: dibujo + frase en la misma conversación, sin error.
- **Los botones acusan el clic**: los de icono (copiar, editar, reenviar, los de la lista de
  conversaciones, los de la barra), los de contorno y los de estado se oscurecen y encogen un poco
  mientras están pulsados. Antes solo cambiaban al pasar por encima y no parecía que se hiciera clic.

## 2026.9.22.0 — «Arrancar con Windows» siempre apunta a la aplicación que se usa

- **Ventana negra al arrancar con Windows.** La entrada de arranque se quedó apuntando a una
  compilación de pruebas vieja (`bin\Debug`), que abría una ventana negra y parecía colgada
  «cargando la IA»; la aplicación de OneDrive cargaba Qwen3.5 4B en menos de 30 s. Ahora, al
  arrancar, si «Arrancar con Windows» está puesto, la entrada pasa a apuntar al exe que se está
  ejecutando (las compilaciones Debug no la tocan). Corregida a mano la entrada de Josep.

## 2026.9.21.0 — Imágenes generadas, adjuntos y visión, «Pensar» en el chat y VS Code en la barra lateral

- **Imágenes en el chat**: «dibuja un faro al atardecer» y la IA la genera en este PC con
  [stable-diffusion.cpp](https://github.com/leejet/stable-diffusion.cpp) (MIT, versión fijada como
  llama.cpp) y **Stable Diffusion 1.5** (GGUF Q8_0, 1,7 GB; licencia CreativeML OpenRAIL-M). Se
  descargan la primera vez (o desde Ajustes › Imágenes). La imagen sale en la conversación con
  **abrir** y **guardar como**; quedan en `images\`. Mientras se genera, la IA del chat se pausa (no
  caben las dos en la memoria de la gráfica) y se retoma en la siguiente pregunta; si la gráfica no
  puede, se genera en la CPU. Herramienta `generate_image` en los dos modos.
- **Adjuntar ficheros e imágenes**: botón 📎, arrastrar al redactor o **Ctrl+V** (imágenes o ficheros
  del portapapeles). Los ficheros de texto/código/.docx van dentro de la pregunta; las imágenes van al
  modelo como tales si tiene **visión**: para las IA del catálogo que la tienen (Gemma 4, Qwen3.5,
  Ministral 3, Qwen3.8) se ofrece descargar su parte de visión (mmproj) al adjuntar la primera
  imagen, y el motor arranca con ella (`--mmproj`). Los adjuntos se ven en la burbuja (miniatura o
  nombre, con abrir) y se guardan con la conversación.
- **«Pensar» en el chat**: la casilla de razonar antes de responder está junto al modo y la IA, no en
  Ajustes.
- **Instancia única**: si Lucia ya está abierta (aunque esté en la bandeja), volver a ejecutarla la
  trae al frente en vez de no hacer nada.
- **Extensión de VS Code 2026.9.210**: el chat vive en la **barra de actividad** (icono propio a la
  izquierda, como los demás complementos) y tiene lo del chat de escritorio: conversaciones guardadas,
  modos Preguntas/Agente, «Pensar» (razonamiento plegado), adjuntos (fichero abierto, del workspace o
  del disco, la selección, imágenes pegadas), parar, copiar/editar/reenviar la pregunta, y en cada
  bloque de código copiar, insertar y guardar como fichero. En modo agente, además de leer el
  workspace, puede **escribir ficheros** y **ejecutar órdenes** (PowerShell en la carpeta; siempre pide).

## 2026.9.20.8 — Documentos, memoria, tareas programadas y más manos libres

- **Internet como ajuste** (Ajustes › Instrucciones, marcado por defecto): la IA puede **buscar en la
  web** (`web_search`, DuckDuckGo sin cuenta) y **leer páginas** en los dos modos cuando la pregunta
  lo necesita; sin marcar, trabaja sin conexión. Deja de aparecer en Permisos.
- **Tus documentos** (Ajustes): carpeta (por defecto `Documentos\Lucia`) cuyo contenido la IA tiene en
  cuenta: texto, Markdown, CSV, JSON, código, HTML y Word (.docx). Con cada pregunta recibe los pasajes
  que encajan con ella y puede listarlos y leerlos enteros. **Genera documentos** ahí mismo
  (`save_document`, .md/.txt/.html/.csv y **.docx**), con botón para abrirlos desde la conversación.
- **Lo que la IA sabe de ti** (Ajustes): frases que guarda cuando le cuentas algo duradero
  (`remember`), se ven todas, se borran una a una o todas, y se puede apagar. Solo en `memory.json`.
- **Tareas programadas**: pídeselo en el chat («cada mañana a las 9…», «el viernes a las 18:00…»,
  «cada 30 minutos…») y se ejecutan solas como conversaciones ⏰ mientras la aplicación esté abierta
  (también en la bandeja, con globo al acabar). En Ajustes se ven, se paran y se borran. Las
  ejecuciones desatendidas solo usan los recursos en «Siempre».
- **Modo preguntas con manos**: también puede usar documentos, internet, ficheros y órdenes cuando la
  pregunta lo pide (pidiendo permiso); el modo agente cambia la consigna (resolver la tarea paso a
  paso), no el alcance.
- **Escribir mientras responde**: se pueden enviar más preguntas durante una respuesta; quedan en la
  conversación y se contestan a continuación. Parar sigue al lado de Enviar.
- **Texto copiable**: todo el texto del chat (preguntas, respuestas, razonamiento) se puede seleccionar
  y copiar; los bloques de código tienen copiar y **guardar como fichero** (con la extensión del lenguaje).
- **Buscador de Hugging Face**: muestra **todos** los resultados (óptimos primero, luego por descargas;
  los que no caben, marcados), entiende **palabras clave** (imágenes, vídeo, voz, transcribir, traducir,
  visión, música…) y, sin la casilla «solo GGUF», enseña modelos de cualquier tipo con su tipo y enlace
  (no se instalan aquí: la aplicación solo ejecuta modelos de texto GGUF).
- **Borrar IA** también desde la fila del catálogo (papelera cuando ya está instalada); «Instaladas en
  este PC» sube arriba del catálogo.
- Bandeja: minimizar desde la barra de tareas o con Win+D también esconde en la bandeja.

## 2026.9.20.7 — Descargas que se retoman y buscador de Hugging Face

- **Las descargas se retoman.** Si cierras la aplicación (o se corta la red) con una IA a medias, al
  volver a abrirla sigue **desde donde iba** (petición *Range* sobre el `.part`; si el servidor no lo
  admite, empieza de cero). Cancelar a mano sí tira lo descargado.
- **Buscador de IA en Hugging Face** en Ajustes › La IA: busca repositorios GGUF por nombre o
  familia (llama, mistral, gemma, qwen, deepseek…), **pesa cada resultado** (elige el cuantizado
  habitual Q4_K_M) y lo valora **para este PC** igual que el catálogo: ★ óptima, «funciona pero más
  lenta», «lenta»; las que no caben en memoria no se muestran. Orden: óptimas primero, luego las más
  descargadas. Se instalan con el mismo botón que las del catálogo (avisa si el repositorio exige
  aceptar una licencia).

## 2026.9.20.6 — Modo y modelo a mano en el chat

- Encima del redactor, un selector **Preguntas / Agente** por conversación (antes era un botón de
  terminal sin texto): en *Preguntas* la IA solo responde; en *Agente* usa el PC con permisos.
- Al lado, un desplegable con las **IA instaladas** para cambiar de modelo sin pasar por Ajustes.
- Al abrir la aplicación se empieza con una **conversación nueva**; las anteriores siguen en la lista.
- Ajustes: «Tamaño de la letra del chat» con el valor en px y una explicación (solo cambia el texto de
  la conversación).

## 2026.9.20.5 — Permisos por recurso y VS Code con contexto

- **La IA puede usar el PC pidiendo permiso.** En modo trabajo, además de órdenes de PowerShell,
  ahora tiene herramientas para **leer ficheros, listar y buscar carpetas, escribir ficheros,
  descargar de internet, leer y escribir el portapapeles y abrir cosas** (fichero, carpeta,
  programa o web), más los datos básicos del PC. Cada acción se enseña antes con su motivo y se
  aprueba; se puede dejar de preguntar por ese recurso en la conversación o siempre.
- **Ajustes › Modo trabajo › Permisos**: por cada recurso (órdenes, leer ficheros, escribir
  ficheros, internet, portapapeles, abrir cosas) se elige **Preguntar, Siempre o Nunca**; con
  «Nunca» la IA ni siquiera ve la herramienta. Registro de todo en `logsctions.log`.
- **Extensión de VS Code con contexto del proyecto**: en el chat lateral la IA puede **listar,
  leer y buscar en los ficheros del workspace y ver el editor activo** por sí misma (con cada paso
  visible), así «analiza la carpeta» funciona sin pegar código; **escribir un fichero pide
  confirmación**. Se apaga con `socLucia.workspaceTools`.
- El atajo del chat en VS Code pasa a **Ctrl+Alt+L** (Ctrl+Alt+I lo usa Copilot Chat y ganaba).

## 2026.9.20.4 — Ahora se llama Lucia

- La aplicación pasa a llamarse **sOC Lucia** (antes sOC AI Chat): exe `sOCLucia.exe`, repositorio
  `donki/Lucia`, extensión «sOC Lucia Code». Los datos se trasladan solos de `%LOCALAPPDATA%\sOCAIChat`
  a `%LOCALAPPDATA%\sOCLucia` al primer arranque (con la versión vieja cerrada) y la entrada de
  «Arrancar con Windows» se renueva.
- **Análisis del PC** en Ajustes › La IA: procesador, RAM y gráfica con su memoria de vídeo. Las IA
  del catálogo llevan **★ si son óptimas para este PC** (caben enteras en la gráfica o son ligeras
  para el procesador) y se avisa de las que irán más lentas; la recomendada es la mayor de las óptimas.
- **Descargas en segundo plano**: la descarga de una IA sigue con Ajustes cerrado; el progreso se ve
  en la barra de estado y al acabar queda activa.
- **Borrar IA descargadas**: lista «Instaladas en este PC» con poner en uso y borrar (los GGUF pesan gigas).
- Las **preguntas** de la conversación se pueden **copiar, editar** (vuelven al redactor) **y reenviar**.
- En la lista de conversaciones, **renombrar y borrar** son botones a la derecha de cada fila (sin menú contextual).
- Idiomas con **banderas dibujadas** (Windows no pinta los emoji de bandera: salían «ES» y «US»).
- Arreglo: con el pensamiento activado a veces salía el razonamiento **sin respuesta** (se agotaba el
  tope de tokens razonando). Ahora el razonamiento tiene el triple de sitio y, si aun así no llega a
  contestar, se repite la vuelta sin pensamiento y la respuesta sale igualmente.

## 2026.9.20.3 — Carpeta de modelos en otro sitio

- En Ajustes › La IA, **la carpeta de los modelos se puede cambiar** (por ejemplo, a otro disco con
  espacio). Al cambiarla se **mueven los GGUF que ya hay** con progreso (renombrado en el mismo
  disco; copia y borrado entre discos, sin dejar nada a medias), el modelo activo pasa a su nueva
  ruta y el motor se para mientras tanto. Por defecto sigue en `%LOCALAPPDATA%\sOCLucia\models`.

## 2026.9.20.2 — Modo trabajo, bandeja y arranque con Windows

- **Modo trabajo** (botón de terminal junto a Enviar, por conversación): la IA puede **ejecutar
  órdenes de PowerShell en este PC** para mirar ficheros, compilar, pasar tests, usar git… Cada
  orden se enseña con el motivo que da el modelo y se aprueba una a una (o «no volver a preguntar
  en esta conversación»); la salida vuelve a la IA, que sigue hasta contestar (hasta 12 vueltas).
  Carpeta de trabajo en Ajustes; tiempo máximo de 3 minutos por orden; registro de todo lo
  ejecutado en `logs\commands.log`. Necesita un modelo con soporte de herramientas (Qwen, Ministral,
  Gemma 4…).
- **Área de notificación**: al minimizar se esconde y queda el icono (clic para volver; botón
  derecho, Abrir o Salir). Ajustable.
- **Arrancar con Windows** (Ajustes › Windows): entrada en el registro del usuario con `--tray`,
  que arranca escondida en la bandeja.

## 2026.9.20.1 — El motor muere con la aplicación

- `llama-server` va en un «job» de Windows con cierre forzoso: si la aplicación se cierra a lo
  bruto (Administrador de tareas, cuelgue), el motor se va con ella y no quedan gigas de memoria
  ocupados. Al arrancar, además, se limpia cualquier motor huérfano de una sesión anterior.

## 2026.9.20.0 — Primera versión

- **Chat con una IA local**: llama.cpp (`llama-server`, MIT) descargado y comprobado la primera
  vez (CUDA en NVIDIA con caída a Vulkan; Vulkan en x64; CPU en ARM), arrancado como proceso hijo
  en loopback. Respuestas en streaming con Markdown y botón de parar; conversaciones guardadas en
  el PC con renombrar y borrar.
- **La IA**: catálogo corto de modelos abiertos (solo los que caben en la memoria del PC, con el
  mayor recomendado), descarga con progreso y cancelación, o importar un GGUF propio sin copiarlo.
- **Ajustes**: instrucciones fijas, razonamiento del modelo opcional (plegado en la respuesta),
  tamaño de letra, español/inglés, diagnóstico (registro del motor, carpeta de datos).
- **Editores de código (VS Code)**: puerta local `http://127.0.0.1:41417/v1` compatible con la
  API de OpenAI, con token, que reenvía al motor (streaming incluido) y arranca la IA si hace
  falta. Extensión **sOC Lucia Code** para VS Code (chat lateral con «Insertar», acciones sobre
  la selección, proveedor de modelo para Copilot Chat); también sirve para Continue, Cline y
  cualquier cliente compatible.
- «Acerca de» del catálogo: contacto, idioma, privacidad, licencia MIT y aviso legal.
