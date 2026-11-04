# Unity MCP 网关

把 Unity 编辑器暴露成 MCP Server，让 AI 客户端（Claude Code / Cursor / WorkBuddy 等）直接读写工程。

目录名以 `~` 结尾，Unity 不会导入它，因此 `node_modules` 不会进资源数据库。

## 为什么是两段式

| 层 | 位置 | 职责 |
|---|---|---|
| MCP Server（stdio） | 本目录 `index.js` | 说 MCP 协议，转发请求 |
| 桥接服务（HTTP） | Unity 编辑器内 `McpBridge.cs` | 执行 Unity API，产出工具清单 |

Unity 编辑器是 GUI 常驻进程，MCP 客户端期望自己拉起子进程走 stdio，两者对不上，所以必须有一层网关做中转。
好处是：协议升级只改网关，工具永远在 C# 侧定义，加工具不用碰 Node 代码。

## 安装

```bash
cd "Assets/Editor/Mcp/Gateway~"
npm install
```

## 运行方式

```bash
node index.js <Unity 工程根目录>
```

工程根目录指包含 `Assets/` 和 `Library/` 的那一级。网关每次请求都会重读
`Library/UnityMcp/session.json`，所以 Unity 重启、换端口、脚本重编译后**网关不用重启**。

可选环境变量：

| 变量 | 默认 | 说明 |
|---|---|---|
| `UNITY_MCP_PROJECT` | — | 工程根目录，等价于命令行第一个参数 |
| `UNITY_MCP_TIMEOUT_MS` | 20000 | 单次请求超时 |
| `UNITY_MCP_DEBUG` | 0 | 置 1 时把调试日志打到 stderr |

调试（会在浏览器打开 MCP Inspector）：

```bash
npx @modelcontextprotocol/inspector node index.js "<工程根>"
```

## 安全边界

- 桥接只监听 `127.0.0.1`，不对外网开放
- 每个请求都要带 `X-UnityMcp-Token`，令牌存在 Unity 的 EditorPrefs 与会话文件里，随会话生成
- 标了 `Dangerous = true` 的工具（改场景、触发构建、播放模式）默认不出现在工具清单里，
  需要在 `Tools/MCP 桥接/允许危险工具` 里显式打开

绝不要为了图方便把桥接改成 `0.0.0.0`，那等于把编辑器的控制权开放给局域网。
