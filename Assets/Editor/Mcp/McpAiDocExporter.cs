using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace GameFramework.EditorTools.Mcp
{
    /// <summary>
    /// 导出「给 AI 用的 MCP 接入说明」Markdown。
    ///
    /// 为什么要有它：人和 AI 对接 MCP 时，缺的从来不是"能连"，而是"AI 不知道该怎么用"——
    /// 有哪些工具、参数叫什么、哪些是危险操作、连不上该看哪个文件。这份 MD 把这些固化下来，
    /// 且**工具清单是反射导出的真实清单**，不会和代码漂移。
    /// </summary>
    public static class McpAiDocExporter
    {
        public static string Export()
        {
            var cfg = McpProjectConfig.Instance;
            if (cfg == null)
            {
                EditorUtility.DisplayDialog("生成失败", "McpProjectConfig 加载失败", "知道了");
                return null;
            }

            // 保证工具清单是新的（可能还没启动过桥接）
            if (McpBridge.Tools.Count == 0) McpBridge.ScanTools();

            string path = Path.Combine(McpProjectConfig.ProjectRoot, McpProjectConfig.AssetDir,
                "MCP_" + cfg.projectId + "_接入说明.md").Replace('\\', '/');

            try
            {
                File.WriteAllText(path, Build(cfg), new UTF8Encoding(false));
                McpClientConfig.WriteSideFile();
                AssetDatabase.Refresh();
                Debug.Log("[MCP] AI 说明已生成: " + path);
                return path;
            }
            catch (Exception e)
            {
                EditorUtility.DisplayDialog("生成失败", e.Message, "知道了");
                return null;
            }
        }

        private static string Build(McpProjectConfig cfg)
        {
            var sb = new StringBuilder(8192);
            string now = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
            string gateway = cfg.GatewayFullPath;
            string clientJson = McpClientConfig.BuildJson();

            sb.AppendLine("# " + cfg.displayName + " · Unity MCP 接入说明（给 AI）");
            sb.AppendLine();
            sb.AppendLine("> 本文件由 Unity 菜单 `Tools/MCP 桥接/导出 AI 说明` 自动生成，可直接作为上下文发给 AI。");
            sb.AppendLine("> 生成时间：" + now + " ｜ 配置指纹：`" + Fingerprint(cfg) + "`");
            sb.AppendLine("> 工程路径：`" + McpProjectConfig.ProjectRoot.Replace('\\', '/') + "`");
            sb.AppendLine();

            sb.AppendLine("## 0. 一句话");
            sb.AppendLine();
            sb.AppendLine("你现在可以通过 MCP 直接读写这个 **Unity 编辑器**：查看场景层级、读控制台日志、查资源、");
            sb.AppendLine("读脚本/配置文本、触发编辑器菜单。数据来自运行中的 Unity，不是磁盘上的静态文件。");
            sb.AppendLine();

            sb.AppendLine("## 1. 连接参数");
            sb.AppendLine();
            sb.Append(cfg.ToSummaryTable());
            sb.AppendLine();
            sb.AppendLine("桥接只监听 `127.0.0.1`，不对外网开放。**端口可能因占用而向后漂移**，");
            sb.AppendLine("权威值永远以会话文件 `" + cfg.SessionFileRelative + "` 里的 `port` 为准。");
            sb.AppendLine();

            sb.AppendLine("## 2. 客户端配置（粘到 MCP 客户端的 `mcpServers` 下）");
            sb.AppendLine();
            sb.AppendLine("```json");
            sb.Append(clientJson.TrimEnd());
            sb.AppendLine();
            sb.AppendLine("```");
            sb.AppendLine();
            sb.AppendLine("首次使用需在网关目录装依赖：`cd \"" + gateway.Substring(0, Math.Max(0, gateway.LastIndexOf('/'))) + "\" && npm install`");
            sb.AppendLine();
            sb.AppendLine("WorkBuddy：写进 `~/.workbuddy/mcp.json`，再到连接器管理页右上角「自定义连接器」点**信任**。");
            sb.AppendLine();

            AppendTools(sb);
            AppendResources(sb, cfg);
            AppendRules(sb, cfg);
            AppendTroubleshoot(sb, cfg);

            return sb.ToString();
        }

        private static void AppendTools(StringBuilder sb)
        {
            var tools = McpBridge.Tools;
            bool dangerousOn = McpSettings.AllowDangerousTools;

            sb.AppendLine("## 3. 工具清单（共 " + tools.Count + " 个，反射导出）");
            sb.AppendLine();
            if (tools.Count == 0)
            {
                sb.AppendLine("> ⚠ 当前扫描到 0 个工具。先执行菜单 `Tools/MCP 桥接/启动桥接`，看 Console 的 `[MCP] 诊断` 行。");
                sb.AppendLine();
                return;
            }

            sb.AppendLine("| 工具名 | 类型 | 参数 | 说明 |");
            sb.AppendLine("|---|---|---|---|");
            foreach (var def in tools)
            {
                string type = def.Dangerous ? (dangerousOn ? "危险（已开启）" : "危险（未开启→调用会被拒）") : "只读";
                sb.Append("| `").Append(def.Name).Append("` | ").Append(type).Append(" | ");
                sb.Append(DescribeParams(def.InputSchema));
                sb.Append(" | ").Append(Clean(def.Description)).AppendLine(" |");
            }
            sb.AppendLine();

            if (!dangerousOn)
            {
                sb.AppendLine("> 危险工具默认拒绝。需要时由人在 Unity 菜单 `Tools/MCP 桥接/允许危险工具` 打开并重启桥接。");
                sb.AppendLine();
            }
        }

        private static void AppendResources(StringBuilder sb, McpProjectConfig cfg)
        {
            string p = McpSettings.ResourceUriPrefix;
            sb.AppendLine("## 4. 资源（resources/read）");
            sb.AppendLine();
            sb.AppendLine("| URI | 内容 |");
            sb.AppendLine("|---|---|");
            sb.AppendLine("| `" + p + "session` | 当前会话：端口、server 名、工程路径、Unity 版本、工具数 |");
            sb.AppendLine("| `" + p + "console` | 最近 500 条控制台日志（环形缓冲，域重载后才开始记录） |");
            sb.AppendLine("| `" + p + "editor` | 编辑器自检快照：桥接状态、编译/播放状态、构建目标、宏 |");
            sb.AppendLine();
        }

        private static void AppendRules(StringBuilder sb, McpProjectConfig cfg)
        {
            sb.AppendLine("## 5. 使用约束（请遵守）");
            sb.AppendLine();
            sb.AppendLine("1. **优先只读工具**：`" + McpSettings.ToolPrefix + "get_hierarchy` / `" +
                          McpSettings.ToolPrefix + "get_console_logs` / `" + McpSettings.ToolPrefix + "find_assets` 先摸清现场再动手。");
            sb.AppendLine("2. **危险工具未开启时调了会被拒**，不要反复重试——那是权限问题，不是网络问题。");
            sb.AppendLine("3. **单次调用超时 " + McpSettings.RequestTimeoutMs + " ms**；编辑器正在编译/构建/进 Play 时请求会排队，超时就等编译完再试。");
            sb.AppendLine("4. **不要用 `" + McpSettings.ToolPrefix + "run_menu_item` 执行删除/构建类菜单**，除非人明确要求。");
            sb.AppendLine("5. **不要改 `" + cfg.SessionFileRelative + "`**：那是桥接写的运行时状态。");
            sb.AppendLine("6. 工具清单以 `tools/list` 的实时返回为准；本文件是快照，配置变了会重新生成。");
            sb.AppendLine("7. 这是**本机开发工具**：所有操作作用于当前打开的 Unity 编辑器，会直接影响正在编辑的工程，");
            sb.AppendLine("   未经人确认不要做不可逆操作（删资源、改场景、覆盖脚本）。");
            sb.AppendLine();
        }

        private static void AppendTroubleshoot(StringBuilder sb, McpProjectConfig cfg)
        {
            sb.AppendLine("## 6. 排障");
            sb.AppendLine();
            sb.AppendLine("| 现象 | 检查 |");
            sb.AppendLine("|---|---|");
            sb.AppendLine("| 客户端报「未找到会话文件」 | Unity 是否打开、桥接是否启动（菜单 `Tools/MCP 桥接/启动桥接`）；网关第二个参数是否为工程根 |");
            sb.AppendLine("| 连上但工具数 0 | Console 里 `[MCP] 诊断` 行：程序集数 / 候选数 / 同名程序集实例数 |");
            sb.AppendLine("| 工具调用超时 | 编辑器在编译或构建；或调大 `requestTimeoutMs` |");
            sb.AppendLine("| 换端口后连不上 | 网关每次请求都重读会话文件，正常无需重启；仍有问题看 `" + cfg.SessionFileRelative + "` |");
            sb.AppendLine("| 重编译后连不上 | 域重载会重建会话文件，网关会自动跟随 |");
            sb.AppendLine("| 端口冲突 | 菜单 `Tools/MCP 桥接/① 一键配置` 里「换到空闲端口」 |");
            sb.AppendLine();
            sb.AppendLine("网关调试：启动客户端时设 `UNITY_MCP_DEBUG=1`，日志走 stderr，不污染 stdio 协议通道。");
            sb.AppendLine();
            sb.AppendLine("---");
            sb.AppendLine();
            sb.AppendLine("## 7. 机器可读摘要");
            sb.AppendLine();
            sb.AppendLine("```yaml");
            sb.AppendLine("mcp:");
            sb.AppendLine("  server_name: " + cfg.serverName);
            sb.AppendLine("  tool_prefix: " + (string.IsNullOrEmpty(cfg.ToolNamePrefix) ? "\"\"" : cfg.ToolNamePrefix));
            sb.AppendLine("  resource_prefix: " + cfg.ResourceUriPrefix);
            sb.AppendLine("  auth_header: " + cfg.authHeaderName);
            sb.AppendLine("  session_file: " + cfg.SessionFileRelative);
            sb.AppendLine("  gateway: " + cfg.GatewayFullPath);
            sb.AppendLine("  port_hint: " + cfg.port);
            sb.AppendLine("  request_timeout_ms: " + McpSettings.RequestTimeoutMs);
            sb.AppendLine("  allow_dangerous: " + (McpSettings.AllowDangerousTools ? "true" : "false"));
            sb.AppendLine("  tool_count: " + McpBridge.Tools.Count);
            sb.AppendLine("```");
        }

        // ---------------- 工具方法 ----------------

        private static string DescribeParams(Dictionary<string, object> schema)
        {
            if (schema == null) return "—";
            object propsObj;
            if (!schema.TryGetValue("properties", out propsObj)) return "—";

            var props = propsObj as Dictionary<string, object>;
            if (props == null || props.Count == 0) return "—";

            var req = new List<string>();
            object reqObj;
            if (schema.TryGetValue("required", out reqObj))
            {
                var list = reqObj as List<object>;
                if (list != null)
                    foreach (var r in list) if (r is string) req.Add((string)r);
            }

            var sb = new StringBuilder();
            foreach (var kv in props)
            {
                var meta = kv.Value as Dictionary<string, object>;
                string type = "string";
                if (meta != null)
                {
                    object t;
                    if (meta.TryGetValue("type", out t) && t is string) type = (string)t;
                }
                if (sb.Length > 0) sb.Append(", ");
                sb.Append(kv.Key).Append(':').Append(type);
                if (req.Contains(kv.Key)) sb.Append('*');
            }
            return sb.ToString();
        }

        private static string Clean(string text)
        {
            if (string.IsNullOrEmpty(text)) return string.Empty;
            return text.Replace("|", "\\|").Replace("\r", " ").Replace("\n", " ");
        }

        /// <summary>配置指纹：配置变了 AI 手里的 MD 就过期了，用它判断是否需要重新发。</summary>
        private static string Fingerprint(McpProjectConfig cfg)
        {
            string raw = cfg.projectId + "|" + cfg.serverName + "|" + cfg.toolPrefix + "|" +
                         cfg.resourceScheme + "|" + cfg.authHeaderName + "|" + cfg.port + "|" +
                         (McpSettings.AllowDangerousTools ? "1" : "0");
            int hash = 17;
            foreach (char c in raw) hash = unchecked(hash * 31 + c);
            return (hash & 0x7FFFFFFF).ToString("x8");
        }
    }
}
