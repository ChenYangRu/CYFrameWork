# 06 · MCP 接入（配置驱动版）

> 目标：**任何机器拉到这个框架，点一个按钮填几个字，就能把 AI 接进来**；配置落成 Unity Asset 可查阅；再导出一份 MD 直接丢给 AI 使用。

---

## 一、为什么是"配置驱动"

大多数 Unity MCP 桥接把工程标识写死在代码里，装到新工程要**手工替换 7 处**，漏一处就连不上。
本框架把这些全部收进一份 ScriptableObject，代码零改动：

| # | 写死在代码里的项 | 现在 |
|---|---|---|
| 1 | 命名空间 `UnityMcp.Editor` 要改名 | 固定 `GameFramework.EditorTools.Mcp`，不用改 |
| 2 | 工具名前缀 `"unity.` 要改 | 写进 Asset（`toolPrefix`），注册时自动加 |
| 3 | 资源 URI `unity://` 要改 | 写进 Asset（`resourceScheme`） |
| 4 | 菜单根 `Tools/MCP 桥接/` 要改 | 固定（菜单路径是编译期常量，改不了；靠窗口标题区分工程） |
| 5 | 鉴权头 `X-UnityMcp-Token` 要改 | 写进 Asset（`authHeaderName`），网关从 `session.json` 读 |
| 6 | 会话目录 `Library/UnityMcp/` 要改 | 写进 Asset（`sessionDirName`），网关自动扫描 `Library/*/session.json` |
| 7 | 端口 `7890` / server 名 `unity-mcp` 要改 | 写进 Asset（`port` / `serverName`），网关启动时读 |

**结论**：代码零改动，换工程 = 换一份 `.asset`。

---

## 二、一键配置（3 步）

### 1. 打开配置窗口

菜单 `Tools/MCP 桥接/① 一键配置（生成 Asset + AI 说明）`

窗口里填：

| 字段 | 说明 | 默认 |
|---|---|---|
| 项目 ID | 小写字母/数字/`-`/`_`，用于派生全部标识 | `gameframework` |
| 显示名 | 给人看的名字 | 同项目 ID |
| server 名 | MCP 客户端里显示的 server 名，多工程靠它区分 | `<id>-unity` |
| 工具前缀 | 工具名会变成 `<前缀>.get_hierarchy` | `<id>` |
| 资源 URI 协议 | 资源 URI 变成 `<协议>://session` | `<id>` |
| 鉴权头名 | 本地握手头 | `X-<PascalId>-Token` |
| 起始端口 | 被占用自动向后探测 20 个 | `7890`（本仓库 Asset 预置 `7893`，避免与同机其它工程冲突） |
| 域重载后自动启动 | 重编译后自动拉起桥接 | 开 |
| 允许危险工具 | 改工程/触发构建类工具 | 关 |

点「按项目 ID 自动派生其余标识」可一键填好除端口外的全部字段；点「换到空闲端口」自动避让冲突。

### 2. 保存并重启桥接

点 **② 保存并重启桥接**。此时会：

- 写入 `Assets/Editor/Mcp/McpProjectConfig.asset`（可选中查看/进版本库）
- 刷新配置缓存
- 重启监听，重新生成 `Library/<会话目录>/session.json`（里面带上 `serverName` / `authHeader` / `toolPrefix` / `resourceUriPrefix`）

### 3. 生成 AI 说明

点 **③ 生成 AI 说明（MD）**。产出：

| 文件 | 用途 |
|---|---|
| `Assets/Editor/Mcp/MCP_<id>_接入说明.md` | **给 AI 的完整说明**，含工具清单/参数/约束/排障，自动复制到剪贴板 |
| `Assets/Editor/Mcp/mcp-client-config.json` | 客户端配置片段，可查看/进库 |

MD 里的工具清单是**反射导出的真实清单**，不会与代码漂移；配置变了重新生成即可（带「配置指纹」，便于判断手里的 MD 是否过期）。

---

## 三、把 AI 接进来

1. **装网关依赖**（每台机器一次）：

```bash
cd "<工程根>/Assets/Editor/Mcp/Gateway~"
npm install
```

2. **复制客户端配置**：菜单 `Tools/MCP 桥接/复制客户端配置`，粘到：

| 客户端 | 位置 |
|---|---|
| WorkBuddy | `~/.workbuddy/mcp.json` 的 `mcpServers` 下 → 连接器管理页右上角「自定义连接器」点**信任** |
| Claude Desktop | `Settings → Developer → Edit Config` |
| Cursor | `.cursor/mcp.json` |

片段形如（路径自动填好，server 名来自 Asset）：

```json
{
  "mcpServers": {
    "mygame-unity": {
      "command": "node",
      "args": [
        "<工程根>/Assets/Editor/Mcp/Gateway~/index.js",
        "<工程根>"
      ]
    }
  }
}
```

3. **把 MD 发给 AI**：菜单 `导出 AI 说明` 生成的 MD 全文已在剪贴板，直接粘进对话即可。AI 会知道：有哪些工具、参数名、哪些是危险操作、连不上看哪个文件。

---

## 四、工具清单（当前 13 个）

| 只读 | 危险（默认关闭） |
|---|---|
| `<前缀>.get_hierarchy` | `<前缀>.create_gameobject` |
| `<前缀>.find_gameobject` | `<前缀>.clear_console_logs` |
| `<前缀>.get_components` | `<前缀>.run_menu_item` |
| `<前缀>.get_console_logs` | `<前缀>.set_play_mode` |
| `<前缀>.find_assets` | `<前缀>.refresh_assets` |
| `<前缀>.read_text_file` | |
| `<前缀>.list_directory` | |
| `<前缀>.get_editor_state` | |

资源：`<协议>://session`、`<协议>://console`、`<协议>://editor`。

### 加一个新工具

在 `Assets/Editor/Mcp/Tools/` 下任意静态类里写：

```csharp
[McpTool("my_tool", "这个工具做什么")]
public static object MyTool(
    [McpParam("资源路径，如 Assets/Scenes/Main.unity", true)] string path)
{
    return new Dictionary<string, object> { { "ok", true } };
}
```

- **不要写前缀**（框架会按 Asset 自动加），写了前缀也不会重复加。
- 参数只支持 `string / int / float / bool`，或单个 `Dictionary<string, object>` 承接原始入参。
- 改完 Unity 重编译 → 桥接自动重启 → `tools/list` 立刻能看到。

---

## 五、数据流

```
AI 客户端 ──stdio── Gateway~/index.js ──HTTP 127.0.0.1:<port>── McpBridge(C#) ──主线程队列── Unity API
                          │                                            │
                          └── 每次请求重读 Library/<dir>/session.json ──┘
                              取 port / token / authHeader / serverName
```

- 网关**不缓存**会话：Unity 重启、换端口、域重载后网关无需重启。
- 桥接只绑 `127.0.0.1`，**不要改成 0.0.0.0**。
- 工具全部在 `EditorApplication.update` 里串行执行；编辑器编译/构建中请求会排队。

---

## 六、排障

| 现象 | 检查 |
|---|---|
| 客户端报「未找到会话文件」 | Unity 是否打开、`Tools/MCP 桥接/启动桥接` 是否执行、网关第二个参数是否工程根 |
| HTTP 401 | Unity 侧改过鉴权头但没重启桥接 → 菜单「保存并重启桥接」 |
| 连上但工具数 0 | Console 的 `[MCP] 诊断`：程序集数 / 候选数 / 同名程序集实例数。常见原因：工具类不是 `static class`，或方法不是 `public static` |
| 工具名缺前缀 | 桥接启动时读的是旧缓存 → 重启桥接 |
| 端口冲突 | 一键配置窗口「换到空闲端口」 |
| 换工程后连错地方 | 两个工程端口/会话目录撞了 → 各自改 server 名与端口 |

调试网关：启动客户端时设 `UNITY_MCP_DEBUG=1`，日志走 stderr，不污染 stdio 协议通道。

---

## 七、设计上的三条取舍

1. **不自动改写 `~/.workbuddy/mcp.json`**：那是全局配置，文本合并极易写出非法 JSON。只复制到剪贴板 + 落 side file，由人决定贴哪里。
2. **token 不进 Asset**：本机凭据存 EditorPrefs，避免随仓库泄漏；MD 里也不输出明文。
3. **菜单根不改名**：`MenuItem` 路径是编译期常量。多工程同时开时靠窗口标题 / server 名 / 端口区分，而不是靠菜单路径。
