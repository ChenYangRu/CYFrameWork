using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Text;
using System.Threading;
using UnityEditor;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace GameFramework.EditorTools.Mcp
{
    /// <summary>
    /// Unity 侧的 MCP 桥接服务。
    /// 对外只监听 127.0.0.1，提供 JSON-RPC 端点（MCP 语义子集），
    /// 真正的 stdio MCP Server 由 Gateway~/index.js 承担，本类只做本地端。
    ///
    /// ★ 所有工程标识（鉴权头 / 会话目录 / 资源 URI）都不写死，统一从
    ///   <see cref="McpProjectConfig"/> 经 <see cref="McpSettings"/> 缓存后读取，
    ///   换工程只换 asset，不动代码。
    /// </summary>
    [InitializeOnLoad]
    public static class McpBridge
    {
        private const string RpcPath = "/mcp";
        private const string HealthPath = "/health";
        private const string ProtocolVersion = "mcp-bridge/2";

        /// <summary>握手头名（工程可配）</summary>
        private static string TokenHeader { get { return McpSettings.AuthHeader; } }

        private static string ResSession { get { return McpSettings.ResourceUriPrefix + "session"; } }
        private static string ResConsole { get { return McpSettings.ResourceUriPrefix + "console"; } }
        private static string ResEditor { get { return McpSettings.ResourceUriPrefix + "editor"; } }

        private static readonly McpToolRegistry Registry = new McpToolRegistry();
        private static TcpListener _listener;
        private static Thread _acceptThread;
        private static volatile bool _running;
        private static int _port;
        private static string _sessionPath;

        static McpBridge()
        {
            // [InitializeOnLoad] 的静态构造在主线程，这里把 EditorPrefs 配置载入缓存，
            // 之后网络线程只读缓存，避免 "GetBool can only be called from the main thread"。
            McpSettings.RefreshCache();

            EditorApplication.update -= Pump;
            EditorApplication.update += Pump;
            AssemblyReloadEvents.beforeAssemblyReload -= OnBeforeReload;
            AssemblyReloadEvents.beforeAssemblyReload += OnBeforeReload;
            McpLogBuffer.Attach();

            // 延迟一帧再起，避免在域重载过程中抢资源
            EditorApplication.delayCall += () =>
            {
                if (McpSettings.AutoStart) Start();
            };
        }

        public static bool IsRunning
        {
            get { return _running; }
        }

        public static int Port
        {
            get { return _port; }
        }

        public static string SessionFilePath
        {
            get { return _sessionPath; }
        }

        public static IReadOnlyList<McpToolDefinition> Tools
        {
            get { return Registry.Tools; }
        }

        /// <summary>只重新扫描工具表（不启动监听）。导出文档 / 调试时用。</summary>
        public static void ScanTools()
        {
            Registry.Scan();
        }

        public static void Start()
        {
            if (_running) return;

            Registry.Scan();
            McpLogBuffer.Attach();

            int startPort = McpSettings.Port;
            for (int i = 0; i < McpSettings.MaxPortProbe; i++)
            {
                int candidate = startPort + i;
                try
                {
                    var listener = new TcpListener(IPAddress.Loopback, candidate);
                    listener.Start();
                    _listener = listener;
                    _port = candidate;
                    break;
                }
                catch (SocketException)
                {
                    // 端口被占用，试下一个
                }
            }

            if (_listener == null)
            {
                Debug.LogWarning("[MCP] 端口 " + startPort + " 起 " + McpSettings.MaxPortProbe + " 个都被占用，桥接未启动。");
                return;
            }

            _running = true;
            _acceptThread = new Thread(AcceptLoop) { IsBackground = true, Name = "UnityMcpAccept" };
            _acceptThread.Start();

            WriteSessionFile();
            Debug.Log("[MCP] 桥接已启动: http://127.0.0.1:" + _port + "  工具 " + Registry.Tools.Count + " 个");

            if (Registry.Tools.Count == 0)
            {
                var sb = new StringBuilder();
                sb.Append("[MCP] 扫描到 0 个工具 —— 诊断：程序集 ")
                    .Append(Registry.LastScanAssemblyCount).Append(" 个（取类型失败 ")
                    .Append(Registry.LastScanFailedAssemblyCount).Append(" 个），命中 [McpTool] 方法 ")
                    .Append(Registry.LastScanCandidateCount).Append(" 个。\n");

                Assembly self = typeof(McpBridge).Assembly;
                sb.Append("[MCP] 诊断: 本程序集=").Append(self.FullName).Append(" @ ").Append(self.Location).Append('\n');
                sb.Append("[MCP] 诊断: 特性类型 FullName=").Append(typeof(McpToolAttribute).FullName).Append('\n');

                int sameName = 0;
                foreach (Assembly asm in AppDomain.CurrentDomain.GetAssemblies())
                    if (asm.GetName().Name == self.GetName().Name) sameName++;
                sb.Append("[MCP] 诊断: 同名程序集实例数=").Append(sameName).Append('\n');

                // 用 typeof 取全名，避免把命名空间写死，换工程改命名空间后诊断依旧可用
                Type probe = self.GetType(typeof(SceneTools).FullName);
                sb.Append("[MCP] 诊断: 本程序集内 SceneTools 类型=").Append(probe != null ? "存在" : "缺失").Append('\n');
                if (probe != null)
                {
                    foreach (MethodInfo m in probe.GetMethods(BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly))
                    {
                        object[] attrs = m.GetCustomAttributes(false);
                        sb.Append("[MCP] 诊断:   ").Append(m.Name).Append(" 特性数=").Append(attrs.Length);
                        foreach (object a in attrs) sb.Append(" | ").Append(a.GetType().FullName);
                        sb.Append('\n');
                    }
                }

                Debug.LogWarning(sb.ToString());
            }
        }

        public static void Stop()
        {
            if (!_running) return;
            _running = false;

            try
            {
                if (_listener != null)
                {
                    _listener.Stop();
                    _listener = null;
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning("[MCP] 停止监听异常: " + e.Message);
            }

            if (_acceptThread != null && _acceptThread.IsAlive)
            {
                _acceptThread.Join(300);
                _acceptThread = null;
            }

            DeleteSessionFile();
        }

        public static void Restart()
        {
            Stop();
            Start();
        }

        private static void OnBeforeReload()
        {
            // 域重载会卸载 AppDomain，必须释放端口，否则重编译后端口泄漏
            Stop();
        }

        private static void Pump()
        {
            MainThreadDispatcher.Pump();
        }

        // ---------- 会话文件 ----------

        private static void WriteSessionFile()
        {
            try
            {
                string root = Path.GetDirectoryName(Application.dataPath);
                string dir = Path.Combine(root ?? ".", "Library", McpSettings.SessionDirName);
                Directory.CreateDirectory(dir);
                _sessionPath = Path.Combine(dir, "session.json");

                var payload = new Dictionary<string, object>
                {
                    { "protocol", ProtocolVersion },
                    { "port", _port },
                    { "token", McpSettings.Token },
                    { "rpcPath", RpcPath },
                    // ↓ 网关靠这几项自适应工程身份，不再写死 header / server 名
                    { "authHeader", McpSettings.AuthHeader },
                    { "serverName", McpSettings.ServerName },
                    { "toolPrefix", McpSettings.ToolPrefix },
                    { "resourceUriPrefix", McpSettings.ResourceUriPrefix },
                    { "sessionDir", McpSettings.SessionDirName },
                    { "requestTimeoutMs", McpSettings.RequestTimeoutMs },
                    { "projectName", Application.productName },
                    { "projectPath", root ?? string.Empty },
                    { "unityVersion", Application.unityVersion },
                    { "pid", Process.GetCurrentProcess().Id },
                    { "startedAt", DateTime.Now.ToString("o", CultureInfo.InvariantCulture) },
                    { "allowDangerousTools", McpSettings.AllowDangerousTools },
                    { "toolCount", Registry.Tools.Count },
                };
                File.WriteAllText(_sessionPath, McpJson.WritePretty(payload), new UTF8Encoding(false));
            }
            catch (Exception e)
            {
                Debug.LogWarning("[MCP] 写会话文件失败: " + e.Message);
            }
        }

        private static void DeleteSessionFile()
        {
            try
            {
                if (!string.IsNullOrEmpty(_sessionPath) && File.Exists(_sessionPath)) File.Delete(_sessionPath);
            }
            catch (Exception)
            {
                // 退出阶段忽略
            }
        }

        // ---------- 网络 ----------

        private static void AcceptLoop()
        {
            while (_running)
            {
                TcpClient client = null;
                try
                {
                    client = _listener.AcceptTcpClient();
                    var thread = new Thread(() => HandleClient(client)) { IsBackground = true, Name = "UnityMcpConn" };
                    thread.Start();
                }
                catch (SocketException)
                {
                    if (!_running) break;
                }
                catch (ObjectDisposedException)
                {
                    break;
                }
                catch (Exception e)
                {
                    if (_running) Debug.LogWarning("[MCP] 接受连接异常: " + e.Message);
                }
            }
        }

        private static void HandleClient(TcpClient client)
        {
            try
            {
                client.ReceiveTimeout = 5000;
                client.SendTimeout = 10000;
                using (NetworkStream stream = client.GetStream())
                {
                    HttpRequest request = HttpRequest.Read(stream);
                    if (request == null) return;
                    HttpResponse response = DispatchOnMainThread(request);
                    response.Write(stream);
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning("[MCP] 处理连接异常: " + e.Message);
            }
            finally
            {
                try { client.Close(); } catch (Exception) { }
            }
        }

        /// <summary>
        /// 请求派发必须落在主线程：EditorPrefs / EditorApplication / AssetDatabase 等 Unity API
        /// 都不能在桥接的网络线程里调用，否则会抛 "… can only be called from the main thread"。
        /// </summary>
        private static HttpResponse DispatchOnMainThread(HttpRequest request)
        {
            if (MainThreadDispatcher.IsMainThread) return Dispatch(request);

            HttpResponse response = null;
            var done = new ManualResetEventSlim(false);
            MainThreadDispatcher.Post(() =>
            {
                try
                {
                    response = Dispatch(request);
                }
                catch (Exception e)
                {
                    response = HttpResponse.Json(500, McpJson.Write(new Dictionary<string, object>
                    {
                        { "error", "派发异常: " + e.Message },
                    }));
                }
                finally
                {
                    done.Set();
                }
            });

            if (!done.Wait(McpSettings.RequestTimeoutMs + 2000))
            {
                return HttpResponse.Json(504, McpJson.Write(new Dictionary<string, object>
                {
                    { "error", "派发超时，编辑器可能正忙或在编译" },
                }));
            }

            return response ?? HttpResponse.Json(500, McpJson.Write(new Dictionary<string, object> { { "error", "无响应" } }));
        }

        private static HttpResponse Dispatch(HttpRequest request)
        {
            if (request.Path == HealthPath)
            {
                var health = new Dictionary<string, object>
                {
                    { "ok", _running },
                    { "port", _port },
                    { "tools", Registry.Tools.Count },
                    { "pending", MainThreadDispatcher.PendingCount },
                    { "allowDangerousTools", McpSettings.AllowDangerousTools },
                    { "compiling", EditorApplication.isCompiling },
                    { "playing", EditorApplication.isPlaying },
                };
                return HttpResponse.Json(200, McpJson.Write(health));
            }

            if (request.Path != RpcPath)
                return HttpResponse.Json(404, McpJson.Write(new Dictionary<string, object> { { "error", "not found" } }));

            // 鉴权：本地令牌，防止同机其他进程随意驱动编辑器
            string token = request.GetHeader(TokenHeader);
            if (string.IsNullOrEmpty(token))
            {
                var bodyProbe = McpJson.AsObject(McpJson.Parse(request.Body));
                var probeParams = McpJson.AsObject(bodyProbe != null && bodyProbe.ContainsKey("params") ? bodyProbe["params"] : null);
                token = McpJson.GetString(probeParams, "token");
            }
            if (!string.Equals(token, McpSettings.Token, StringComparison.Ordinal))
                return HttpResponse.Json(401, McpJson.Write(new Dictionary<string, object> { { "error", "invalid token" } }));

            object requestJson;
            try
            {
                requestJson = McpJson.Parse(request.Body);
            }
            catch (Exception e)
            {
                return HttpResponse.Json(400, McpJson.Write(new Dictionary<string, object> { { "error", "JSON 解析失败: " + e.Message } }));
            }

            var req = McpJson.AsObject(requestJson);
            if (req == null)
                return HttpResponse.Json(400, McpJson.Write(new Dictionary<string, object> { { "error", "请求体必须是 JSON 对象" } }));

            object id = req.ContainsKey("id") ? req["id"] : null;
            string method = McpJson.GetString(req, "method");
            var parameters = McpJson.AsObject(req.ContainsKey("params") ? req["params"] : null) ?? new Dictionary<string, object>();

            object result;
            string error;
            bool ok = Execute(method, parameters, out result, out error);

            var response = new Dictionary<string, object> { { "jsonrpc", "2.0" } };
            if (id != null) response["id"] = id;
            if (ok) response["result"] = result;
            else response["error"] = new Dictionary<string, object> { { "code", -32000 }, { "message", error ?? "未知错误" } };

            return HttpResponse.Json(200, McpJson.Write(response));
        }

        // ---------- RPC 方法 ----------

        private static bool Execute(string method, Dictionary<string, object> parameters, out object result, out string error)
        {
            result = null;
            error = null;

            switch (method)
            {
                case "ping":
                    result = new Dictionary<string, object> { { "ok", true }, { "tools", Registry.Tools.Count } };
                    return true;

                case "tools/list":
                    result = BuildToolList();
                    return true;

                case "resources/list":
                    result = BuildResourceList();
                    return true;

                case "resources/read":
                    return ReadResource(McpJson.GetString(parameters, "uri"), out result, out error);

                case "tools/call":
                    return CallTool(parameters, out result, out error);

                default:
                    error = "不支持的方法: " + method;
                    return false;
            }
        }

        private static Dictionary<string, object> BuildToolList()
        {
            var tools = new List<object>();
            foreach (McpToolDefinition def in Registry.Tools)
            {
                if (def.Dangerous && !McpSettings.AllowDangerousTools) continue;
                tools.Add(def.ToJson());
            }
            return new Dictionary<string, object> { { "tools", tools } };
        }

        private static Dictionary<string, object> BuildResourceList()
        {
            var resources = new List<object>
            {
                new Dictionary<string, object>
                {
                    { "uri", ResSession },
                    { "name", "会话信息" },
                    { "description", "当前桥接会话：端口、Unity 版本、工程路径、工具数。" },
                    { "mimeType", "application/json" },
                },
                new Dictionary<string, object>
                {
                    { "uri", ResConsole },
                    { "name", "控制台日志" },
                    { "description", "最近 500 条 Unity 控制台日志（环形缓冲）。" },
                    { "mimeType", "application/json" },
                },
                new Dictionary<string, object>
                {
                    { "uri", ResEditor },
                    { "name", "编辑器状态" },
                    { "description", "编辑器自检快照：桥接状态、编译/播放状态、构建目标、宏。" },
                    { "mimeType", "application/json" },
                },
            };
            return new Dictionary<string, object> { { "resources", resources } };
        }

        private static bool ReadResource(string uri, out object result, out string error)
        {
            result = null;
            error = null;

            // 注意：URI 前缀是运行时可配的，不能用 switch 常量分支
            if (uri == ResSession)
            {
                result = new Dictionary<string, object>
                {
                    { "port", _port },
                    { "serverName", McpSettings.ServerName },
                    { "projectName", Application.productName },
                    { "projectPath", Path.GetDirectoryName(Application.dataPath) ?? string.Empty },
                    { "unityVersion", Application.unityVersion },
                    { "tools", Registry.Tools.Count },
                    { "allowDangerousTools", McpSettings.AllowDangerousTools },
                    { "sessionFile", _sessionPath ?? string.Empty },
                };
                return true;
            }
            if (uri == ResConsole)
            {
                result = new Dictionary<string, object> { { "entries", McpLogBuffer.Snapshot(200, null, null) } };
                return true;
            }
            if (uri == ResEditor)
            {
                result = McpEditorTools.GetEditorState();
                return true;
            }

            error = "未知资源: " + uri;
            return false;
        }

        /// <summary>
        /// 工具调用：后台线程收到后投递到主线程执行，带超时兜底，避免编辑器卡顿导致网关死等。
        /// </summary>
        private static bool CallTool(Dictionary<string, object> parameters, out object result, out string error)
        {
            result = null;
            error = null;

            string name = McpJson.GetString(parameters, "name");
            if (string.IsNullOrEmpty(name))
            {
                error = "缺少参数: name";
                return false;
            }
            var args = McpJson.AsObject(parameters.ContainsKey("arguments") ? parameters["arguments"] : null)
                       ?? new Dictionary<string, object>(StringComparer.Ordinal);

            object callResult = null;
            string callError = null;
            var done = new ManualResetEventSlim(false);

            Action invoke = () =>
            {
                bool ok = Registry.TryInvoke(name, args, out callResult, out callError);
                if (!ok) callResult = null;
                done.Set();
            };

            // 已经在主线程（例如派发已投递过一次）时不能再 Post + Wait，否则队列没人泵，必然超时
            if (MainThreadDispatcher.IsMainThread)
            {
                invoke();
            }
            else
            {
                MainThreadDispatcher.Post(invoke);
                if (!done.Wait(McpSettings.RequestTimeoutMs))
                {
                    error = "工具 " + name + " 执行超时（" + McpSettings.RequestTimeoutMs + "ms），可能编辑器正忙或在编译。";
                    return false;
                }
            }

            if (callError != null)
            {
                result = new Dictionary<string, object>
                {
                    { "content", new List<object> { new Dictionary<string, object> { { "type", "text" }, { "text", callError } } } },
                    { "isError", true },
                };
                return true;
            }

            string text = callResult as string;
            if (text == null)
                text = McpJson.WritePretty(callResult ?? new Dictionary<string, object> { { "ok", true } });

            result = new Dictionary<string, object>
            {
                { "content", new List<object> { new Dictionary<string, object> { { "type", "text" }, { "text", text } } } },
                { "isError", false },
            };
            return true;
        }

        // ---------- 极简 HTTP ----------

        private sealed class HttpRequest
        {
            public string Method;
            public string Path;
            public Dictionary<string, string> Headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            public string Body = string.Empty;

            public string GetHeader(string key)
            {
                string value;
                return Headers.TryGetValue(key, out value) ? value : null;
            }

            public static HttpRequest Read(Stream stream)
            {
                var request = new HttpRequest();
                string line = ReadLine(stream);
                if (string.IsNullOrEmpty(line)) return null;

                string[] parts = line.Split(' ');
                if (parts.Length < 2) return null;
                request.Method = parts[0];
                request.Path = parts[1].Split('?')[0];

                while (true)
                {
                    line = ReadLine(stream);
                    if (line == null) return null;
                    if (line.Length == 0) break;
                    int idx = line.IndexOf(':');
                    if (idx <= 0) continue;
                    request.Headers[line.Substring(0, idx).Trim()] = line.Substring(idx + 1).Trim();
                }

                string lengthRaw;
                if (request.Headers.TryGetValue("Content-Length", out lengthRaw))
                {
                    int length;
                    if (int.TryParse(lengthRaw, out length) && length > 0 && length < 8 * 1024 * 1024)
                        request.Body = ReadBody(stream, length);
                }
                return request;
            }

            private static string ReadLine(Stream stream)
            {
                var buffer = new List<byte>();
                int b;
                while ((b = stream.ReadByte()) != -1)
                {
                    if (b == '\n')
                    {
                        if (buffer.Count > 0 && buffer[buffer.Count - 1] == '\r') buffer.RemoveAt(buffer.Count - 1);
                        return Encoding.UTF8.GetString(buffer.ToArray());
                    }
                    buffer.Add((byte)b);
                }
                return buffer.Count > 0 ? Encoding.UTF8.GetString(buffer.ToArray()) : null;
            }

            private static string ReadBody(Stream stream, int length)
            {
                var data = new byte[length];
                int offset = 0;
                while (offset < length)
                {
                    int read = stream.Read(data, offset, length - offset);
                    if (read <= 0) break;
                    offset += read;
                }
                return Encoding.UTF8.GetString(data, 0, offset);
            }
        }

        private sealed class HttpResponse
        {
            public int Status = 200;
            public string Body = string.Empty;

            public static HttpResponse Json(int status, string body)
            {
                return new HttpResponse { Status = status, Body = body };
            }

            public void Write(Stream stream)
            {
                byte[] payload = Encoding.UTF8.GetBytes(Body);
                var sb = new StringBuilder();
                sb.Append("HTTP/1.1 ").Append(Status).Append(" ").Append(Reason(Status)).Append("\r\n");
                sb.Append("Content-Type: application/json; charset=utf-8\r\n");
                sb.Append("Content-Length: ").Append(payload.Length.ToString(CultureInfo.InvariantCulture)).Append("\r\n");
                sb.Append("Cache-Control: no-store\r\n");
                sb.Append("Access-Control-Allow-Origin: *\r\n");
                sb.Append("Access-Control-Allow-Headers: Content-Type, ").Append(TokenHeader).Append("\r\n");
                sb.Append("Access-Control-Allow-Methods: POST, GET, OPTIONS\r\n");
                sb.Append("Connection: close\r\n\r\n");

                byte[] head = Encoding.UTF8.GetBytes(sb.ToString());
                stream.Write(head, 0, head.Length);
                stream.Write(payload, 0, payload.Length);
                stream.Flush();
            }

            private static string Reason(int status)
            {
                switch (status)
                {
                    case 200: return "OK";
                    case 400: return "Bad Request";
                    case 401: return "Unauthorized";
                    case 404: return "Not Found";
                    case 500: return "Internal Server Error";
                    case 504: return "Gateway Timeout";
                    default: return "OK";
                }
            }
        }
    }
}
