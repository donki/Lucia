// sOC Lucia Code — VS Code client for the private AI running in sOC Lucia on this computer.
// Talks to the local door the app opens in Settings > Code editors (OpenAI-compatible, token-protected).
// Nothing leaves the PC: the address is loopback and the model runs in the app.
const vscode = require("vscode");

const VENDOR = "soclucia";
const APP = "sOC Lucia";

function config() {
  const c = vscode.workspace.getConfiguration("socLucia");
  return {
    baseUrl: String(c.get("baseUrl") || "http://127.0.0.1:41417/v1").replace(/\/+$/, ""),
    token: String(c.get("token") || ""),
    maxTokens: Number(c.get("maxTokens") || 1024),
    temperature: Number(c.get("temperature") ?? 0.2),
    systemPrompt: String(c.get("systemPrompt") || ""),
  };
}

function headers() {
  return { "Content-Type": "application/json", Authorization: `Bearer ${config().token}` };
}

async function listModels() {
  const { baseUrl } = config();
  const r = await fetch(`${baseUrl}/models`, { headers: headers() });
  if (!r.ok) throw new Error(await describeError(r));
  const j = await r.json();
  return (j.data || []).map((m) => ({ id: m.id, name: m.name || m.id }));
}

async function describeError(r) {
  let text = "";
  try {
    const j = await r.json();
    text = j?.error?.message || JSON.stringify(j);
  } catch {
    text = await r.text().catch(() => "");
  }
  if (r.status === 401) return `${APP} rejected the token. Copy it from ${APP} > Settings > Code editors into the setting socLucia.token.`;
  if (r.status === 503) return text || `${APP} has no AI installed or could not start it.`;
  return `${r.status} ${text}`.trim();
}

/** Streams a chat completion. onDelta(text) per chunk; resolves with the full text. */
async function chat(messages, onDelta, signal) {
  const { baseUrl, maxTokens, temperature } = config();
  let r;
  try {
    r = await fetch(`${baseUrl}/chat/completions`, {
      method: "POST",
      headers: headers(),
      body: JSON.stringify({ model: "local", messages, stream: true, max_tokens: maxTokens, temperature }),
      signal,
    });
  } catch (e) {
    throw new Error(`${APP} is not reachable at ${baseUrl}. Open ${APP} and turn on Settings > Code editors. (${e.message})`);
  }
  if (!r.ok) throw new Error(await describeError(r));
  const reader = r.body.getReader();
  const decoder = new TextDecoder();
  let buffer = "";
  let full = "";
  for (;;) {
    const { value, done } = await reader.read();
    if (done) break;
    buffer += decoder.decode(value, { stream: true });
    let nl;
    while ((nl = buffer.indexOf("\n")) >= 0) {
      const line = buffer.slice(0, nl).trim();
      buffer = buffer.slice(nl + 1);
      if (!line.startsWith("data:")) continue;
      const payload = line.slice(5).trim();
      if (payload === "[DONE]") return full;
      try {
        const j = JSON.parse(payload);
        const delta = j.choices?.[0]?.delta?.content ?? j.choices?.[0]?.text ?? "";
        if (delta) {
          full += delta;
          onDelta(delta);
        }
      } catch {
        /* partial line */
      }
    }
  }
  return full;
}

// ------------------------------------------------------------------ chat panel

let panel = null;
const history = [];
let inflight = null;

function openChat(context, initialPrompt) {
  if (!panel) {
    panel = vscode.window.createWebviewPanel("socLucia", APP, vscode.ViewColumn.Beside, { enableScripts: true, retainContextWhenHidden: true });
    panel.iconPath = vscode.Uri.joinPath(context.extensionUri, "icon.png");
    panel.webview.html = chatHtml();
    panel.onDidDispose(() => (panel = null));
    panel.webview.onDidReceiveMessage(async (msg) => {
      if (msg.type === "send") await send(msg.text, msg.withSelection);
      else if (msg.type === "insert") insertAtCursor(msg.text);
      else if (msg.type === "clear") history.length = 0;
    });
  }
  panel.reveal(vscode.ViewColumn.Beside, true);
  if (initialPrompt) void send(initialPrompt, true);
}

async function send(text, withSelection) {
  if (!panel || !text.trim()) return;
  if (inflight) inflight.abort();
  const controller = new AbortController();
  inflight = controller;
  const editor = vscode.window.activeTextEditor;
  let user = text;
  if (withSelection && editor && !editor.selection.isEmpty) {
    user = `${text}\n\n\`\`\`${editor.document.languageId}\n${editor.document.getText(editor.selection)}\n\`\`\``;
  }
  history.push({ role: "user", content: user });
  panel.webview.postMessage({ type: "user", text: user });
  panel.webview.postMessage({ type: "start" });
  const messages = [{ role: "system", content: config().systemPrompt }, ...history.slice(-12)];
  try {
    const full = await chat(messages, (d) => panel?.webview.postMessage({ type: "delta", text: d }), controller.signal);
    history.push({ role: "assistant", content: full });
    panel?.webview.postMessage({ type: "end" });
  } catch (e) {
    if (controller.signal.aborted) return;
    panel?.webview.postMessage({ type: "error", text: e.message });
    history.pop();
  } finally {
    if (inflight === controller) inflight = null;
  }
}

function insertAtCursor(text) {
  const editor = vscode.window.activeTextEditor;
  if (!editor) return;
  editor.edit((b) => {
    if (editor.selection.isEmpty) b.insert(editor.selection.active, text);
    else b.replace(editor.selection, text);
  });
}

function chatHtml() {
  const nonce = String(Math.random()).slice(2);
  return `<!DOCTYPE html><html><head><meta charset="utf-8">
<meta http-equiv="Content-Security-Policy" content="default-src 'none'; style-src 'unsafe-inline'; script-src 'nonce-${nonce}';">
<style>
  body{font:13px var(--vscode-font-family);color:var(--vscode-foreground);background:var(--vscode-editor-background);margin:0;display:flex;flex-direction:column;height:100vh}
  #log{flex:1;overflow:auto;padding:12px;display:flex;flex-direction:column;gap:10px}
  .m{max-width:92%;padding:8px 10px;border-radius:10px;white-space:pre-wrap;word-break:break-word;line-height:1.4}
  .u{align-self:flex-end;background:var(--vscode-button-background);color:var(--vscode-button-foreground)}
  .a{align-self:flex-start;background:var(--vscode-editorWidget-background);border:1px solid var(--vscode-widget-border,transparent)}
  .e{align-self:stretch;color:var(--vscode-errorForeground)}
  pre{background:var(--vscode-textCodeBlock-background);padding:8px;border-radius:6px;overflow:auto;margin:6px 0;position:relative}
  pre button{position:absolute;top:4px;right:4px;font-size:11px;padding:2px 6px;border:1px solid var(--vscode-button-border,transparent);background:var(--vscode-button-secondaryBackground);color:var(--vscode-button-secondaryForeground);border-radius:4px;cursor:pointer}
  form{display:flex;gap:6px;padding:10px;border-top:1px solid var(--vscode-widget-border,#333)}
  textarea{flex:1;resize:none;min-height:38px;max-height:160px;background:var(--vscode-input-background);color:var(--vscode-input-foreground);border:1px solid var(--vscode-input-border,transparent);border-radius:6px;padding:6px 8px;font:inherit}
  button.p{background:var(--vscode-button-background);color:var(--vscode-button-foreground);border:0;border-radius:6px;padding:0 12px;cursor:pointer}
  label{display:flex;align-items:center;gap:4px;font-size:11px;color:var(--vscode-descriptionForeground);padding:0 12px 6px}
  #status{font-size:11px;color:var(--vscode-descriptionForeground);padding:0 12px 8px}
</style></head><body>
<div id="log"></div>
<div id="status"></div>
<label><input type="checkbox" id="sel" checked> Include the current selection</label>
<form id="f"><textarea id="t" placeholder="Ask… (Enter sends, Shift+Enter new line)"></textarea><button class="p" type="submit">Send</button><button class="p" type="button" id="clear" title="New conversation">⟲</button></form>
<script nonce="${nonce}">
const vscode = acquireVsCodeApi();
const log = document.getElementById('log'), status = document.getElementById('status');
let current = null, raw = '';
function esc(s){return s.replace(/[&<>]/g,c=>({'&':'&amp;','<':'&lt;','>':'&gt;'}[c]));}
function render(text){
  const parts = text.split(/\`\`\`(\\w*)\\n([\\s\\S]*?)(?:\`\`\`|$)/g); let html='';
  for(let i=0;i<parts.length;i+=3){ html+=esc(parts[i]); if(parts[i+2]!==undefined){ const code=parts[i+2]; html+='<pre><button data-code="'+esc(code).replace(/"/g,'&quot;')+'">Insert</button><code>'+esc(code)+'</code></pre>'; } }
  return html;
}
function add(cls, text){const d=document.createElement('div');d.className='m '+cls;d.innerHTML=render(text);log.appendChild(d);log.scrollTop=log.scrollHeight;return d;}
window.addEventListener('message', e=>{const m=e.data;
  if(m.type==='user'){add('u',m.text);}
  else if(m.type==='start'){raw='';current=add('a','');status.textContent='Thinking…';}
  else if(m.type==='delta'){raw+=m.text;if(current){current.innerHTML=render(raw);log.scrollTop=log.scrollHeight;}}
  else if(m.type==='end'){status.textContent='';current=null;}
  else if(m.type==='error'){status.textContent='';add('e',m.text);current=null;}
});
log.addEventListener('click', e=>{const b=e.target.closest('button[data-code]'); if(b) vscode.postMessage({type:'insert', text:b.dataset.code});});
const f=document.getElementById('f'), t=document.getElementById('t');
f.addEventListener('submit', e=>{e.preventDefault(); const text=t.value.trim(); if(!text) return; vscode.postMessage({type:'send', text, withSelection:document.getElementById('sel').checked}); t.value='';});
t.addEventListener('keydown', e=>{ if(e.key==='Enter' && !e.shiftKey){ e.preventDefault(); f.requestSubmit(); }});
document.getElementById('clear').addEventListener('click', ()=>{log.innerHTML='';vscode.postMessage({type:'clear'});});
</script></body></html>`;
}

// ------------------------------------------------------------------ selection commands

function selectionCommand(context, prompt) {
  return async () => {
    const editor = vscode.window.activeTextEditor;
    if (!editor || editor.selection.isEmpty) {
      vscode.window.showInformationMessage("Select some code first.");
      return;
    }
    openChat(context, prompt);
  };
}

async function askCommand(context) {
  const q = await vscode.window.showInputBox({ prompt: `Ask ${APP} about the selection`, placeHolder: "What does this do? / Why does it fail? / …" });
  if (!q) return;
  openChat(context, q);
}

async function checkConnection() {
  try {
    const models = await listModels();
    const names = models.map((m) => m.name).join(", ") || "no AI installed yet";
    vscode.window.showInformationMessage(`${APP} is reachable. AI: ${names}.`);
  } catch (e) {
    const pick = await vscode.window.showErrorMessage(`${APP}: ${e.message}`, "Open settings");
    if (pick) vscode.commands.executeCommand("workbench.action.openSettings", "socLucia");
  }
}

// ------------------------------------------------------------------ Language Model provider (Copilot Chat "Manage models" and other lm consumers)

function registerLanguageModelProvider(context) {
  const lm = vscode.lm;
  if (!lm || typeof lm.registerLanguageModelChatProvider !== "function") return;
  const provider = {
    async provideLanguageModelChatInformation() {
      let models = [];
      try {
        models = await listModels();
      } catch {
        return [];
      }
      return models.map((m) => ({
        id: m.id,
        name: `${m.name} (${APP})`,
        family: VENDOR,
        version: "1",
        maxInputTokens: 6000,
        maxOutputTokens: config().maxTokens,
        capabilities: { toolCalling: false, imageInput: false },
      }));
    },
    async provideLanguageModelChatResponse(_model, messages, _options, progress, token) {
      const converted = messages.map((m) => ({
        role: m.role === vscode.LanguageModelChatMessageRole.Assistant ? "assistant" : "user",
        content: (m.content || []).map((p) => (p instanceof vscode.LanguageModelTextPart ? p.value : typeof p === "string" ? p : "")).join(""),
      }));
      const controller = new AbortController();
      token.onCancellationRequested(() => controller.abort());
      await chat([{ role: "system", content: config().systemPrompt }, ...converted], (d) => progress.report(new vscode.LanguageModelTextPart(d)), controller.signal);
    },
    async provideTokenCount(_model, text) {
      const s = typeof text === "string" ? text : (text.content || []).map((p) => (p instanceof vscode.LanguageModelTextPart ? p.value : "")).join("");
      return Math.ceil(s.length / 4);
    },
  };
  try {
    context.subscriptions.push(lm.registerLanguageModelChatProvider(VENDOR, provider));
  } catch (e) {
    console.warn(`${APP}: language model provider not registered:`, e.message);
  }
}

function activate(context) {
  context.subscriptions.push(
    vscode.commands.registerCommand("socLucia.openChat", () => openChat(context)),
    vscode.commands.registerCommand("socLucia.ask", () => askCommand(context)),
    vscode.commands.registerCommand("socLucia.explain", selectionCommand(context, "Explain what this code does, step by step, and point out anything risky.")),
    vscode.commands.registerCommand("socLucia.improve", selectionCommand(context, "Improve this code (readability, correctness, performance). Return the full improved code in one block, then a short list of what changed.")),
    vscode.commands.registerCommand("socLucia.tests", selectionCommand(context, "Write unit tests for this code using the usual test framework of the language. Return only the test code in one block.")),
    vscode.commands.registerCommand("socLucia.checkConnection", checkConnection),
  );
  registerLanguageModelProvider(context);
  const item = vscode.window.createStatusBarItem(vscode.StatusBarAlignment.Right, 50);
  item.text = "$(hubot) sOC AI";
  item.tooltip = `${APP}: open chat`;
  item.command = "socLucia.openChat";
  item.show();
  context.subscriptions.push(item);
}

function deactivate() {
  if (inflight) inflight.abort();
}

module.exports = { activate, deactivate };
