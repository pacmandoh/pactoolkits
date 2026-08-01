#!/usr/bin/env node
/**
 * Format AutoHotkey v2 sources via thqby vscode-autohotkey2-lsp (textDocument/formatting).
 *
 * Usage:
 *   node ahk2-format.mjs --server <server.js> --write|--check [--] <files...>
 */
import { spawn } from "node:child_process";
import fs from "node:fs";
import path from "node:path";
import process from "node:process";

const FORMAT_OPTIONS = {
  array_style: "none",
  break_chained_methods: false,
  ignore_comment: false,
  indent_string: "\t",
  max_preserve_newlines: 2,
  brace_style: "One True Brace",
  object_style: "none",
  preserve_newlines: true,
  space_after_double_colon: true,
  space_before_conditional: true,
  space_in_empty_paren: false,
  space_in_other: true,
  space_in_paren: false,
  wrap_line_length: 0,
};

function usage(exitCode = 2) {
  console.error("usage: node ahk2-format.mjs --server <server.js> --write|--check [--] <files...>");
  process.exit(exitCode);
}

function parseArgs(argv) {
  let server = "";
  let mode = "";
  const files = [];
  for (let i = 0; i < argv.length; i++) {
    const arg = argv[i];
    if (arg === "--") {
      files.push(...argv.slice(i + 1));
      break;
    }
    if (arg === "--server") {
      server = argv[++i] ?? "";
      continue;
    }
    if (arg === "--write" || arg === "--check") {
      mode = arg.slice(2);
      continue;
    }
    if (arg.startsWith("-")) usage();
    files.push(arg);
  }
  if (!server || (mode !== "write" && mode !== "check") || files.length === 0) usage();
  return { server: path.resolve(server), mode, files: files.map((f) => path.resolve(f)) };
}

function pathToUri(filePath) {
  const resolved = path.resolve(filePath);
  if (process.platform === "win32") {
    const normalized = resolved.replace(/\\/g, "/");
    return "file:///" + encodeURI(normalized).replace(/#/g, "%23");
  }
  return "file://" + encodeURI(resolved).replace(/#/g, "%23");
}

/** LSP character offsets are UTF-16 code units; JS string indexes match. */
function positionToOffset(text, position) {
  let offset = 0;
  let line = 0;
  while (line < position.line && offset < text.length) {
    const nl = text.indexOf("\n", offset);
    if (nl < 0) return text.length;
    offset = nl + 1;
    line++;
  }
  return Math.min(text.length, offset + position.character);
}

function applyEdits(text, edits) {
  if (!Array.isArray(edits) || edits.length === 0) return text;

  const ops = edits
    .map((edit) => ({
      start: positionToOffset(text, edit.range.start),
      end: positionToOffset(text, edit.range.end),
      newText: edit.newText ?? "",
    }))
    .sort((a, b) => b.start - a.start || b.end - a.end);

  let result = text;
  for (const op of ops) {
    result = result.slice(0, op.start) + op.newText + result.slice(op.end);
  }
  return result;
}

class LspClient {
  constructor(serverPath) {
    this.child = spawn(process.execPath, [serverPath, "--stdio"], {
      stdio: ["pipe", "pipe", "inherit"],
    });
    this.buf = Buffer.alloc(0);
    this.nextId = 1;
    this.pending = new Map();
    this.child.stdout.on("data", (chunk) => this.#onData(chunk));
    this.child.on("error", (err) => {
      for (const { reject } of this.pending.values()) reject(err);
      this.pending.clear();
    });
    this.child.on("exit", (code, signal) => {
      if (this.pending.size === 0) return;
      const err = new Error(
        `ahk2 LSP server exited (code=${code ?? "null"} signal=${signal ?? "null"})`,
      );
      for (const { reject } of this.pending.values()) reject(err);
      this.pending.clear();
    });
  }

  #onData(chunk) {
    this.buf = Buffer.concat([this.buf, chunk]);
    while (true) {
      const headerEnd = this.buf.indexOf("\r\n\r\n");
      if (headerEnd < 0) return;
      const header = this.buf.slice(0, headerEnd).toString("utf8");
      const match = /Content-Length:\s*(\d+)/i.exec(header);
      if (!match) {
        this.buf = this.buf.slice(headerEnd + 4);
        continue;
      }
      const len = Number(match[1]);
      const start = headerEnd + 4;
      if (this.buf.length < start + len) return;
      const body = this.buf.slice(start, start + len).toString("utf8");
      this.buf = this.buf.slice(start + len);
      let msg;
      try {
        msg = JSON.parse(body);
      } catch (err) {
        console.error("ahk2 LSP: invalid JSON body", err);
        continue;
      }
      if (msg.id == null || !this.pending.has(msg.id)) continue;
      const { resolve, reject } = this.pending.get(msg.id);
      this.pending.delete(msg.id);
      if (msg.error) {
        reject(Object.assign(new Error(msg.error.message || "LSP error"), { data: msg.error }));
      } else {
        resolve(msg.result);
      }
    }
  }

  send(method, params, id) {
    const msg = { jsonrpc: "2.0", method, params };
    if (id !== undefined) msg.id = id;
    const body = Buffer.from(JSON.stringify(msg), "utf8");
    this.child.stdin.write(`Content-Length: ${body.length}\r\n\r\n`);
    this.child.stdin.write(body);
  }

  request(method, params) {
    const id = this.nextId++;
    return new Promise((resolve, reject) => {
      this.pending.set(id, { resolve, reject });
      this.send(method, params, id);
    });
  }

  notify(method, params) {
    this.send(method, params);
  }

  async shutdown() {
    try {
      await this.request("shutdown", null);
    } catch {
      // ignore
    }
    try {
      this.notify("exit", undefined);
    } catch {
      // ignore
    }
    if (!this.child.killed) {
      this.child.kill();
    }
  }
}

async function formatFile(client, filePath) {
  const text = fs.readFileSync(filePath, "utf8");
  const uri = pathToUri(filePath);
  client.notify("textDocument/didOpen", {
    textDocument: { uri, languageId: "ahk2", version: 1, text },
  });
  try {
    const edits = await client.request("textDocument/formatting", {
      textDocument: { uri },
      options: { tabSize: 4, insertSpaces: false },
    });
    return applyEdits(text, edits);
  } finally {
    client.notify("textDocument/didClose", { textDocument: { uri } });
  }
}

async function main() {
  const { server, mode, files } = parseArgs(process.argv.slice(2));
  if (!fs.existsSync(server)) {
    console.error(`ERROR: ahk2 LSP server not found: ${server}`);
    process.exit(1);
  }

  const client = new LspClient(server);
  let failed = 0;
  try {
    await client.request("initialize", {
      processId: process.pid,
      rootUri: null,
      capabilities: {},
      initializationOptions: { FormatOptions: FORMAT_OPTIONS },
    });
    client.notify("initialized", {});

    for (const file of files) {
      if (!fs.existsSync(file)) {
        console.error(`ERROR: missing file: ${file}`);
        failed++;
        continue;
      }
      const original = fs.readFileSync(file, "utf8");
      let formatted;
      try {
        formatted = await formatFile(client, file);
      } catch (err) {
        console.error(`ERROR: format failed: ${file}`);
        console.error(err);
        failed++;
        continue;
      }

      if (formatted === original) {
        console.log(`ok  ${path.relative(process.cwd(), file) || file}`);
        continue;
      }

      if (mode === "write") {
        fs.writeFileSync(file, formatted, "utf8");
        console.log(`fix ${path.relative(process.cwd(), file) || file}`);
      } else {
        console.error(`would reformat: ${path.relative(process.cwd(), file) || file}`);
        failed++;
      }
    }
  } finally {
    await client.shutdown();
  }

  process.exit(failed > 0 ? 1 : 0);
}

main().catch((err) => {
  console.error(err);
  process.exit(1);
});
