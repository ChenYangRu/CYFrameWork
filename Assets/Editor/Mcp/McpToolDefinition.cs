using System;
using System.Collections.Generic;

namespace GameFramework.EditorTools.Mcp
{
    /// <summary>
    /// 一个 MCP 工具的元信息，由 McpToolRegistry 反射生成，用于响应 tools/list。
    /// </summary>
    public sealed class McpToolDefinition
    {
        public McpToolDefinition(string name, string description, bool dangerous, Dictionary<string, object> inputSchema)
        {
            Name = name;
            Description = description;
            Dangerous = dangerous;
            InputSchema = inputSchema ?? new Dictionary<string, object>();
        }

        public string Name { get; }

        public string Description { get; }

        public bool Dangerous { get; }

        /// <summary>JSON Schema 对象（已是可直接序列化的结构）。</summary>
        public Dictionary<string, object> InputSchema { get; }

        public Dictionary<string, object> ToJson()
        {
            var json = new Dictionary<string, object>
            {
                { "name", Name },
                { "description", Dangerous ? "[危险] " + Description : Description },
                { "inputSchema", InputSchema },
            };
            return json;
        }
    }
}
