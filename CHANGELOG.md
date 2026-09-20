# Changelog — sOC AI Chat

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
  falta. Extensión **sOC AI Chat Code** para VS Code (chat lateral con «Insertar», acciones sobre
  la selección, proveedor de modelo para Copilot Chat); también sirve para Continue, Cline y
  cualquier cliente compatible.
- «Acerca de» del catálogo: contacto, idioma, privacidad, licencia MIT y aviso legal.
