# Privacidad — sOC Lucia

*Última actualización: 2026-10-21 (versión 2026.10.21.3). [English below](#privacy--soc-lucia).*

## Qué se queda en tu PC

Todo lo que escribes y lo que contesta la IA se queda en este PC, en tu perfil de usuario
(`%LOCALAPPDATA%\sOCLucia`): conversaciones, ajustes, memoria, tareas, registros y los modelos. La
IA se ejecuta aquí (llama.cpp) y contesta aquí. Sin cuenta, sin anuncios, sin rastreadores ni
analítica.

## Qué sale del PC, y solo si lo pides

- **Descargas**: el motor (llama.cpp, de GitHub), las IA que eliges (de Hugging Face) y el motor de
  imágenes, solo cuando lo pides.
- **Internet para la IA**: si dejas activado «acceder a internet», la IA puede buscar (DuckDuckGo) y
  leer páginas cuando la pregunta lo necesita.
- **Puerta para editores** (apagada por defecto): escucha solo en este PC (127.0.0.1) y pide un token.
- **MLflow** (apagado por defecto): si lo activas en Ajustes › MLflow, Lucia manda **al servidor MLflow
  que tú pongas** (y a ningún otro sitio):
  - una **traza por pregunta** con el modelo y los ajustes con que contesta (temperatura, tope de
    tokens, contexto), tiempos (hasta el primer trozo, total), tokens de entrada y salida, tokens por
    segundo, las herramientas usadas (su nombre y si acabaron bien), los nombres de los documentos de
    tu carpeta que se usaron y el resultado (bien, cancelada o error con su mensaje);
  - las **congelaciones** de la ventana y el **registro de diagnóstico** (qué hacía la aplicación y
    cuánto tardó, sin tus textos y con la carpeta de usuario tapada);
  - un id aleatorio de la conversación y otro de esta instalación, para agrupar (no dicen nada de ti).
  - **No manda el texto** de tus preguntas, de las respuestas, de los documentos ni de lo que hacen
    las herramientas, salvo que marques aparte «Enviar también el texto de preguntas y respuestas»
    (también apagado, y con aviso): entonces ese texto se guarda en el servidor y lo puede leer quien
    tenga acceso a él.
  - La contraseña y el token de MLflow se guardan **cifrados para tu usuario de Windows** (DPAPI) y
    solo viajan en la cabecera de autenticación hacia ese servidor; nunca en claro ni en los registros.

## Registros

`logs\diagnostics.log` y `logs\engine.log` se quedan en tu PC (rotan solos). «Copiar registros» en
Ajustes › Diagnóstico hace un zip para que **tú** lo mandes si quieres; no se envía solo.

---

# Privacy — sOC Lucia

*Last updated: 2026-10-21 (version 2026.10.21.3).*

## What stays on your PC

Everything you write and everything the AI answers stays on this PC, in your user profile
(`%LOCALAPPDATA%\sOCLucia`): conversations, settings, memory, tasks, logs and models. The AI runs here
(llama.cpp) and answers here. No account, no ads, no trackers, no analytics.

## What leaves the PC, and only if you ask for it

- **Downloads**: the engine (llama.cpp, from GitHub), the AIs you choose (from Hugging Face) and the
  image engine, only when you ask for them.
- **Internet for the AI**: if «access the internet» stays on, the AI can search (DuckDuckGo) and read
  pages when the question needs it.
- **Editor door** (off by default): listens only on this PC (127.0.0.1) and requires a token.
- **MLflow** (off by default): if you turn it on in Settings › MLflow, Lucia sends **to the MLflow
  server you enter** (and nowhere else):
  - **one trace per question** with the model and settings it answers with (temperature, token limit,
    context), times (to the first piece, total), input and output tokens, tokens per second, the tools
    used (their name and whether they finished well), the names of the documents from your folder that
    were used and the outcome (ok, cancelled, or error with its message);
  - window **freezes** and the **diagnostics log** (what the app was doing and how long it took, without
    your texts and with the user folder masked);
  - a random conversation id and a random id for this installation, to group them (they say nothing
    about you).
  - **It does not send the text** of your questions, the answers, the documents or what the tools do,
    unless you separately tick «Also send the text of questions and answers» (also off, with a
    warning): then that text is stored on the server and whoever has access to it can read it.
  - The MLflow password and token are stored **encrypted for your Windows user** (DPAPI) and only travel
    in the authentication header to that server; never in plain text or in the logs.

## Logs

`logs\diagnostics.log` and `logs\engine.log` stay on your PC (they rotate on their own). «Copy logs» in
Settings › Diagnostics makes a zip for **you** to send if you want; nothing is sent by itself.
