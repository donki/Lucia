// sOC Lucia Code — VS Code client for the private AI running in sOC Lucia on this computer.
// Talks to the local door the app opens in Settings > Code editors (OpenAI-compatible, token-protected).
// Nothing leaves the PC: the address is loopback and the model runs in the app.
//
// The chat lives in the activity bar (its own icon on the left, like the other add-ons) and has
// what the desktop chat has: conversations (kept between sessions), Questions/Agent modes, the
// «Think» switch, attachments (workspace files, the selection, pasted images), stop, copy / edit /
// resend a question, and save or insert code blocks.
const vscode = require("vscode");
const path = require("path");
const { spawn } = require("child_process");

const VENDOR = "soclucia";
const APP = "sOC Lucia";
const VIEW_ID = "socLucia.chatView";

function config() {
  const c = vscode.workspace.getConfiguration("socLucia");
  return {
    baseUrl: String(c.get("baseUrl") || "http://127.0.0.1:41417/v1").replace(/\/+$/, ""),
    token: String(c.get("token") || ""),
    maxTokens: Number(c.get("maxTokens") || 1024),
    temperature: Number(c.get("temperature") ?? 0.2),
    systemPrompt: String(c.get("systemPrompt") || ""),
    workspaceTools: c.get("workspaceTools") !== false,
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

/**
 * Streams a chat completion. onDelta({text, reasoning}) per chunk; resolves with
 * { content, reasoning, toolCalls, finish }. `thinking` lets the model reason first (the door
 * turns it off unless asked); `images` (data URLs) go with the last user message.
 */
async function chat(messages, onDelta, signal, tools, thinking) {
  const { baseUrl, maxTokens, temperature } = config();
  const body = { model: "local", messages, stream: true, max_tokens: thinking ? maxTokens * 3 : maxTokens, temperature, chat_template_kwargs: { enable_thinking: !!thinking } };
  if (tools && tools.length) {
    body.tools = tools;
    body.tool_choice = "auto";
  }
  let r;
  try {
    r = await fetch(`${baseUrl}/chat/completions`, { method: "POST", headers: headers(), body: JSON.stringify(body), signal });
  } catch (e) {
    if (signal?.aborted) throw e;
    throw new Error(`${APP} is not reachable at ${baseUrl}. Open ${APP} and turn on Settings > Code editors. (${e.message})`);
  }
  if (!r.ok) throw new Error(await describeError(r));
  const reader = r.body.getReader();
  const decoder = new TextDecoder();
  let buffer = "";
  let content = "";
  let reasoning = "";
  let finish = null;
  const calls = new Map(); // index -> { id, name, arguments } (arguments arrive in fragments)
  const result = () => ({ content, reasoning, finish, toolCalls: [...calls.entries()].sort((x, y) => x[0] - y[0]).map(([i, c]) => ({ id: c.id || `call_${i}`, name: c.name, arguments: c.arguments })) });
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
        const think = delta.reasoning_content ?? "";
        if (text) content += text;
        if (think) reasoning += think;
        if (text || think) onDelta({ text, reasoning: think });
      } catch {
        /* partial line */
      }
    }
  }
  return result();
}

// ------------------------------------------------------------------ workspace tools
// In Agent mode the AI can look at the open workspace by itself: list, read and search files, see
// the active editor, write a file and run a command (those two always ask first).

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
    fn("run_command", "Run one PowerShell command line in the workspace folder and return its output (build, test, git, dotnet, npm…). The user confirms it first.", { command: { type: "string", description: "The command line." } }),
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

function runCommand(command, cwd) {
  return new Promise((resolve) => {
    const child = spawn("powershell.exe", ["-NoProfile", "-NonInteractive", "-Command", command], { cwd, windowsHide: true });
    let out = "";
    const take = (d) => { out += d.toString(); if (out.length > 60000) out = out.slice(-60000); };
    child.stdout.on("data", take);
    child.stderr.on("data", take);
    const timer = setTimeout(() => { try { child.kill(); } catch { /* ya */ } resolve("[timed out after 3 minutes]\n" + out); }, 180000);
    child.on("close", (code) => { clearTimeout(timer); resolve(`[exit code ${code}]\n` + cut(out)); });
    child.on("error", (e) => { clearTimeout(timer); resolve(`[error] ${e.message}`); });
  });
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
    case "run_command": {
      const root = workspaceRoot();
      if (!root) return "No folder is open in VS Code.";
      const command = String(args.command || "").trim();
      if (!command) return "[empty command]";
      const pick = await vscode.window.showWarningMessage(`${APP} wants to run in ${root.fsPath}:\n\n${command}`, { modal: true }, "Run");
      if (pick !== "Run") return "The user declined to run the command.";
      return await runCommand(command, root.fsPath);
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
    "You can inspect the workspace yourself with the tools (list_files, read_file, search_text, active_editor) and change it (write_file, run_command; the user approves each): when the user talks about \"the folder\", \"the project\" or \"this code\", look at it instead of asking them to paste it. Read only what you need; files are cut at 12k characters. One step per call; read the result before the next."
  );
}

// ------------------------------------------------------------------ conversations (kept in VS Code's global state)

const STATE_KEY = "socLucia.threads";
const MAX_THREADS = 40;

class Threads {
  constructor(context) {
    this.context = context;
    this.list = context.globalState.get(STATE_KEY) || [];
  }
  save() {
    // Images are big: only the last 20 threads keep them, and none above ~400 KB.
    const trimmed = this.list.slice(0, MAX_THREADS).map((t, i) => ({
      ...t,
      messages: t.messages.map((m) => (m.images && i >= 20 ? { ...m, images: undefined } : m)),
    }));
    return this.context.globalState.update(STATE_KEY, trimmed);
  }
  create(mode) {
    const t = { id: Date.now().toString(36) + Math.random().toString(36).slice(2, 6), title: "", mode: mode || "ask", messages: [], at: Date.now() };
    this.list.unshift(t);
    return t;
  }
  get(id) {
    return this.list.find((t) => t.id === id) || null;
  }
  remove(id) {
    this.list = this.list.filter((t) => t.id !== id);
  }
  touch(t) {
    t.at = Date.now();
    this.list.sort((a, b) => b.at - a.at);
  }
}

function titleFrom(text) {
  const line = (text || "").trim().split("\n")[0].trim();
  return line.length > 60 ? line.slice(0, 57).trimEnd() + "…" : line || "…";
}

// ------------------------------------------------------------------ the chat view (activity bar)

class ChatViewProvider {
  constructor(context) {
    this.context = context;
    this.threads = new Threads(context);
    this.view = null;
    this.current = null;
    this.inflight = null;
    this.thinking = context.globalState.get("socLucia.thinking") === true;
    this.pending = []; // attachments waiting to be sent: {name, kind: 'text'|'image', content|dataUrl}
    this.queued = null; // a prompt sent before the view was ready
  }

  resolveWebviewView(view) {
    this.view = view;
    view.webview.options = { enableScripts: true, localResourceRoots: [this.context.extensionUri] };
    view.webview.html = chatHtml(view.webview.cspSource);
    view.webview.onDidReceiveMessage((m) => this.onMessage(m));
    view.onDidDispose(() => (this.view = null));
    view.onDidChangeVisibility(() => { if (view.visible) this.paintAll(); });
  }

  post(m) {
    this.view?.webview.postMessage(m);
  }

  paintAll() {
    if (!this.view) return;
    this.post({ type: "state", threads: this.threads.list.map((t) => ({ id: t.id, title: t.title || "…" })), current: this.current?.id || null, mode: this.current?.mode || "ask", thinking: this.thinking, model: this.modelName || "", pending: this.pending.map((p) => ({ name: p.name, kind: p.kind })), busy: !!this.inflight });
    this.post({ type: "messages", messages: (this.current?.messages || []).filter((m) => m.role !== "tool" && !(m.role === "assistant" && !m.content && m.tool_calls)) });
    if (this.queued) { const q = this.queued; this.queued = null; void this.send(q.text, q.withSelection); }
    void this.refreshModel();
  }

  async refreshModel() {
    try {
      const models = await listModels();
      this.modelName = models[0]?.name || "";
    } catch {
      this.modelName = "";
    }
    this.post({ type: "model", model: this.modelName });
  }

  async onMessage(m) {
    switch (m.type) {
      case "ready": this.paintAll(); break;
      case "send": await this.send(m.text, m.withSelection); break;
      case "stop": this.inflight?.abort(); break;
      case "new": this.current = null; this.pending = []; this.paintAll(); break;
      case "select": this.current = this.threads.get(m.id); this.paintAll(); break;
      case "mode": if (this.current) { this.current.mode = m.mode; await this.threads.save(); } this.pendingMode = m.mode; break;
      case "thinking": this.thinking = !!m.value; await this.context.globalState.update("socLucia.thinking", this.thinking); break;
      case "rename": {
        const t = this.threads.get(m.id);
        if (!t) break;
        const title = await vscode.window.showInputBox({ prompt: "Title of the conversation", value: t.title });
        if (title?.trim()) { t.title = title.trim(); await this.threads.save(); this.paintAll(); }
        break;
      }
      case "delete": {
        const t = this.threads.get(m.id);
        if (!t) break;
        const pick = await vscode.window.showWarningMessage(`Delete «${t.title || "…"}»?`, { modal: true }, "Delete");
        if (pick !== "Delete") break;
        if (this.current?.id === m.id) { this.inflight?.abort(); this.current = null; }
        this.threads.remove(m.id);
        await this.threads.save();
        this.paintAll();
        break;
      }
      case "insert": insertAtCursor(m.text); break;
      case "copy": await vscode.env.clipboard.writeText(m.text); vscode.window.setStatusBarMessage(`${APP}: copied`, 1500); break;
      case "saveCode": await saveCode(m.text, m.lang); break;
      case "attachFile": await this.attachFromWorkspace(); break;
      case "attachImage": this.pending.push({ name: m.name || "pasted image", kind: "image", dataUrl: m.dataUrl }); this.paintAll(); break;
      case "removePending": this.pending.splice(m.index, 1); this.paintAll(); break;
      case "openApp": vscode.commands.executeCommand("socLucia.checkConnection"); break;
    }
  }

  async attachFromWorkspace() {
    const editor = vscode.window.activeTextEditor;
    const items = [];
    if (editor) items.push({ label: "$(file) " + vscode.workspace.asRelativePath(editor.document.uri, false), description: "the open file", uri: editor.document.uri });
    items.push({ label: "$(search) Pick a file of the workspace…", pick: true });
    items.push({ label: "$(folder-opened) Choose a file on disk…", disk: true });
    const choice = await vscode.window.showQuickPick(items, { placeHolder: "Attach to the next question" });
    if (!choice) return;
    let uri = choice.uri;
    if (choice.pick) {
      const files = await vscode.workspace.findFiles("**/*", EXCLUDE, 2000);
      const rel = await vscode.window.showQuickPick(files.map((u) => vscode.workspace.asRelativePath(u, false)).sort(), { placeHolder: "File", matchOnDescription: true });
      if (!rel) return;
      uri = resolveInWorkspace(rel);
    } else if (choice.disk) {
      const picked = await vscode.window.showOpenDialog({ canSelectMany: true });
      if (!picked?.length) return;
      for (const u of picked) await this.attachUri(u);
      this.paintAll();
      return;
    }
    if (uri) await this.attachUri(uri);
    this.paintAll();
  }

  async attachUri(uri) {
    const name = path.basename(uri.fsPath);
    const ext = path.extname(name).toLowerCase();
    const bytes = await vscode.workspace.fs.readFile(uri);
    if ([".png", ".jpg", ".jpeg", ".gif", ".webp", ".bmp"].includes(ext)) {
      const mime = ext === ".jpg" ? "image/jpeg" : "image/" + ext.slice(1).replace("jpeg", "jpeg");
      this.pending.push({ name, kind: "image", dataUrl: `data:${mime};base64,${Buffer.from(bytes).toString("base64")}` });
      return;
    }
    if (bytes.length > 2_000_000) { vscode.window.showWarningMessage(`${name} is larger than 2 MB.`); return; }
    const text = new TextDecoder().decode(bytes);
    if (text.includes("\u0000")) { vscode.window.showWarningMessage(`${name} does not look like a text file.`); return; }
    this.pending.push({ name, kind: "text", content: text.length > 40000 ? text.slice(0, 40000) + "\n[… cut]" : text });
  }

  async send(text, withSelection) {
    text = (text || "").trim();
    if (!this.view) { this.queued = { text, withSelection }; await vscode.commands.executeCommand(`${VIEW_ID}.focus`); return; }
    if (!text && !this.pending.length) return;
    if (this.inflight) { vscode.window.setStatusBarMessage(`${APP}: still answering; stop it first`, 2000); return; }
    if (!this.current) {
      this.current = this.threads.create(this.pendingMode || "ask");
    }
    const thread = this.current;
    const editor = vscode.window.activeTextEditor;
    let user = text || "(see the attached files)";
    if (withSelection && editor && !editor.selection.isEmpty)
      user = `${user}\n\n\`\`\`${editor.document.languageId}\n${editor.document.getText(editor.selection)}\n\`\`\``;
    const attachments = this.pending.map((p) => ({ name: p.name, kind: p.kind }));
    let modelText = user;
    const images = [];
    for (const p of this.pending) {
      if (p.kind === "text") modelText += `\n\n[Attached file: ${p.name}]\n\`\`\`\n${p.content}\n\`\`\``;
      else images.push(p.dataUrl);
    }
    this.pending = [];
    const message = { role: "user", content: user, modelText: modelText !== user ? modelText : undefined, attachments: attachments.length ? attachments : undefined, images: images.length ? images : undefined };
    thread.messages.push(message);
    if (!thread.title) thread.title = titleFrom(text || attachments[0]?.name || "");
    this.threads.touch(thread);
    await this.threads.save();
    this.paintAll();

    const controller = new AbortController();
    this.inflight = controller;
    this.post({ type: "busy", value: true });
    const agent = thread.mode === "agent";
    const useTools = agent && config().workspaceTools && !!workspaceRoot();
    const tools = useTools ? toolDefinitions() : undefined;
    const system = config().systemPrompt + (useTools ? "\n\n" + workspaceContext() : "") + (images.length ? "\n\nThe user may attach images; describe or use them when asked." : "");
    const added = [];
    try {
      for (let round = 0; round < 10; round++) {
        if (thread !== this.current) break;
        this.post({ type: "start" });
        const messages = [{ role: "system", content: system }, ...thread.messages.slice(-30).map(toApi)];
        const r = await chat(messages, (d) => { if (thread === this.current) this.post({ type: "delta", text: d.text, reasoning: d.reasoning }); }, controller.signal, tools, this.thinking);
        if (r.toolCalls.length && useTools) {
          const assistant = { role: "assistant", content: r.content || "", reasoning: r.reasoning || undefined, tool_calls: r.toolCalls.map((c) => ({ id: c.id, type: "function", function: { name: c.name, arguments: c.arguments || "{}" } })) };
          thread.messages.push(assistant);
          added.push(assistant);
          this.post({ type: "end", reasoning: r.reasoning || "" });
          for (const call of r.toolCalls) {
            let shown = "";
            try { const a = JSON.parse(call.arguments || "{}"); shown = a.path || a.pattern || a.query || a.command || ""; } catch { /* shown stays empty */ }
            this.post({ type: "step", text: `${call.name} ${shown}`.trim() });
            let output;
            try { output = await runTool(call.name, call.arguments); } catch (e) { output = `[error] ${e.message}`; }
            const toolMsg = { role: "tool", tool_call_id: call.id, content: output, command: `${call.name} ${shown}`.trim() };
            thread.messages.push(toolMsg);
            added.push(toolMsg);
            this.post({ type: "stepResult", text: output.length > 1500 ? output.slice(0, 1500) + "\n…" : output });
          }
          await this.threads.save();
          continue;
        }
        const answer = { role: "assistant", content: r.content, reasoning: r.reasoning || undefined };
        thread.messages.push(answer);
        added.push(answer);
        this.post({ type: "end", reasoning: r.reasoning || "" });
        await this.threads.save();
        return;
      }
      this.post({ type: "error", text: "Stopped after 10 tool rounds without a final answer." });
    } catch (e) {
      if (!controller.signal.aborted) {
        this.post({ type: "error", text: e.message });
        for (const m of added) { const i = thread.messages.lastIndexOf(m); if (i >= 0) thread.messages.splice(i, 1); }
      } else {
        this.post({ type: "end", reasoning: "" });
      }
      await this.threads.save();
    } finally {
      if (this.inflight === controller) this.inflight = null;
      this.post({ type: "busy", value: false });
      this.paintAll();
    }
  }
}

/** A stored message as the API wants it (text files inside the text, images as parts). */
function toApi(m) {
  if (m.role === "tool") return { role: "tool", tool_call_id: m.tool_call_id, content: m.content };
  if (m.tool_calls) return { role: "assistant", content: m.content || null, tool_calls: m.tool_calls };
  if (m.role === "user" && (m.images?.length || m.modelText)) {
    const text = m.modelText || m.content;
    if (!m.images?.length) return { role: "user", content: text };
    return { role: "user", content: [...m.images.map((url) => ({ type: "image_url", image_url: { url } })), { type: "text", text }] };
  }
  return { role: m.role, content: m.content };
}

function insertAtCursor(text) {
  const editor = vscode.window.activeTextEditor;
  if (!editor) return;
  editor.edit((b) => {
    if (editor.selection.isEmpty) b.insert(editor.selection.active, text);
    else b.replace(editor.selection, text);
  });
}

async function saveCode(text, lang) {
  const ext = { csharp: "cs", cs: "cs", javascript: "js", js: "js", typescript: "ts", ts: "ts", python: "py", py: "py", json: "json", xml: "xml", xaml: "xaml", html: "html", css: "css", sql: "sql", powershell: "ps1", ps1: "ps1", bash: "sh", sh: "sh", markdown: "md", md: "md", yaml: "yml", yml: "yml", java: "java", kotlin: "kt", go: "go", rust: "rs", cpp: "cpp", c: "c" }[String(lang || "").toLowerCase()] || "txt";
  const root = workspaceRoot();
  const target = await vscode.window.showSaveDialog({ defaultUri: root ? vscode.Uri.joinPath(root, `nuevo.${ext}`) : undefined, filters: { "Code": [ext], "All files": ["*"] } });
  if (!target) return;
  await vscode.workspace.fs.writeFile(target, new TextEncoder().encode(text));
  vscode.window.showTextDocument(target, { preview: false });
}

function chatHtml(csp) {
  const nonce = String(Math.random()).slice(2);
  return `<!DOCTYPE html><html><head><meta charset="utf-8">
<meta http-equiv="Content-Security-Policy" content="default-src 'none'; img-src ${csp} data:; style-src 'unsafe-inline'; script-src 'nonce-${nonce}';">
<style>
  body{font:13px var(--vscode-font-family);color:var(--vscode-foreground);background:var(--vscode-sideBar-background);margin:0;display:flex;flex-direction:column;height:100vh}
  .bar{display:flex;gap:6px;align-items:center;padding:8px 10px 6px;flex-wrap:wrap}
  select{flex:1;min-width:120px;background:var(--vscode-dropdown-background);color:var(--vscode-dropdown-foreground);border:1px solid var(--vscode-dropdown-border,transparent);border-radius:4px;padding:3px 6px;font:inherit}
  button.i{background:transparent;border:0;color:var(--vscode-icon-foreground);cursor:pointer;padding:3px 5px;border-radius:4px;font-size:14px;line-height:1}
  button.i:hover{background:var(--vscode-toolbar-hoverBackground)}
  .seg{display:flex;border:1px solid var(--vscode-widget-border,#555);border-radius:6px;overflow:hidden}
  .seg button{background:transparent;border:0;color:var(--vscode-foreground);padding:3px 9px;cursor:pointer;font:inherit}
  .seg button.on{background:var(--vscode-button-background);color:var(--vscode-button-foreground)}
  .chk{display:flex;align-items:center;gap:4px;font-size:12px;color:var(--vscode-descriptionForeground);cursor:pointer}
  #model{font-size:11px;color:var(--vscode-descriptionForeground);padding:0 10px 4px;white-space:nowrap;overflow:hidden;text-overflow:ellipsis}
  #log{flex:1;overflow:auto;padding:8px 10px;display:flex;flex-direction:column;gap:8px}
  .m{max-width:94%;padding:8px 10px;border-radius:10px;white-space:pre-wrap;word-break:break-word;line-height:1.4;position:relative}
  .u{align-self:flex-end;background:var(--vscode-button-background);color:var(--vscode-button-foreground)}
  .a{align-self:flex-start;background:var(--vscode-editorWidget-background);border:1px solid var(--vscode-widget-border,transparent)}
  .e{align-self:stretch;color:var(--vscode-errorForeground)}
  .s{align-self:flex-start;font-size:11px;color:var(--vscode-descriptionForeground);font-family:var(--vscode-editor-font-family);padding:0 4px;max-width:94%}
  .s details{margin-top:2px}.s pre{margin:4px 0 0;max-height:160px;overflow:auto;font-size:11px}
  details.r{font-size:12px;color:var(--vscode-descriptionForeground);margin-bottom:6px}
  details.r pre{white-space:pre-wrap;background:transparent;padding:0;margin:4px 0 0}
  .acts{display:flex;gap:2px;justify-content:flex-end;margin:4px -4px -4px 0;opacity:.75}
  .acts button.i{font-size:12px}
  .u .acts button.i{color:var(--vscode-button-foreground)}
  .att{display:flex;flex-wrap:wrap;gap:4px;margin-bottom:6px}
  .att span{display:inline-flex;align-items:center;gap:4px;background:rgba(0,0,0,.15);border-radius:6px;padding:2px 6px;font-size:11px}
  .att img{max-height:80px;border-radius:6px}
  pre{background:var(--vscode-textCodeBlock-background);padding:8px;border-radius:6px;overflow:auto;margin:6px 0;position:relative;font-family:var(--vscode-editor-font-family);font-size:12px}
  pre .cb{position:absolute;top:4px;right:4px;display:flex;gap:2px}
  pre .cb button{font-size:11px;padding:2px 6px;border:1px solid var(--vscode-button-border,transparent);background:var(--vscode-button-secondaryBackground);color:var(--vscode-button-secondaryForeground);border-radius:4px;cursor:pointer}
  code.inl{background:var(--vscode-textCodeBlock-background);padding:0 4px;border-radius:3px;font-family:var(--vscode-editor-font-family)}
  #pending{display:flex;flex-wrap:wrap;gap:4px;padding:0 10px}
  #pending span{display:inline-flex;align-items:center;gap:4px;background:var(--vscode-editorWidget-background);border:1px solid var(--vscode-widget-border,transparent);border-radius:6px;padding:2px 6px;font-size:11px}
  form{display:flex;gap:6px;padding:8px 10px;border-top:1px solid var(--vscode-widget-border,#333);align-items:flex-end}
  textarea{flex:1;resize:none;min-height:38px;max-height:160px;background:var(--vscode-input-background);color:var(--vscode-input-foreground);border:1px solid var(--vscode-input-border,transparent);border-radius:6px;padding:6px 8px;font:inherit}
  button.p{background:var(--vscode-button-background);color:var(--vscode-button-foreground);border:0;border-radius:6px;padding:0 10px;height:32px;cursor:pointer;font-size:14px}
  button.p.stop{background:var(--vscode-errorForeground)}
  .foot{display:flex;align-items:center;justify-content:space-between;padding:0 10px 8px;font-size:11px;color:var(--vscode-descriptionForeground)}
  #status{font-size:11px;color:var(--vscode-descriptionForeground);padding:0 10px 4px}
  #empty{color:var(--vscode-descriptionForeground);text-align:center;margin:auto;padding:20px;line-height:1.5}
</style></head><body>
<div class="bar">
  <select id="threads" title="Conversations"></select>
  <button class="i" id="new" title="New conversation">＋</button>
  <button class="i" id="rename" title="Rename">✎</button>
  <button class="i" id="del" title="Delete">🗑</button>
</div>
<div class="bar" style="padding-top:0">
  <div class="seg"><button id="ask">Questions</button><button id="agent">Agent</button></div>
  <label class="chk"><input type="checkbox" id="think"> Think</label>
</div>
<div id="model"></div>
<div id="log"><div id="empty">Ask the AI running in ${APP} on this PC.<br>Nothing leaves this computer.</div></div>
<div id="status"></div>
<div id="pending"></div>
<form id="f"><button class="p" type="button" id="attach" title="Attach a file (or paste an image)">📎</button><textarea id="t" placeholder="Ask… (Enter sends, Shift+Enter new line; Ctrl+V pastes images)"></textarea><button class="p" type="submit" id="send" title="Send">➤</button></form>
<div class="foot"><label class="chk"><input type="checkbox" id="sel" checked> Include the current selection</label><span id="hint"></span></div>
<script nonce="${nonce}">
const vscode = acquireVsCodeApi();
const $ = (id) => document.getElementById(id);
const log = $('log'), status = $('status');
let current = null, raw = '', rawThink = '', busy = false;
function esc(s){return String(s).replace(/[&<>]/g,c=>({'&':'&amp;','<':'&lt;','>':'&gt;'}[c]));}
function inline(s){ return esc(s).replace(/\`([^\`\\n]+)\`/g,'<code class="inl">$1</code>').replace(/\\*\\*([^*\\n]+)\\*\\*/g,'<b>$1</b>'); }
function render(text){
  const parts = String(text).split(/\`\`\`(\\w*)[ \\t]*\\n([\\s\\S]*?)(?:\`\`\`|$)/g); let html='';
  for(let i=0;i<parts.length;i+=3){ html+=inline(parts[i]); if(parts[i+2]!==undefined){ const code=parts[i+2], lang=parts[i+1]||''; const enc=encodeURIComponent(code);
    html+='<pre><div class="cb"><button data-act="copy" data-code="'+enc+'">Copy</button><button data-act="insert" data-code="'+enc+'">Insert</button><button data-act="save" data-lang="'+esc(lang)+'" data-code="'+enc+'">Save…</button></div><code>'+esc(code)+'</code></pre>'; } }
  return html;
}
function bubble(cls){const d=document.createElement('div');d.className='m '+cls;log.appendChild(d);return d;}
function scroll(){log.scrollTop=log.scrollHeight;}
function userBubble(m){
  const d=bubble('u'); let html='';
  if(m.attachments&&m.attachments.length){ html+='<div class="att">'; m.attachments.forEach((a,i)=>{ const img=m.images&&m.images[i]; html+= img?'<span><img src="'+img+'" alt="'+esc(a.name)+'"></span>':'<span>📄 '+esc(a.name)+'</span>'; }); html+='</div>'; }
  html+=esc(m.content);
  html+='<div class="acts"><button class="i" data-uact="copy" title="Copy">⧉</button><button class="i" data-uact="edit" title="Edit the question">✎</button><button class="i" data-uact="resend" title="Send again">↻</button></div>';
  d.innerHTML=html; d.dataset.text=m.content; return d;
}
function assistantBubble(m){
  const d=bubble('a'); let html='';
  if(m.reasoning) html+='<details class="r"><summary>Reasoning</summary><pre>'+esc(m.reasoning)+'</pre></details>';
  html+=render(m.content||'');
  html+='<div class="acts"><button class="i" data-aact="copy" title="Copy">⧉</button></div>';
  d.innerHTML=html; d.dataset.text=m.content||''; return d;
}
function paintMessages(list){
  log.innerHTML=''; if(!list.length){ log.innerHTML='<div id="empty">Ask the AI running in ${APP} on this PC.<br>Nothing leaves this computer.</div>'; return; }
  for(const m of list){ if(m.role==='user') userBubble(m); else if(m.role==='assistant') assistantBubble(m); }
  scroll();
}
window.addEventListener('message', e=>{const m=e.data;
  if(m.type==='state'){
    const sel=$('threads'); sel.innerHTML='<option value="">— new conversation —</option>'+m.threads.map(t=>'<option value="'+t.id+'"'+(t.id===m.current?' selected':'')+'>'+esc(t.title)+'</option>').join('');
    $('ask').className=m.mode==='agent'?'':'on'; $('agent').className=m.mode==='agent'?'on':''; $('think').checked=!!m.thinking;
    $('pending').innerHTML=m.pending.map((p,i)=>'<span>'+(p.kind==='image'?'🖼 ':'📄 ')+esc(p.name)+' <button class="i" data-rm="'+i+'" title="Remove">✕</button></span>').join('');
    setBusy(m.busy); if(m.model) $('model').textContent='AI: '+m.model;
  }
  else if(m.type==='messages'){ paintMessages(m.messages); }
  else if(m.type==='model'){ $('model').textContent=m.model?'AI: '+m.model:'${APP} is not reachable: open it and turn on Settings › Code editors.'; }
  else if(m.type==='start'){ raw=''; rawThink=''; const empty=$('empty'); if(empty) empty.remove(); current=bubble('a'); current.innerHTML='…'; status.textContent='Thinking…'; scroll(); }
  else if(m.type==='delta'){ raw+=m.text||''; rawThink+=m.reasoning||''; if(current){ current.innerHTML=(rawThink?'<details class="r" open><summary>Reasoning</summary><pre>'+esc(rawThink)+'</pre></details>':'')+render(raw); scroll(); } }
  else if(m.type==='end'){ status.textContent=''; if(current){ if(!raw.trim()&&!rawThink.trim()) current.remove(); else { current.innerHTML=(rawThink?'<details class="r"><summary>Reasoning</summary><pre>'+esc(rawThink)+'</pre></details>':'')+render(raw)+'<div class="acts"><button class="i" data-aact="copy" title="Copy">⧉</button></div>'; current.dataset.text=raw; } } current=null; }
  else if(m.type==='step'){ const d=document.createElement('div'); d.className='s'; d.innerHTML='⚙ '+esc(m.text); log.appendChild(d); scroll(); }
  else if(m.type==='stepResult'){ const last=[...log.querySelectorAll('.s')].pop(); if(last){ last.innerHTML+='<details><summary>output</summary><pre>'+esc(m.text)+'</pre></details>'; } }
  else if(m.type==='error'){ status.textContent=''; const d=bubble('e'); d.textContent=m.text; current=null; scroll(); }
  else if(m.type==='busy'){ setBusy(m.value); }
});
function setBusy(b){ busy=b; const s=$('send'); s.textContent=b?'■':'➤'; s.className='p'+(b?' stop':''); s.title=b?'Stop':'Send'; }
log.addEventListener('click', e=>{
  const b=e.target.closest('button'); if(!b) return;
  if(b.dataset.act){ const code=decodeURIComponent(b.dataset.code); if(b.dataset.act==='insert') vscode.postMessage({type:'insert',text:code}); else if(b.dataset.act==='copy') vscode.postMessage({type:'copy',text:code}); else vscode.postMessage({type:'saveCode',text:code,lang:b.dataset.lang}); return; }
  const bub=b.closest('.m'); if(!bub) return; const text=bub.dataset.text||'';
  if(b.dataset.uact==='copy'||b.dataset.aact==='copy') vscode.postMessage({type:'copy',text});
  else if(b.dataset.uact==='edit'){ $('t').value=text; $('t').focus(); }
  else if(b.dataset.uact==='resend'){ if(!busy) vscode.postMessage({type:'send',text,withSelection:false}); }
});
const f=$('f'), t=$('t');
function submit(){ if(busy){ vscode.postMessage({type:'stop'}); return; } const text=t.value.trim(); const hasPending=$('pending').children.length>0; if(!text&&!hasPending) return; vscode.postMessage({type:'send', text, withSelection:$('sel').checked}); t.value=''; t.style.height='auto'; }
f.addEventListener('submit', e=>{e.preventDefault(); submit();});
$('send').addEventListener('click', e=>{e.preventDefault(); submit();});
t.addEventListener('keydown', e=>{ if(e.key==='Enter' && !e.shiftKey){ e.preventDefault(); submit(); }});
t.addEventListener('input', ()=>{ t.style.height='auto'; t.style.height=Math.min(160,t.scrollHeight)+'px'; });
t.addEventListener('paste', e=>{ const items=[...(e.clipboardData?.items||[])]; const img=items.find(i=>i.type.startsWith('image/')); if(!img) return; e.preventDefault(); const file=img.getAsFile(); const r=new FileReader(); r.onload=()=>vscode.postMessage({type:'attachImage', name:'pasted image', dataUrl:r.result}); r.readAsDataURL(file); });
$('attach').addEventListener('click', ()=>vscode.postMessage({type:'attachFile'}));
$('pending').addEventListener('click', e=>{ const b=e.target.closest('button[data-rm]'); if(b) vscode.postMessage({type:'removePending', index:Number(b.dataset.rm)}); });
$('new').addEventListener('click', ()=>vscode.postMessage({type:'new'}));
$('rename').addEventListener('click', ()=>{ const id=$('threads').value; if(id) vscode.postMessage({type:'rename', id}); });
$('del').addEventListener('click', ()=>{ const id=$('threads').value; if(id) vscode.postMessage({type:'delete', id}); });
$('threads').addEventListener('change', ()=>{ const id=$('threads').value; vscode.postMessage(id?{type:'select', id}:{type:'new'}); });
$('ask').addEventListener('click', ()=>{ $('ask').className='on'; $('agent').className=''; vscode.postMessage({type:'mode', mode:'ask'}); });
$('agent').addEventListener('click', ()=>{ $('agent').className='on'; $('ask').className=''; vscode.postMessage({type:'mode', mode:'agent'}); });
$('think').addEventListener('change', ()=>vscode.postMessage({type:'thinking', value:$('think').checked}));
vscode.postMessage({type:'ready'});
</script></body></html>`;
}

// ------------------------------------------------------------------ selection commands

let provider = null;

function openChat(initialPrompt) {
  void vscode.commands.executeCommand(`${VIEW_ID}.focus`);
  if (initialPrompt && provider) void provider.send(initialPrompt, true);
}

function selectionCommand(prompt) {
  return async () => {
    const editor = vscode.window.activeTextEditor;
    if (!editor || editor.selection.isEmpty) {
      vscode.window.showInformationMessage("Select some code first.");
      return;
    }
    openChat(prompt);
  };
}

async function askCommand() {
  const q = await vscode.window.showInputBox({ prompt: `Ask ${APP} about the selection`, placeHolder: "What does this do? / Why does it fail? / …" });
  if (!q) return;
  openChat(q);
}

async function checkConnection() {
  try {
    const models = await listModels();
    const names = models.map((m) => m.name).join(", ") || "no AI installed yet";
    vscode.window.showInformationMessage(`${APP} is reachable. AI: ${names}.`);
    provider?.refreshModel();
  } catch (e) {
    const pick = await vscode.window.showErrorMessage(`${APP}: ${e.message}`, "Open settings");
    if (pick) vscode.commands.executeCommand("workbench.action.openSettings", "socLucia");
  }
}

// ------------------------------------------------------------------ Language Model provider (Copilot Chat "Manage models" and other lm consumers)

function registerLanguageModelProvider(context) {
  const lm = vscode.lm;
  if (!lm || typeof lm.registerLanguageModelChatProvider !== "function") return;
  const lmProvider = {
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
      await chat([{ role: "system", content: config().systemPrompt }, ...converted], (d) => { if (d.text) progress.report(new vscode.LanguageModelTextPart(d.text)); }, controller.signal, undefined, false);
    },
    async provideTokenCount(_model, text) {
      const s = typeof text === "string" ? text : (text.content || []).map((p) => (p instanceof vscode.LanguageModelTextPart ? p.value : "")).join("");
      return Math.ceil(s.length / 4);
    },
  };
  try {
    context.subscriptions.push(lm.registerLanguageModelChatProvider(VENDOR, lmProvider));
  } catch (e) {
    console.warn(`${APP}: language model provider not registered:`, e.message);
  }
}

function activate(context) {
  provider = new ChatViewProvider(context);
  context.subscriptions.push(
    vscode.window.registerWebviewViewProvider(VIEW_ID, provider, { webviewOptions: { retainContextWhenHidden: true } }),
    vscode.commands.registerCommand("socLucia.openChat", () => openChat()),
    vscode.commands.registerCommand("socLucia.newChat", () => { provider.current = null; provider.pending = []; provider.paintAll(); openChat(); }),
    vscode.commands.registerCommand("socLucia.ask", () => askCommand()),
    vscode.commands.registerCommand("socLucia.explain", selectionCommand("Explain what this code does, step by step, and point out anything risky.")),
    vscode.commands.registerCommand("socLucia.improve", selectionCommand("Improve this code (readability, correctness, performance). Return the full improved code in one block, then a short list of what changed.")),
    vscode.commands.registerCommand("socLucia.tests", selectionCommand("Write unit tests for this code using the usual test framework of the language. Return only the test code in one block.")),
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
  provider?.inflight?.abort();
}

module.exports = { activate, deactivate };
// Gancho para probar el bucle de herramientas fuera de VS Code (con un «vscode» de mentira).
module.exports.__test = { sendForTest: async (text) => { await provider?.send(text, false); }, provider: () => provider };
