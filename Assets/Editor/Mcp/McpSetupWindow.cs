using System.Net;
using System.Net.Sockets;
using UnityEditor;
using UnityEngine;

namespace GameFramework.EditorTools.Mcp
{
    /// <summary>
    /// MCP 一键配置窗口。
    ///
    /// 解决的问题：基线装到新工程要手工改 7 处标识（命名空间/工具前缀/资源 URI/鉴权头/
    /// 会话目录/端口/server 名），漏一个就连不上。现在改成：
    ///   打开窗口 → 填工程标识 → 点「保存并重启」→ 点「生成 AI 说明」→ 把 MD 丢给 AI。
    ///
    /// 产出物：
    ///   ① Assets/Editor/Mcp/McpProjectConfig.asset   （Unity Asset，可查阅/可进库）
    ///   ② Assets/Editor/Mcp/mcp-client-config.json   （客户端配置片段）
    ///   ③ Assets/Editor/Mcp/MCP_<id>_接入说明.md     （给 AI 的说明，含工具清单）
    /// </summary>
    public class McpSetupWindow : EditorWindow
    {
        private string _projectId;
        private string _displayName;
        private string _serverName;
        private string _toolPrefix;
        private string _resourceScheme;
        private string _authHeader;
        private int _port;
        private bool _autoStart;
        private bool _allowDangerous;

        private Vector2 _scroll;
        private string _portHint = string.Empty;
        private bool _dirty;

        public static void Open()
        {
            var win = GetWindow<McpSetupWindow>(false, "MCP 一键配置", true);
            win.minSize = new Vector2(560, 640);
            win.Reload();
            win.Show();
        }

        private void Reload()
        {
            var cfg = McpProjectConfig.Instance;
            _projectId = cfg.projectId;
            _displayName = cfg.displayName;
            _serverName = cfg.serverName;
            _toolPrefix = cfg.toolPrefix;
            _resourceScheme = cfg.resourceScheme;
            _authHeader = cfg.authHeaderName;
            _port = cfg.port;
            _autoStart = cfg.autoStart;
            _allowDangerous = cfg.allowDangerousTools;
            _dirty = false;
            _portHint = string.Empty;
        }

        private void OnGUI()
        {
            _scroll = EditorGUILayout.BeginScrollView(_scroll);

            EditorGUILayout.HelpBox(
                "同一份 MCP 代码适配任意工程：这里填的标识会写进 McpProjectConfig.asset，\n" +
                "代码不需要任何改动。多个 Unity 工程同时开时，靠 server 名与端口区分。",
                MessageType.Info);

            EditorGUILayout.Space(6);
            EditorGUILayout.LabelField("工程标识", EditorStyles.boldLabel);
            EditorGUI.BeginChangeCheck();
            _projectId = EditorGUILayout.TextField("项目 ID（小写）", _projectId);
            _displayName = EditorGUILayout.TextField("显示名", _displayName);
            if (EditorGUI.EndChangeCheck()) _dirty = true;

            if (GUILayout.Button("按项目 ID 自动派生其余标识", GUILayout.Height(24)))
            {
                var tmp = ScriptableObject.CreateInstance<McpProjectConfig>();
                tmp.projectId = _projectId;
                tmp.DeriveFromProject();
                _serverName = tmp.serverName;
                _toolPrefix = tmp.toolPrefix;
                _resourceScheme = tmp.resourceScheme;
                _authHeader = tmp.authHeaderName;
                DestroyImmediate(tmp);
                _dirty = true;
            }

            EditorGUILayout.Space(6);
            EditorGUILayout.LabelField("MCP 客户端", EditorStyles.boldLabel);
            EditorGUI.BeginChangeCheck();
            _serverName = EditorGUILayout.TextField("server 名", _serverName);
            _toolPrefix = EditorGUILayout.TextField("工具前缀", _toolPrefix);
            _resourceScheme = EditorGUILayout.TextField("资源 URI 协议", _resourceScheme);
            _authHeader = EditorGUILayout.TextField("鉴权头名", _authHeader);
            if (EditorGUI.EndChangeCheck()) _dirty = true;

            EditorGUILayout.LabelField("工具名示例", McpPreview.Prefix(_toolPrefix) + "get_hierarchy");
            EditorGUILayout.LabelField("资源 URI 示例", McpPreview.Scheme(_resourceScheme) + "://session");

            EditorGUILayout.Space(6);
            EditorGUILayout.LabelField("网络与行为", EditorStyles.boldLabel);
            EditorGUI.BeginChangeCheck();
            _port = EditorGUILayout.IntField("起始端口", _port);
            _autoStart = EditorGUILayout.Toggle("域重载后自动启动", _autoStart);
            _allowDangerous = EditorGUILayout.Toggle("允许危险工具", _allowDangerous);
            if (EditorGUI.EndChangeCheck()) _dirty = true;

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("检测端口占用"))
                {
                    if (IsPortFree(_port)) _portHint = "✓ 端口 " + _port + " 可用";
                    else
                    {
                        int found = FindFreePort(_port, 20);
                        _portHint = found > 0
                            ? "✗ 端口 " + _port + " 被占用，建议改用 " + found
                            : "✗ 从 " + _port + " 起 20 个端口都被占用";
                    }
                }
                if (GUILayout.Button("换到空闲端口"))
                {
                    int found = FindFreePort(_port, 20);
                    if (found > 0) { _port = found; _portHint = "已改为 " + found; _dirty = true; }
                    else _portHint = "找不到空闲端口，请手工指定";
                }
            }
            if (!string.IsNullOrEmpty(_portHint)) EditorGUILayout.HelpBox(_portHint, MessageType.None);

            EditorGUILayout.Space(8);
            EditorGUILayout.LabelField("路径（自动计算）", EditorStyles.boldLabel);
            var cfgPreview = McpProjectConfig.Instance;
            EditorGUILayout.SelectableLabel("会话文件: <工程根>/" + cfgPreview.SessionFileRelative,
                EditorStyles.textField, GUILayout.Height(18));
            EditorGUILayout.SelectableLabel("网关脚本: " + cfgPreview.GatewayFullPath,
                EditorStyles.textField, GUILayout.Height(18));

            EditorGUILayout.Space(8);
            EditorGUILayout.LabelField("桥接状态", EditorStyles.boldLabel);
            EditorGUILayout.LabelField("运行中", McpBridge.IsRunning ? "是（端口 " + McpBridge.Port + "）" : "否");
            EditorGUILayout.LabelField("工具数", McpBridge.Tools.Count.ToString());

            EditorGUILayout.Space(10);
            if (_dirty)
                EditorGUILayout.HelpBox("有未保存的修改", MessageType.Warning);

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("① 保存配置（写 Asset）", GUILayout.Height(32)))
                {
                    Save(false);
                }
                if (GUILayout.Button("② 保存并重启桥接", GUILayout.Height(32)))
                {
                    Save(true);
                }
            }

            EditorGUILayout.Space(6);
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("③ 生成 AI 说明（MD）", GUILayout.Height(32)))
                {
                    Save(false);
                    string path = McpAiDocExporter.Export();
                    if (!string.IsNullOrEmpty(path))
                    {
                        EditorGUIUtility.systemCopyBuffer = System.IO.File.ReadAllText(path);
                        EditorUtility.DisplayDialog("已生成",
                            "MD 已生成并复制到剪贴板：\n" + path + "\n\n直接粘给 AI 即可。", "好");
                        EditorUtility.RevealInFinder(path);
                    }
                }
                if (GUILayout.Button("只复制客户端 JSON", GUILayout.Height(32)))
                {
                    string json = McpClientConfig.BuildJson();
                    EditorGUIUtility.systemCopyBuffer = json;
                    McpClientConfig.WriteSideFile();
                    EditorUtility.DisplayDialog("已复制", json, "好");
                }
            }

            EditorGUILayout.Space(6);
            if (GUILayout.Button("清空本机覆盖（回到 Asset 里的值）"))
            {
                McpSettings.ClearLocalOverrides();
                Reload();
                McpBridge.Restart();
            }

            EditorGUILayout.Space(8);
            EditorGUILayout.HelpBox(
                "落地顺序：\n" +
                "1) 保存并重启桥接（会话文件才会带上新的 server 名/前缀）\n" +
                "2) 复制客户端 JSON → 粘到 ~/.workbuddy/mcp.json 的 mcpServers 下\n" +
                "3) 生成 AI 说明 MD → 发给 AI\n" +
                "4) 首次使用需在 Gateway~ 目录执行 npm install",
                MessageType.None);

            EditorGUILayout.EndScrollView();
        }

        private void Save(bool restart)
        {
            string id = NormalizeId(_projectId);
            if (id != _projectId)
            {
                EditorUtility.DisplayDialog("项目 ID 非法",
                    "只允许小写字母/数字/'-'/'_'，已自动修正为：" + id, "好");
            }

            var cfg = McpProjectConfig.Instance;
            cfg.projectId = id;
            cfg.displayName = string.IsNullOrEmpty(_displayName) ? id : _displayName;
            cfg.serverName = _serverName;
            cfg.toolPrefix = _toolPrefix;
            cfg.resourceScheme = _resourceScheme;
            cfg.authHeaderName = _authHeader;
            cfg.port = _port;
            cfg.autoStart = _autoStart;
            cfg.allowDangerousTools = _allowDangerous;

            cfg.Save();
            AssetDatabase.SaveAssets();

            // 窗口是"权威配置"入口：清掉本机的 EditorPrefs 覆盖，让 Asset 生效
            McpSettings.ClearLocalOverrides();
            if (restart) McpBridge.Restart();

            _dirty = false;
            Debug.Log("[MCP] 配置已保存: " + McpProjectConfig.AssetPath);
        }

        private static string NormalizeId(string raw)
        {
            if (string.IsNullOrEmpty(raw)) return "gameframework";
            var sb = new System.Text.StringBuilder(raw.Length);
            foreach (char c in raw)
            {
                if (char.IsLetterOrDigit(c) || c == '-' || c == '_') sb.Append(char.ToLowerInvariant(c));
            }
            return sb.Length == 0 ? "gameframework" : sb.ToString();
        }

        private static bool IsPortFree(int port)
        {
            if (port <= 0 || port > 65535) return false;
            TcpListener listener = null;
            try
            {
                listener = new TcpListener(IPAddress.Loopback, port);
                listener.Start();
                return true;
            }
            catch (SocketException)
            {
                return false;
            }
            finally
            {
                if (listener != null) { try { listener.Stop(); } catch { } }
            }
        }

        private static int FindFreePort(int start, int count)
        {
            for (int i = 0; i < count; i++)
            {
                int p = start + i;
                if (p > 65535) break;
                if (IsPortFree(p)) return p;
            }
            return -1;
        }
    }

    /// <summary>窗口里的即时预览用（不依赖 Asset 是否已保存）。</summary>
    internal static class McpPreview
    {
        public static string Prefix(string raw)
        {
            if (string.IsNullOrEmpty(raw)) return string.Empty;
            return raw + ".";
        }

        public static string Scheme(string raw)
        {
            return string.IsNullOrEmpty(raw) ? "framework" : raw;
        }
    }
}
