# Changelog — sOC Lucia

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
