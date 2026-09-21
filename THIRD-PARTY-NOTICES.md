# Avisos de terceros — sOC Lucia

| Componente | Uso | Licencia | Titular |
|---|---|---|---|
| llama.cpp (`llama-server`, build fijado en `Engine/EnginePin.cs`) | El motor que ejecuta el modelo. **No va dentro del paquete**: la aplicación lo descarga de las releases oficiales de GitHub la primera vez, comprueba su SHA-256 y lo guarda en `%LOCALAPPDATA%\sOCLucia\engine`. | MIT | ggml-org — <https://github.com/ggml-org/llama.cpp> |
| Runtime de CUDA (`cudart`, zip que acompaña a la versión CUDA de llama.cpp) | Solo en PC con NVIDIA; se descarga con el motor. | Licencia de NVIDIA CUDA Toolkit (redistribuible) | NVIDIA |
| stable-diffusion.cpp (`sd-cli`, build fijado en `Engine/ImageEngine.cs`) | El motor de las imágenes generadas. **No va dentro del paquete**: se descarga de sus releases de GitHub la primera vez que se pide una imagen (o desde Ajustes › Imágenes), se comprueba su SHA-256 y se guarda en `%LOCALAPPDATA%\sOCLucia\engine`. | MIT | leejet — <https://github.com/leejet/stable-diffusion.cpp> |
| Stable Diffusion 1.5 (GGUF Q8_0 publicado por second-state) | El modelo de imágenes; se descarga con el motor y se muestra su licencia en Ajustes. No se distribuye con la aplicación. | CreativeML OpenRAIL-M (uso comercial permitido, con restricciones de uso) | Runway / Stability AI; conversión GGUF de second-state |
| Modelos GGUF (Qwen, Gemma, Ministral, Phi, …) | La IA que el usuario elige e instala desde Ajustes. Cada uno lleva su licencia (Apache-2.0, MIT, Gemma…), que se muestra antes de descargar. No se distribuyen con la aplicación. | La de cada modelo | Sus autores, publicados en Hugging Face |
| API de VS Code (`vscode`) | La extensión `vscode/` usa la API pública del editor; no incluye código de terceros. | MIT (API) | Microsoft |

Todo el código propio (`*.cs`, `*.xaml`, `vscode/*.js`) va bajo MIT (`LICENSE`).
