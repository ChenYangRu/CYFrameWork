using System.Collections.Generic;

namespace GameFramework.EditorTools.Mcp
{
    /// <summary>
    /// 控制台日志工具。数据源是 McpLogBuffer 的环形缓冲（最近 500 条），
    /// 不反射 UnityEditor 内部类型，避免版本升级失效。
    /// </summary>
    public static class ConsoleTools
    {
        [McpTool("get_console_logs",
            "读取最近若干条 Unity 控制台日志。level 可填 error/warning/exception/log（留空为全部），"
            + "contains 按消息子串过滤。返回按时间正序，含 stackTrace。")]
        public static object GetConsoleLogs(
            [McpParam("返回条数，最大 500", Required = false)] int count = 50,
            [McpParam("级别过滤：error/warning/exception/log，留空为全部", Required = false)] string level = "",
            [McpParam("消息包含的子串，留空不过滤", Required = false)] string contains = "")
        {
            List<object> entries = McpLogBuffer.Snapshot(count, level, contains);
            return new Dictionary<string, object>
            {
                { "count", entries.Count },
                { "filter", new Dictionary<string, object> { { "level", level ?? string.Empty }, { "contains", contains ?? string.Empty } } },
                { "entries", entries },
            };
        }

        [McpTool("clear_console_logs",
            "清空 MCP 侧维护的日志缓冲（不会清 Unity 控制台本身）。", Dangerous = true)]
        public static object ClearConsoleLogs()
        {
            McpLogBuffer.Clear();
            return new Dictionary<string, object> { { "ok", true } };
        }
    }
}
