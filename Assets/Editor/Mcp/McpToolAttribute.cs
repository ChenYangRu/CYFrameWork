using System;

namespace GameFramework.EditorTools.Mcp
{
    /// <summary>
    /// 把一个 public static 方法暴露成 MCP 工具。
    /// 方法签名支持两种：具名参数（string/int/float/bool），或单个 Dictionary&lt;string,object&gt; 承接原始入参。
    /// </summary>
    [AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = false)]
    public sealed class McpToolAttribute : Attribute
    {
        public McpToolAttribute(string name, string description)
        {
            Name = name;
            Description = description;
        }

        /// <summary>工具名，形如 unity.get_hierarchy。</summary>
        public string Name { get; }

        /// <summary>给 LLM 看的说明，写清楚副作用与返回值含义。</summary>
        public string Description { get; }

        /// <summary>true 表示会改工程或触发构建，默认禁用，需在 McpSettings.AllowDangerousTools 打开。</summary>
        public bool Dangerous { get; set; }
    }
}
