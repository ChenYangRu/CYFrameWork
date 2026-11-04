using System;
using UnityEditor;

namespace GameFramework.EditorTools.Mcp
{
    /// <summary>
    /// MCP 运行时配置缓存。数据来源分两层：
    ///   ① McpProjectConfig（Unity Asset，随仓库走，决定工程身份：前缀/URI/头名/端口）；
    ///   ② EditorPrefs（本机覆盖，只在端口冲突、临时开关危险工具时用，不随仓库走）。
    ///
    /// ★ 关键约束：EditorPrefs.Get* 与 AssetDatabase 都只能在主线程调用，而桥接的网络线程要读
    ///   端口/token/危险工具开关。所以这里统一"写走源、读走静态缓存"，缓存只在主线程 RefreshCache 时刷新。
    /// </summary>
    public static class McpSettings
    {
        public const int FallbackPort = 7890;
        public const int FallbackMaxPortProbe = 20;
        public const int FallbackRequestTimeoutMs = 15000;

        private const string KeyRootFallback = "GameFramework.Mcp.";
        private static string _keyPrefix = KeyRootFallback;

        private static string KeyAutoStart { get { return _keyPrefix + "AutoStart"; } }
        private static string KeyPort { get { return _keyPrefix + "Port"; } }
        private static string KeyAllowDangerous { get { return _keyPrefix + "AllowDangerous"; } }
        private static string KeyToken { get { return _keyPrefix + "Token"; } }

        private static bool _autoStart = true;
        private static bool _allowDangerous;
        private static int _port = FallbackPort;
        private static int _maxPortProbe = FallbackMaxPortProbe;
        private static int _requestTimeoutMs = FallbackRequestTimeoutMs;
        private static string _token = string.Empty;

        private static string _serverName = "gameframework-unity";
        private static string _toolPrefix = string.Empty;
        private static string _resourceScheme = "gameframework://";
        private static string _authHeader = "X-Gameframework-Token";
        private static string _sessionDirName = "McpGameframework";

        private static bool _loaded;
        private static McpProjectConfig _config;

        static McpSettings()
        {
            // 静态构造可能被网络线程触发（域重载后谁先访问谁触发）。
            // 这里尽力而为：非主线程会抛 UnityException，保持默认值，主线程随后会 RefreshCache。
            try { RefreshCache(); }
            catch (Exception) { }
        }

        /// <summary>从 Asset + EditorPrefs 重新载入全部配置到缓存。**必须在主线程调用**。</summary>
        public static void RefreshCache()
        {
            _config = McpProjectConfig.Instance;

            // EditorPrefs 在 Windows 上是「整机 + 该 Unity 版本共享」的，必须带工程前缀隔离
            _keyPrefix = KeyRootFallback + _config.projectId + ".";

            _autoStart = EditorPrefs.GetBool(KeyAutoStart, _config.autoStart);
            _port = EditorPrefs.GetInt(KeyPort, _config.port);
            _maxPortProbe = _config.maxPortProbe > 0 ? _config.maxPortProbe : FallbackMaxPortProbe;
            _requestTimeoutMs = _config.requestTimeoutMs > 0 ? _config.requestTimeoutMs : FallbackRequestTimeoutMs;
            _allowDangerous = EditorPrefs.GetBool(KeyAllowDangerous, _config.allowDangerousTools);

            _serverName = _config.serverName;
            _toolPrefix = _config.ToolNamePrefix;
            _resourceScheme = _config.ResourceUriPrefix;
            _authHeader = _config.authHeaderName;
            _sessionDirName = _config.SessionDirName;

            string token = EditorPrefs.GetString(KeyToken, string.Empty);
            if (string.IsNullOrEmpty(token))
            {
                token = Guid.NewGuid().ToString("N");
                EditorPrefs.SetString(KeyToken, token);
            }
            _token = token;

            _loaded = true;
        }

        /// <summary>配置是否已在主线程成功载入。</summary>
        public static bool Loaded { get { return _loaded; } }

        /// <summary>当前工程配置（可能为 null，仅主线程可用）。</summary>
        public static McpProjectConfig Config { get { return _config; } }

        // ---------------- 可被任意线程读取的缓存值 ----------------

        public static bool AutoStart
        {
            get { return _autoStart; }
            set { EditorPrefs.SetBool(KeyAutoStart, value); _autoStart = value; }
        }

        public static int Port
        {
            get { return _port; }
            set { EditorPrefs.SetInt(KeyPort, value); _port = value; }
        }

        public static int MaxPortProbe { get { return _maxPortProbe; } }

        public static int RequestTimeoutMs { get { return _requestTimeoutMs; } }

        public static bool AllowDangerousTools
        {
            get { return _allowDangerous; }
            set { EditorPrefs.SetBool(KeyAllowDangerous, value); _allowDangerous = value; }
        }

        /// <summary>客户端里显示的 server 名。</summary>
        public static string ServerName { get { return _serverName; } }

        /// <summary>工具名前缀（含末尾点，可能为空串）。</summary>
        public static string ToolPrefix { get { return _toolPrefix; } }

        /// <summary>资源 URI 前缀（含 "://"）。</summary>
        public static string ResourceUriPrefix { get { return _resourceScheme; } }

        /// <summary>握手 HTTP 头名。</summary>
        public static string AuthHeader { get { return _authHeader; } }

        /// <summary>会话文件目录名（Library 下）。</summary>
        public static string SessionDirName { get { return _sessionDirName; } }

        public static string Token
        {
            get
            {
                if (string.IsNullOrEmpty(_token))
                {
                    // 兜底：万一缓存没加载（非主线程访问），生成一个临时值但不写盘
                    _token = Guid.NewGuid().ToString("N");
                }
                return _token;
            }
        }

        /// <summary>重置令牌（怀疑泄漏 / 换机器时用）。必须在主线程调用。</summary>
        public static void RegenerateToken()
        {
            string token = Guid.NewGuid().ToString("N");
            EditorPrefs.SetString(KeyToken, token);
            _token = token;
        }

        /// <summary>清掉本机对端口/危险工具的覆盖，回到 Asset 里的值。</summary>
        public static void ClearLocalOverrides()
        {
            EditorPrefs.DeleteKey(KeyPort);
            EditorPrefs.DeleteKey(KeyAllowDangerous);
            EditorPrefs.DeleteKey(KeyAutoStart);
            RefreshCache();
        }

        /// <summary>配置变更后调用：重载缓存 + 让桥接按新配置重启。</summary>
        public static void ApplyAndRestart()
        {
            RefreshCache();
            McpBridge.Restart();
        }
    }
}
