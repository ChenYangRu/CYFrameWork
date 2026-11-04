using System.IO;
using UnityEditor;
using UnityEngine;

namespace GameFramework.EditorTools.Mcp
{
    /// <summary>
    /// MCP 桥接的编辑器菜单入口。
    /// 菜单根固定为 Tools/MCP 桥接/（MenuItem 路径是编译期常量，不能按工程动态改）；
    /// 工程身份（server 名 / 工具前缀 / 端口 / 鉴权头）全部来自 McpProjectConfig。
    /// </summary>
    public static class McpMenu
    {
        private const string MenuRoot = "Tools/MCP 桥接/";
        private const string MenuAutoStart = MenuRoot + "自动启动（域重载后）";
        private const string MenuAllowDangerous = MenuRoot + "允许危险工具";

        [MenuItem(MenuRoot + "① 一键配置（生成 Asset + AI 说明）", priority = 0)]
        public static void OpenSetup()
        {
            McpSetupWindow.Open();
        }

        [MenuItem(MenuRoot + "启动桥接", priority = 1)]
        public static void Start()
        {
            McpBridge.Start();
        }

        [MenuItem(MenuRoot + "停止桥接", priority = 2)]
        public static void Stop()
        {
            McpBridge.Stop();
        }

        [MenuItem(MenuRoot + "重启桥接", priority = 3)]
        public static void Restart()
        {
            McpBridge.Restart();
        }

        [MenuItem(MenuAutoStart, priority = 20)]
        public static void ToggleAutoStart()
        {
            McpSettings.AutoStart = !McpSettings.AutoStart;
        }

        [MenuItem(MenuAutoStart, true)]
        public static bool ToggleAutoStartValidate()
        {
            Menu.SetChecked(MenuAutoStart, McpSettings.AutoStart);
            return true;
        }

        [MenuItem(MenuAllowDangerous, priority = 21)]
        public static void ToggleAllowDangerous()
        {
            McpSettings.AllowDangerousTools = !McpSettings.AllowDangerousTools;
            McpBridge.Restart(); // 工具清单会变化，重启让会话文件同步
        }

        [MenuItem(MenuAllowDangerous, true)]
        public static bool ToggleAllowDangerousValidate()
        {
            Menu.SetChecked(MenuAllowDangerous, McpSettings.AllowDangerousTools);
            return true;
        }

        [MenuItem(MenuRoot + "复制客户端配置", priority = 40)]
        public static void CopyClientConfig()
        {
            string json = McpClientConfig.BuildJson();
            EditorGUIUtility.systemCopyBuffer = json;
            Debug.Log("[MCP] 客户端配置已复制到剪贴板:\n" + json);
        }

        [MenuItem(MenuRoot + "导出 AI 说明（Markdown）", priority = 41)]
        public static void ExportAiDoc()
        {
            string path = McpAiDocExporter.Export();
            if (string.IsNullOrEmpty(path)) return;

            string text = File.ReadAllText(path);
            bool copy = EditorUtility.DisplayDialog("已生成 AI 说明",
                "文件：\n" + path + "\n\n是否同时把全文复制到剪贴板？（可直接粘给 AI）", "复制并打开", "只打开");
            if (copy) EditorGUIUtility.systemCopyBuffer = text;
            EditorUtility.RevealInFinder(path);
        }

        [MenuItem(MenuRoot + "打印状态", priority = 42)]
        public static void PrintStatus()
        {
            var sb = new System.Text.StringBuilder();
            sb.Append("[MCP] 运行状态\n");
            sb.Append("  运行中: ").Append(McpBridge.IsRunning).Append('\n');
            sb.Append("  端口: ").Append(McpBridge.Port).Append('\n');
            sb.Append("  server 名: ").Append(McpSettings.ServerName).Append('\n');
            sb.Append("  工具前缀: ").Append(string.IsNullOrEmpty(McpSettings.ToolPrefix) ? "(无)" : McpSettings.ToolPrefix).Append('\n');
            sb.Append("  资源 URI: ").Append(McpSettings.ResourceUriPrefix).Append("session").Append('\n');
            sb.Append("  鉴权头: ").Append(McpSettings.AuthHeader).Append('\n');
            sb.Append("  会话文件: ").Append(McpBridge.SessionFilePath ?? "(未生成)").Append('\n');
            sb.Append("  工具数: ").Append(McpBridge.Tools.Count).Append('\n');
            sb.Append("  危险工具: ").Append(McpSettings.AllowDangerousTools ? "已开启" : "已关闭").Append('\n');
            sb.Append("  工具清单:\n");

            foreach (McpToolDefinition def in McpBridge.Tools)
            {
                if (def.Dangerous && !McpSettings.AllowDangerousTools) continue;
                sb.Append("    - ").Append(def.Name).Append(def.Dangerous ? " [危险]" : string.Empty).Append('\n');
            }

            Debug.Log(sb.ToString());
        }
    }

    /// <summary>
    /// 生成"可粘贴到 MCP 客户端"的 JSON 片段。
    /// ★ 刻意不自动改写用户的 ~/.workbuddy/mcp.json：那是全局配置，文本合并极易写出非法 JSON。
    ///   这里只复制到剪贴板 + 落一份 side file，由人决定贴到哪个客户端。
    /// </summary>
    public static class McpClientConfig
    {
        public const string SideFilePath = McpProjectConfig.AssetDir + "/mcp-client-config.json";

        public static string BuildJson()
        {
            var cfg = McpProjectConfig.Instance;
            string gateway = cfg.GatewayFullPath;
            string root = McpProjectConfig.ProjectRoot.Replace("\\", "/");
            string serverName = McpSettings.ServerName;

            return "{\n"
                   + "  \"mcpServers\": {\n"
                   + "    \"" + serverName + "\": {\n"
                   + "      \"command\": \"node\",\n"
                   + "      \"args\": [\n"
                   + "        \"" + gateway + "\",\n"
                   + "        \"" + root + "\"\n"
                   + "      ]\n"
                   + "    }\n"
                   + "  }\n"
                   + "}\n";
        }

        /// <summary>落一份 side file，方便查阅/进版本库</summary>
        public static string WriteSideFile()
        {
            try
            {
                string abs = Path.Combine(McpProjectConfig.ProjectRoot, SideFilePath);
                File.WriteAllText(abs, BuildJson(), new System.Text.UTF8Encoding(false));
                return abs.Replace('\\', '/');
            }
            catch (System.Exception e)
            {
                Debug.LogWarning("[MCP] 写客户端配置落盘失败: " + e.Message);
                return null;
            }
        }
    }
}
