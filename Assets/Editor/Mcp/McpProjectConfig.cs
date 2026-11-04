using System;
using UnityEditor;
using UnityEngine;

namespace GameFramework.EditorTools.Mcp
{
    /// <summary>
    /// MCP 接入配置（Unity Asset，可查阅、可进版本库）。
    ///
    /// 设计目标：**同一份 MCP 代码适配任意工程/任意机器**。
    /// 基线的做法是装到新工程后手工替换 7 处硬编码标识（命名空间/工具前缀/资源 URI/鉴权头/
    /// 会话目录/端口/server 名），漏一个就连不上。这里把这些全部收进本资产：
    ///   代码零改动 → 生成 asset → 一键导出 MD 给 AI。
    ///
    /// ★ 唯一不进本资产的是 token：那是本机凭据，存 EditorPrefs，避免随仓库泄漏。
    /// </summary>
    [CreateAssetMenu(fileName = "McpProjectConfig", menuName = "框架/MCP 工程配置", order = 1)]
    public class McpProjectConfig : ScriptableObject
    {
        public const string AssetDir = "Assets/Editor/Mcp";
        public const string AssetPath = AssetDir + "/McpProjectConfig.asset";

        [Header("工程标识")]
        [Tooltip("小写短标识，用于目录名与默认派生，例：mygame")]
        public string projectId = "gameframework";

        [Tooltip("给人看的工程名，例：MyGame")]
        public string displayName = "GameFramework";

        [Header("MCP 客户端")]
        [Tooltip("客户端里显示的 server 名，多工程共存时靠它区分")]
        public string serverName = "gameframework-unity";

        [Tooltip("工具名前缀。工具注册时会自动加，工具代码里只写 get_hierarchy")]
        public string toolPrefix = "gameframework";

        [Tooltip("资源 URI 协议头，例：mygame → mygame://session")]
        public string resourceScheme = "gameframework";

        [Tooltip("本地握手用 HTTP 头名，网关与会话文件里都会用到")]
        public string authHeaderName = "X-GameFramework-Token";

        [Header("网络")]
        [Tooltip("起始端口，被占用时自动向后探测 maxPortProbe 个")]
        public int port = 7890;

        public int maxPortProbe = 20;

        [Tooltip("单次工具调用超时（毫秒）")]
        public int requestTimeoutMs = 15000;

        [Header("运行行为")]
        public bool autoStart = true;

        [Tooltip("危险工具（改工程/触发构建）默认关闭，开启后需重启桥接")]
        public bool allowDangerousTools = false;

        [Header("路径")]
        [Tooltip("会话文件目录名，实际路径 = <工程根>/Library/<此名>/session.json")]
        public string sessionDirName = "McpBridge";

        [Tooltip("网关脚本相对工程根的路径")]
        public string gatewayScriptRelative = "Assets/Editor/Mcp/Gateway~/index.js";

        // ---------------- 派生值 ----------------

        /// <summary>会话目录（相对工程根），例：Library/McpBridge</summary>
        public string SessionRelativeDir
        {
            get { return "Library/" + Normalize(SessionDirName, "McpBridge"); }
        }

        /// <summary>资源 URI 前缀，例：mygame://</summary>
        public string ResourceUriPrefix
        {
            get { return Normalize(resourceScheme, "gameframework") + "://"; }
        }

        /// <summary>工具名前缀（含末尾点），空前缀返回空串</summary>
        public string ToolNamePrefix
        {
            get
            {
                string p = Normalize(toolPrefix, string.Empty);
                return p.Length == 0 ? string.Empty : p + ".";
            }
        }

        public string SessionDirName { get { return Normalize(sessionDirName, "McpBridge"); } }

        public string SessionFileRelative
        {
            get { return SessionRelativeDir + "/session.json"; }
        }

        /// <summary>工程根（Application.dataPath 的父目录）</summary>
        public static string ProjectRoot
        {
            get { return System.IO.Path.GetDirectoryName(Application.dataPath) ?? "."; }
        }

        public string GatewayFullPath
        {
            get { return System.IO.Path.GetFullPath(System.IO.Path.Combine(ProjectRoot, gatewayScriptRelative)).Replace('\\', '/'); }
        }

        // ---------------- 加载 / 保存 ----------------

        private static McpProjectConfig _instance;

        /// <summary>
        /// 当前配置。asset 不存在时自动创建一份默认的（保证新机器拉下框架就能用）。
        /// ★ 只能在主线程调用（AssetDatabase 限制）。
        /// </summary>
        public static McpProjectConfig Instance
        {
            get
            {
                if (_instance != null) return _instance;

                _instance = AssetDatabase.LoadAssetAtPath<McpProjectConfig>(AssetPath);
                if (_instance == null)
                {
                    _instance = CreateInstance<McpProjectConfig>();
                    _instance.DeriveFromProject();

                    // 域重载早期 AssetDatabase 可能还没就绪，失败也不致命：
                    // 退化成内存实例，桥接照样能跑，之后从窗口保存时会补写 asset。
                    try
                    {
                        System.IO.Directory.CreateDirectory(System.IO.Path.Combine(ProjectRoot, AssetDir));
                        AssetDatabase.Refresh();
                        AssetDatabase.CreateAsset(_instance, AssetPath);
                        AssetDatabase.SaveAssets();
                        Debug.Log("[MCP] 已创建默认配置: " + AssetPath);
                    }
                    catch (Exception e)
                    {
                        Debug.LogWarning("[MCP] 创建默认配置失败（已退化为内存配置）: " + e.Message);
                    }
                }
                return _instance;
            }
        }

        public static void Reload()
        {
            _instance = null;
        }

        public void Save()
        {
            // 若之前退化成内存实例（域重载早期创建失败），这里补写 asset
            var existing = AssetDatabase.LoadAssetAtPath<McpProjectConfig>(AssetPath);
            if (existing == null)
            {
                System.IO.Directory.CreateDirectory(System.IO.Path.Combine(ProjectRoot, AssetDir));
                AssetDatabase.Refresh();
                AssetDatabase.CreateAsset(this, AssetPath);
            }

            EditorUtility.SetDirty(this);
            AssetDatabase.SaveAssets();
        }

        /// <summary>按工程名派生一套合理默认值（projectId 变了才会覆盖）</summary>
        public void DeriveFromProject()
        {
            // projectId / 前缀 / URI 一律小写（工具名与 URI 大小写敏感，统一小写最省事）；
            // 会话目录保留 Pascal 形式，方便人肉在 Library 下认出是哪个工程。
            string id = Normalize(projectId, "gameframework").ToLowerInvariant();
            projectId = id;
            serverName = id + "-unity";
            toolPrefix = id;
            resourceScheme = id;
            authHeaderName = "X-" + Pascal(id) + "-Token";
            sessionDirName = "Mcp" + Pascal(id);
        }

        /// <summary>把配置复制成一段可直接粘给 AI 的摘要（不含 token）</summary>
        public string ToSummaryTable()
        {
            var sb = new System.Text.StringBuilder();
            sb.AppendLine("| 项 | 值 |");
            sb.AppendLine("|---|---|");
            sb.AppendLine("| 工程标识 | `" + projectId + "` |");
            sb.AppendLine("| 显示名 | " + displayName + " |");
            sb.AppendLine("| 客户端 server 名 | `" + serverName + "` |");
            sb.AppendLine("| 工具名前缀 | `" + (ToolNamePrefix.Length == 0 ? "(无)" : ToolNamePrefix) + "` |");
            sb.AppendLine("| 资源 URI 前缀 | `" + ResourceUriPrefix + "` |");
            sb.AppendLine("| 鉴权头 | `" + authHeaderName + "` |");
            sb.AppendLine("| 起始端口 | `" + port + "`（被占用自动向后探测 " + maxPortProbe + " 个） |");
            sb.AppendLine("| 会话文件 | `<工程根>/" + SessionFileRelative + "` |");
            sb.AppendLine("| 网关脚本 | `" + GatewayFullPath + "` |");
            sb.AppendLine("| 危险工具 | " + (allowDangerousTools ? "已开启" : "默认关闭") + " |");
            return sb.ToString();
        }

        // ---------------- 工具方法 ----------------

        /// <summary>
        /// 标识清洗：只保留字母/数字/'.'/'-'/'_'。
        /// ★ 保留大小写 —— 它同时用于会话目录名，强转小写会让 asset 里显示的和实际目录不一致。
        ///   需要小写的地方（projectId / toolPrefix）由调用方自己 ToLowerInvariant。
        /// </summary>
        private static string Normalize(string value, string fallback)
        {
            if (string.IsNullOrEmpty(value)) value = fallback;
            if (string.IsNullOrEmpty(value)) return string.Empty;

            var sb = new System.Text.StringBuilder(value.Length);
            foreach (char c in value)
            {
                if (char.IsLetterOrDigit(c) || c == '.' || c == '-' || c == '_') sb.Append(c);
            }
            return sb.Length == 0 ? fallback : sb.ToString();
        }

        private static string Pascal(string id)
        {
            if (string.IsNullOrEmpty(id)) return "Framework";
            var parts = id.Split(new[] { '-', '_', '.' }, StringSplitOptions.RemoveEmptyEntries);
            var sb = new System.Text.StringBuilder();
            foreach (var p in parts)
            {
                if (p.Length == 0) continue;
                sb.Append(char.ToUpperInvariant(p[0]));
                if (p.Length > 1) sb.Append(p.Substring(1));
            }
            return sb.Length == 0 ? "Framework" : sb.ToString();
        }
    }
}
