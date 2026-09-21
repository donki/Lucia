# Changelog — sOC Lucia

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
