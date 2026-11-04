using System;

namespace GameFramework.EditorTools.Mcp
{
    /// <summary>
    /// 描述 MCP 工具单个参数的含义，用于生成 JSON Schema。
    /// 不标注时按参数名生成必填项。
    /// </summary>
    [AttributeUsage(AttributeTargets.Parameter, AllowMultiple = false, Inherited = false)]
    public sealed class McpParamAttribute : Attribute
    {
        public McpParamAttribute(string description)
        {
            Description = description;
            Required = true;
        }

        public string Description { get; }

        public bool Required { get; set; }
    }
}
