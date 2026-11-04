#!/usr/bin/env node
/**
 * Unity MCP 网关（配置自适应版）
 *
 * 职责：把 stdio 上的 MCP 协议请求，转发到 Unity 编辑器内的本地桥接服务。
 *
 * 相比旧版的关键改动 —— 网关不再写死任何工程标识：
 *   1. 会话目录、鉴权头、server 名全部从 session.json 读取（Unity 侧 McpProjectConfig 决定）；
 *   2. 会话文件按「已知目录名 → Library/* 扫描」两级定位，换工程/换目录名都不用改这里；
 *   3. 工具清单由 Unity 侧生成，网关零硬编码；
 *   4. 每次请求都重读会话文件 —— Unity 重启、换端口、域重载后网关无需重启；
 *   5. 日志只走 stderr，绝不污染 stdout（stdout 是协议通道）。
 */

import { Server } from "@modelcontextprotocol/sdk/server/index.js";
import { StdioServerTransport } from "@modelcontextprotocol/sdk/server/stdio.js";
import {
  CallToolRequestSchema,
  ListToolsRequestSchema,
  ListResourcesRequestSchema,
  ReadResourceRequestSchema,
} from "@modelcontextprotocol/sdk/types.js";
import fs from "node:fs";
import path from "node:path";

const REQUEST_TIMEOUT_MS = Number(process.env.UNITY_MCP_TIMEOUT_MS || 20000);
const DEBUG = process.env.UNITY_MCP_DEBUG === "1";
const LOG_TAG = "[mcp-gateway]";

/** 常见会话目录名（新框架用 Mcp<项目名>，老工程是 UnityMcp） */
const SESSION_DIR_CANDIDATES = ["McpBridge", "UnityMcp"];

let idSequence = 0;

function debug(...args) {
  if (DEBUG) console.error(LOG_TAG, ...args);
}

/** 在给定工程根下找会话文件。 */
function findSessionUnder(root) {
  if (!root) return null;

  // ① 已知目录名（快路径）
  for (const dir of SESSION_DIR_CANDIDATES) {
    const p = path.join(root, "Library", dir, "session.json");
    if (fs.existsSync(p)) return p;
  }

  // ② 扫描 Library/* /session.json，取第一个像会话文件的
  const library = path.join(root, "Library");
  try {
    if (!fs.existsSync(library)) return null;
    for (const entry of fs.readdirSync(library, { withFileTypes: true })) {
      if (!entry.isDirectory()) continue;
      const candidate = path.join(library, entry.name, "session.json");
      if (!fs.existsSync(candidate)) continue;
      try {
        const probe = JSON.parse(fs.readFileSync(candidate, "utf8"));
        if (probe && probe.port && probe.token) return candidate;
      } catch {
        /* 坏文件跳过 */
      }
    }
  } catch {
    /* 权限/IO 问题忽略 */
  }
  return null;
}

/** 定位会话文件：命令行参数 > 环境变量 > 从 cwd 向上查找。 */
function resolveSessionPath() {
  const explicit = process.argv[2] || process.env.UNITY_MCP_PROJECT;
  if (explicit) return findSessionUnder(explicit);

  let dir = process.cwd();
  for (let i = 0; i < 6; i++) {
    const found = findSessionUnder(dir);
    if (found) return found;
    const parent = path.dirname(dir);
    if (parent === dir) break;
    dir = parent;
  }
  return null;
}

/** 读取会话。每次调用都重新读盘，保证 Unity 侧变化能立刻生效。 */
function loadSession() {
  const sessionPath = resolveSessionPath();
  if (!sessionPath || !fs.existsSync(sessionPath)) {
    throw new Error(
      "未找到 Unity 桥接会话文件。请确认：① Unity 已打开该工程；② 菜单 Tools/MCP 桥接/启动桥接 已执行；" +
        "③ 网关第二个参数是工程根目录（菜单 Tools/MCP 桥接/复制客户端配置 会生成完整配置）。"
    );
  }

  let session;
  try {
    session = JSON.parse(fs.readFileSync(sessionPath, "utf8"));
  } catch (e) {
    throw new Error(`会话文件解析失败: ${sessionPath} (${e.message})`);
  }

  if (!session.port || !session.token) {
    throw new Error("会话文件缺少 port/token 字段，请在 Unity 里重启桥接");
  }
  return session;
}

/** 向 Unity 桥接发一次 JSON-RPC。 */
async function rpc(method, params = {}) {
  const session = loadSession();
  const url = `http://127.0.0.1:${session.port}${session.rpcPath || "/mcp"}`;
  const headerName = session.authHeader || "X-UnityMcp-Token";
  const id = ++idSequence;

  debug("rpc ->", url, method, "auth:", headerName);

  let response;
  try {
    response = await fetch(url, {
      method: "POST",
      headers: {
        "Content-Type": "application/json",
        [headerName]: session.token,
      },
      body: JSON.stringify({ jsonrpc: "2.0", id, method, params }),
      signal: AbortSignal.timeout(REQUEST_TIMEOUT_MS),
    });
  } catch (e) {
    throw new Error(
      `无法连接 Unity 桥接 (${url})：${e.message}。检查 Unity 是否运行、桥接是否已启动。`
    );
  }

  if (!response.ok) {
    throw new Error(`Unity 桥接返回 HTTP ${response.status}（鉴权头不符时通常是 401：确认 Unity 侧配置已保存并重启桥接）`);
  }

  const json = await response.json();
  if (json.error) {
    throw new Error(json.error.message || "Unity 桥接返回错误");
  }
  return json.result;
}

/** 启动时探一次 server 名（只影响客户端显示，失败不影响功能）。 */
function detectServerName() {
  if (process.env.UNITY_MCP_SERVER_NAME) return process.env.UNITY_MCP_SERVER_NAME;
  try {
    const session = loadSession();
    return session.serverName || "unity-mcp";
  } catch {
    return "unity-mcp";
  }
}

const serverName = detectServerName();
debug("server name:", serverName);

const server = new Server(
  { name: serverName, version: "2.0.0" },
  { capabilities: { tools: {}, resources: {} } }
);

server.setRequestHandler(ListToolsRequestSchema, async () => {
  const result = await rpc("tools/list");
  return { tools: result.tools || [] };
});

server.setRequestHandler(CallToolRequestSchema, async (request) => {
  const name = request.params.name;
  const args = request.params.arguments || {};
  const result = await rpc("tools/call", { name, arguments: args });
  return result;
});

server.setRequestHandler(ListResourcesRequestSchema, async () => {
  const result = await rpc("resources/list");
  return { resources: result.resources || [] };
});

server.setRequestHandler(ReadResourceRequestSchema, async (request) => {
  const uri = request.params.uri;
  const result = await rpc("resources/read", { uri });
  return { contents: [{ uri, mimeType: "application/json", text: JSON.stringify(result, null, 2) }] };
});

async function main() {
  const transport = new StdioServerTransport();
  await server.connect(transport);
  debug("gateway ready");
}

main().catch((e) => {
  console.error(LOG_TAG, "启动失败:", e.message);
  process.exit(1);
});
