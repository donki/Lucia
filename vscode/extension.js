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

/** Streams a chat completion. onDelta(text) per chunk; resolves with { content, toolCalls, finish }. */
async function chat(messages, onDelta, signal, tools) {
  const { baseUrl, maxTokens, temperature } = config();
  const body = { model: "local", messages, stream: true, max_tokens: maxTokens, temperature };
  if (tools && tools.length) {
    body.tools = tools;
    body.tool_choice = "auto";
  }
  let r;
  try {
    r = await fetch(`${baseUrl}/chat/completions`, { method: "POST", headers: headers(), body: JSON.stringify(body), signal });
  } catch (e) {
    throw new Error(`${APP} is not reachable at ${baseUrl}. Open ${APP} and turn on Settings > Code editors. (${e.message})`);
  }
  if (!r.ok) throw new Error(await describeError(r));
  const reader = r.body.getReader();
  const decoder = new TextDecoder();
  let buffer = "";
  let content = "";
  let finish = null;
  const calls = new Map(); // index -> { id, name, arguments } (arguments arrive in fragments)
  const result = () => ({ content, finish, toolCalls: [...calls.entries()].sort((x, y) => x[0] - y[0]).map(([i, c]) => ({ id: c.id || `call_${i}`, name: c.name, arguments: c.arguments })) });
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
      if (payload === "[DONE]") return result();
      try {
        const j = JSON.parse(payload);
        const choice = j.choices?.[0] || {};
        if (choice.finish_reason) finish = choice.finish_reason;
        const delta = choice.delta || {};
        for (const f of delta.tool_calls || []) {
          const i = f.index ?? 0;
          const c = calls.get(i) || { id: "", name: "", arguments: "" };
          if (f.id) c.id = f.id;
          if (f.function?.name) c.name += f.function.name;
          if (f.function?.arguments) c.arguments += f.function.arguments;
          calls.set(i, c);
        }
        const text = delta.content ?? choice.text ?? "";
        if (text) {
          content += text;
          onDelta(text);
        }
      } catch {
        /* partial line */
      }
    }
  }
  return result();
}

// ------------------------------------------------------------------ workspace tools
// The AI can look at the open workspace by itself: list, read and search files, see the active
// editor, and write a file (that one always asks). Everything stays inside VS Code's workspace API.

const MAX_CHARS = 12000;
const EXCLUDE = "**/{node_modules,bin,obj,.git,dist,out,.vs,__pycache__}/**";

function toolDefinitions() {
  const fn = (name, description, properties, required) => ({
    type: "function",
    function: { name, description, parameters: { type: "object", properties, required: required || Object.keys(properties) } },
  });
  return [
    fn("list_files", "List files of the open workspace matching a glob (relative paths). Start here to understand a project.", { pattern: { type: "string", description: "Glob like **/*.cs or src/**. Default: everything." } }, []),
    fn("read_file", "Read a file of the workspace by its relative path (long files are cut).", { path: { type: "string", description: "Path relative to the workspace, as list_files returns it." } }),
    fn("search_text", "Search a text (case-insensitive) in the workspace files and return file:line matches.", { query: { type: "string", description: "Text to look for." }, pattern: { type: "string", description: "Optional glob to limit the files." } }, ["query"]),
    fn("active_editor", "The file the user has open right now: path, language, selection and content.", {}, []),
    fn("write_file", "Create or overwrite a file in the workspace with the given content. The user confirms it first.", { path: { type: "string", description: "Relative path." }, content: { type: "string", description: "Whole content." } }),
  ];
}

function cut(text) {
  return text.length <= MAX_CHARS ? text : text.slice(0, MAX_CHARS / 2) + "\n…[cut]…\n" + text.slice(-MAX_CHARS / 2);
}

function workspaceRoot() {
  return vscode.workspace.workspaceFolders?.[0]?.uri || null;
}

function resolveInWorkspace(rel) {
  const root = workspaceRoot();
  if (!root) throw new Error("No folder is open in VS Code.");
  const clean = String(rel || "").replace(/^[\\/]+/, "").replace(/\\/g, "/");
  if (clean.includes("..")) throw new Error("Paths must stay inside the workspace.");
  return vscode.Uri.joinPath(root, clean);
}

async function runTool(name, argsJson) {
  let args = {};
  try {
    args = JSON.parse(argsJson || "{}");
  } catch {
    /* the model sent broken JSON: run with no arguments */
  }
  const decoder = new TextDecoder();
  switch (name) {
    case "list_files": {
      const files = await vscode.workspace.findFiles(args.pattern || "**/*", EXCLUDE, 400);
      if (!files.length) return "[no files]";
      const lines = files.map((u) => vscode.workspace.asRelativePath(u, false)).sort();
      return cut(lines.join("\n") + (files.length >= 400 ? "\n…[more]" : ""));
    }
    case "read_file": {
      const uri = resolveInWorkspace(args.path);
      const bytes = await vscode.workspace.fs.readFile(uri);
      return cut(decoder.decode(bytes));
    }
    case "search_text": {
      const query = String(args.query || "").toLowerCase();
      if (!query) return "[empty query]";
      const files = await vscode.workspace.findFiles(args.pattern || "**/*", EXCLUDE, 600);
      const hits = [];
      for (const uri of files) {
        let text;
        try {
          const stat = await vscode.workspace.fs.stat(uri);
          if (stat.size > 300000) continue;
          text = decoder.decode(await vscode.workspace.fs.readFile(uri));
        } catch {
          continue;
        }
        if (text.includes("\u0000")) continue;
        const lines = text.split("\n");
        for (let i = 0; i < lines.length && hits.length < 80; i++)
          if (lines[i].toLowerCase().includes(query)) hits.push(`${vscode.workspace.asRelativePath(uri, false)}:${i + 1}: ${lines[i].trim().slice(0, 200)}`);
        if (hits.length >= 80) break;
      }
      return hits.length ? hits.join("\n") : "[no matches]";
    }
    case "active_editor": {
      const editor = vscode.window.activeTextEditor;
      if (!editor) return "[no editor is active]";
      const doc = editor.document;
      const sel = editor.selection.isEmpty ? "" : `\nSelection (lines ${editor.selection.start.line + 1}-${editor.selection.end.line + 1}):\n${doc.getText(editor.selection)}`;
      return `Path: ${vscode.workspace.asRelativePath(doc.uri, false)}\nLanguage: ${doc.languageId}${sel}\n\nContent:\n${cut(doc.getText())}`;
    }
    case "write_file": {
      const uri = resolveInWorkspace(args.path);
      const rel = vscode.workspace.asRelativePath(uri, false);
      const pick = await vscode.window.showWarningMessage(`${APP} wants to write ${rel} (${String(args.content || "").length} characters).`, { modal: true }, "Write");
      if (pick !== "Write") return "The user declined to write the file.";
      await vscode.workspace.fs.writeFile(uri, new TextEncoder().encode(String(args.content || "")));
      vscode.window.showTextDocument(uri, { preview: false });
      return `Written ${rel}.`;
    }
    default:
      return `Unknown tool: ${name}`;
  }
}

function workspaceContext() {
  const folders = (vscode.workspace.workspaceFolders || []).map((f) => `${f.name} (${f.uri.fsPath})`);
  const editor = vscode.window.activeTextEditor;
  const active = editor ? `${vscode.workspace.asRelativePath(editor.document.uri, false)} [${editor.document.languageId}]` : "none";
  if (!folders.length) return "No folder is open in VS Code.";
  return (
    `Workspace folder(s): ${folders.join("; ")}. Active file: ${active}. ` +
    "You can inspect the workspace yourself with the tools (list_files, read_file, search_text, active_editor): when the user talks about \"the folder\", \"the project\" or \"this code\", look at it instead of asking them to paste it. Read only what you need; files are cut at 12k characters."
  );
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
  const useTools = vscode.workspace.getConfiguration("socLucia").get("workspaceTools") !== false && !!workspaceRoot();
  const tools = useTools ? toolDefinitions() : undefined;
  const system = config().systemPrompt + (useTools ? "\n\n" + workspaceContext() : "");
  const added = []; // what this turn appended to history (to roll back on error)
  try {
    for (let round = 0; round < 8; round++) {
      panel.webview.postMessage({ type: "start" });
      const messages = [{ role: "system", content: system }, ...history.slice(-24)];
      const r = await chat(messages, (d) => panel?.webview.postMessage({ type: "delta", text: d }), controller.signal, tools);
      if (r.toolCalls.length && useTools) {
        const assistant = { role: "assistant", content: r.content || "", tool_calls: r.toolCalls.map((c) => ({ id: c.id, type: "function", function: { name: c.name, arguments: c.arguments || "{}" } })) };
        history.push(assistant);
        added.push(assistant);
        panel?.webview.postMessage({ type: "end" });
        for (const call of r.toolCalls) {
          let shown = "";
          try {
            const a = JSON.parse(call.arguments || "{}");
            shown = a.path || a.pattern || a.query || "";
          } catch {
            /* shown stays empty */
          }
          panel?.webview.postMessage({ type: "step", text: `${call.name} ${shown}`.trim() });
          let output;
          try {
            output = await runTool(call.name, call.arguments);
          } catch (e) {
            output = `[error] ${e.message}`;
          }
          const toolMsg = { role: "tool", tool_call_id: call.id, content: output };
          history.push(toolMsg);
          added.push(toolMsg);
        }
        continue;
      }
      const answer = { role: "assistant", content: r.content };
      history.push(answer);
      added.push(answer);
      panel?.webview.postMessage({ type: "end" });
      return;
    }
    panel?.webview.postMessage({ type: "error", text: "Stopped after 8 tool rounds without a final answer." });
  } catch (e) {
    if (controller.signal.aborted) return;
    panel?.webview.postMessage({ type: "error", text: e.message });
    for (const m of added) {
      const i = history.lastIndexOf(m);
      if (i >= 0) history.splice(i, 1);
    }
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
  .s{align-self:flex-start;font-size:11px;color:var(--vscode-descriptionForeground);font-family:var(--vscode-editor-font-family);padding:0 4px}
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
  else if(m.type==='end'){status.textContent='';if(current&&!raw.trim())current.remove();current=null;}
  else if(m.type==='step'){add('s','\u2699 '+m.text);}
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
      await chat([{ role: "system", content: config().systemPrompt }, ...converted], (d) => progress.report(new vscode.LanguageModelTextPart(d)), controller.signal, undefined);
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
  item.text = "$(hubot) Lucia";
  item.tooltip = `${APP}: open chat`;
  item.command = "socLucia.openChat";
  item.show();
  context.subscriptions.push(item);
}

function deactivate() {
  if (inflight) inflight.abort();
}

module.exports = { activate, deactivate };
// Gancho para probar el bucle de herramientas fuera de VS Code (con un «vscode» de mentira).
module.exports.__test = { openChatAndSend: async (context, text) => { openChat(context); await send(text, false); } };
