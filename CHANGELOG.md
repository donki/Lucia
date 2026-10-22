# Changelog — sOC Lucia

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
