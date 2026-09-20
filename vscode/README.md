# sOC Lucia Code (VS Code)

Program with the private AI that [sOC Lucia](https://github.com/donki/Lucia) runs on this computer. The extension talks to the app over a loopback address protected with a token; the model runs in the app, so nothing leaves the PC.

## Setup

1. In sOC Lucia: **Settings › Code editors (VS Code)** → turn it on. Copy the **token** (and the address only if you changed the port).
2. In VS Code: install this extension (`soc-lucia-code-<version>.vsix` → *Extensions › … › Install from VSIX*), then open *Settings › sOC Lucia* and paste the token.
3. Run **sOC Lucia: Check connection** from the Command Palette. It lists the AI installed in the app.

## What it does

- **sOC Lucia: Open chat** (`Ctrl+Alt+I`): a chat panel beside the editor. Answers stream in; every code block has an **Insert** button that puts it at the cursor (or replaces the selection). "Include the current selection" attaches the selected code to the question.
- Right-click on a selection → **sOC Lucia ›** *Ask about the selection*, *Explain*, *Improve*, *Write tests*.
- **Copilot Chat and other model consumers**: the extension registers the app as a language model provider (vendor `soclucia`). In Copilot Chat, *Manage models…* shows "*<your AI> (sOC Lucia)*". Needs VS Code 1.104 or newer.

## Other clients

Any OpenAI-compatible client works with the same address and token: **Continue** (`provider: openai`, `apiBase: http://127.0.0.1:41417/v1`, `apiKey: <token>`, `model: local`), **Cline / Roo Code** (provider *OpenAI Compatible*, base URL `http://127.0.0.1:41417/v1`, API key = token, model id from `GET /v1/models`).

## Settings

| Setting | Default | Meaning |
|---|---|---|
| `socLucia.baseUrl` | `http://127.0.0.1:41417/v1` | Address shown in the app |
| `socLucia.token` | — | Token shown in the app |
| `socLucia.maxTokens` | 1024 | Longest answer |
| `socLucia.temperature` | 0.2 | Low is better for code |
| `socLucia.systemPrompt` | (engineering assistant) | Standing instructions |

## Build the .vsix

```bash
cd vscode
npm run package
```

MIT License · Copyright © 2026 Socratic.
