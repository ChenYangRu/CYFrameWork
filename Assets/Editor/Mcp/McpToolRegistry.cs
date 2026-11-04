using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;

namespace GameFramework.EditorTools.Mcp
{
    /// <summary>
    /// 工具注册表：扫描所有带 [McpTool] 的静态方法，生成 JSON Schema 并负责调用。
    /// 新增工具只需写一个静态类 + 一个带特性的方法，不用改这里。
    /// </summary>
    public sealed class McpToolRegistry
    {
        private sealed class Binding
        {
            public MethodInfo Method;
            public McpToolDefinition Definition;
            public bool TakesRawArgs;
            public ParameterInfo[] Parameters;
        }

        private readonly Dictionary<string, Binding> _bindings = new Dictionary<string, Binding>(StringComparer.Ordinal);
        private readonly List<McpToolDefinition> _definitions = new List<McpToolDefinition>();
        private readonly object _gate = new object();

        /// <summary>上次扫描过的程序集数，扫不到工具时用来定位问题。</summary>
        public int LastScanAssemblyCount { get; private set; }

        /// <summary>上次扫描命中的 [McpTool] 方法数（含因参数不支持被丢弃的）。</summary>
        public int LastScanCandidateCount { get; private set; }

        /// <summary>上次扫描时反射取类型失败的程序集数。</summary>
        public int LastScanFailedAssemblyCount { get; private set; }

        /// <summary>扫描已加载程序集。重复调用会重建表。</summary>
        public void Scan()
        {
            var fresh = new Dictionary<string, Binding>(StringComparer.Ordinal);
            Assembly[] assemblies = AppDomain.CurrentDomain.GetAssemblies();

            int asmCount = 0;
            int candidates = 0;
            int failed = 0;

            foreach (Assembly asm in assemblies)
            {
                asmCount++;
                Type[] types;
                try
                {
                    types = asm.GetTypes();
                }
                catch (ReflectionTypeLoadException e)
                {
                    types = e.Types ?? new Type[0];
                    failed++;
                }
                catch (Exception)
                {
                    failed++;
                    continue;
                }

                foreach (Type type in types)
                {
                    if (type == null || !type.IsClass) continue;

                    // static class 编译后是 abstract sealed，不能按 IsAbstract 一律排除，
                    // 否则所有工具类（都是 static class）会被整体跳过，扫出 0 个工具。
                    bool isStaticClass = type.IsAbstract && type.IsSealed;
                    if (type.IsAbstract && !isStaticClass) continue;

                    MethodInfo[] methods;
                    try
                    {
                        methods = type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.DeclaredOnly);
                    }
                    catch (Exception)
                    {
                        continue;
                    }

                    foreach (MethodInfo method in methods)
                    {
                        string toolName, description;
                        bool dangerous;
                        if (!TryReadToolAttribute(method, out toolName, out description, out dangerous)) continue;
                        candidates++;
                        if (!method.IsPublic || !method.IsStatic) continue;

                        Binding binding = CreateBinding(method, ApplyToolPrefix(toolName), description, dangerous);
                        if (binding == null) continue;
                        fresh[binding.Definition.Name] = binding;
                    }
                }
            }

            LastScanAssemblyCount = asmCount;
            LastScanCandidateCount = candidates;
            LastScanFailedAssemblyCount = failed;

            lock (_gate)
            {
                _bindings.Clear();
                _definitions.Clear();
                foreach (KeyValuePair<string, Binding> pair in fresh) _bindings[pair.Key] = pair.Value;
                foreach (Binding b in fresh.Values) _definitions.Add(b.Definition);
                _definitions.Sort((a, b) => string.CompareOrdinal(a.Name, b.Name));
            }
        }

        public IReadOnlyList<McpToolDefinition> Tools
        {
            get { return _definitions; }
        }

        /// <summary>
        /// 按工程配置给工具名加前缀（配置在 McpProjectConfig.toolPrefix）。
        /// 规则：工具代码里只写短名（如 get_hierarchy），这里统一变成 mygame.get_hierarchy；
        ///       若短名里已经带 '.'，视为调用方自己管前缀，不重复添加。
        /// 这样"换工程只换 asset"，工具代码零改动。
        /// </summary>
        private static string ApplyToolPrefix(string name)
        {
            string prefix = McpSettings.ToolPrefix;   // 已含末尾点，可能为空串
            if (string.IsNullOrEmpty(prefix)) return name;
            if (name.IndexOf('.') >= 0) return name;
            return prefix + name;
        }

        /// <summary>执行工具。result 为可序列化的 object；error 非 null 表示失败。</summary>
        public bool TryInvoke(string name, Dictionary<string, object> args, out object result, out string error)
        {
            result = null;
            error = null;

            Binding binding;
            lock (_gate)
            {
                if (!_bindings.TryGetValue(name, out binding))
                {
                    error = "未知工具: " + name;
                    return false;
                }
            }

            if (binding.Definition.Dangerous && !McpSettings.AllowDangerousTools)
            {
                error = "工具 " + name + " 被标记为危险操作，已拒绝。请在 Tools/MCP 桥接/允许危险工具 中开启。";
                return false;
            }

            object[] callArgs;
            if (!TryBuildArgs(binding, args, out callArgs, out error)) return false;

            try
            {
                result = binding.Method.Invoke(null, callArgs);
                return true;
            }
            catch (TargetInvocationException e)
            {
                Exception inner = e.InnerException ?? e;
                error = inner.GetType().Name + ": " + inner.Message;
                return false;
            }
            catch (Exception e)
            {
                error = e.GetType().Name + ": " + e.Message;
                return false;
            }
        }

        /// <summary>
        /// 读取 [McpTool] 特性。按类型 FullName 匹配而不是 typeof 比较：
        /// Unity 域重载后 AppDomain 里可能同时存在新旧两份 Editor 程序集，
        /// typeof(McpToolAttribute) 与工具方法上的特性类型不是同一个 Type 实例，强转会失配。
        /// </summary>
        private static bool TryReadToolAttribute(MethodInfo method, out string name, out string description, out bool dangerous)
        {
            name = null;
            description = null;
            dangerous = false;

            foreach (object attribute in method.GetCustomAttributes(false))
            {
                Type at = attribute.GetType();
                if (at.FullName != typeof(McpToolAttribute).FullName) continue;

                name = ReadStringProperty(at, attribute, "Name");
                description = ReadStringProperty(at, attribute, "Description");

                PropertyInfo prop = at.GetProperty("Dangerous");
                if (prop != null)
                {
                    object value = prop.GetValue(attribute);
                    dangerous = value is bool && (bool)value;
                }
                return !string.IsNullOrEmpty(name);
            }
            return false;
        }

        /// <summary>读取 [McpParam] 特性，同样按 FullName 匹配。</summary>
        private static bool TryReadParamAttribute(ParameterInfo parameter, out string description, out bool required)
        {
            description = null;
            required = false;

            foreach (object attribute in parameter.GetCustomAttributes(false))
            {
                Type at = attribute.GetType();
                if (at.FullName != typeof(McpParamAttribute).FullName) continue;

                description = ReadStringProperty(at, attribute, "Description");
                PropertyInfo prop = at.GetProperty("Required");
                if (prop != null)
                {
                    object value = prop.GetValue(attribute);
                    required = value is bool && (bool)value;
                }
                return true;
            }
            return false;
        }

        private static string ReadStringProperty(Type type, object instance, string propertyName)
        {
            PropertyInfo prop = type.GetProperty(propertyName);
            if (prop == null) return null;
            return prop.GetValue(instance) as string;
        }

        private static Binding CreateBinding(MethodInfo method, string toolName, string description, bool dangerous)
        {
            ParameterInfo[] ps = method.GetParameters();
            bool raw = ps.Length == 1 && ps[0].ParameterType == typeof(Dictionary<string, object>);
            if (!raw && ps.Length > 0)
            {
                foreach (ParameterInfo p in ps)
                {
                    Type t = p.ParameterType;
                    if (t != typeof(string) && t != typeof(int) && t != typeof(float) && t != typeof(bool))
                        return null; // 参数类型不受支持，跳过该工具
                }
            }

            var definition = new McpToolDefinition(toolName, description, dangerous, BuildSchema(ps, raw));
            return new Binding
            {
                Method = method,
                Definition = definition,
                TakesRawArgs = raw,
                Parameters = ps,
            };
        }

        private static Dictionary<string, object> BuildSchema(ParameterInfo[] ps, bool raw)
        {
            var schema = new Dictionary<string, object> { { "type", "object" } };
            if (raw)
            {
                schema["additionalProperties"] = true;
                return schema;
            }
            if (ps.Length == 0) return schema;

            var properties = new Dictionary<string, object>();
            var required = new List<object>();

            foreach (ParameterInfo p in ps)
            {
                var prop = new Dictionary<string, object> { { "type", JsonTypeOf(p.ParameterType) } };
                string paramDescription;
                bool paramRequired;
                if (TryReadParamAttribute(p, out paramDescription, out paramRequired))
                {
                    prop["description"] = paramDescription;
                    if (paramRequired && !p.HasDefaultValue) required.Add(p.Name);
                }
                else
                {
                    prop["description"] = "参数 " + p.Name;
                    if (!p.HasDefaultValue) required.Add(p.Name);
                }
                properties[p.Name] = prop;
            }

            schema["properties"] = properties;
            if (required.Count > 0) schema["required"] = required;
            return schema;
        }

        private static string JsonTypeOf(Type t)
        {
            if (t == typeof(string)) return "string";
            if (t == typeof(bool)) return "boolean";
            if (t == typeof(int)) return "integer";
            if (t == typeof(float)) return "number";
            return "string";
        }

        private static bool TryBuildArgs(Binding binding, Dictionary<string, object> args, out object[] callArgs, out string error)
        {
            error = null;
            callArgs = null;
            if (args == null) args = new Dictionary<string, object>(StringComparer.Ordinal);

            if (binding.TakesRawArgs)
            {
                callArgs = new object[] { args };
                return true;
            }

            ParameterInfo[] ps = binding.Parameters;
            if (ps.Length == 0)
            {
                callArgs = new object[0];
                return true;
            }

            var built = new object[ps.Length];
            for (int i = 0; i < ps.Length; i++)
            {
                ParameterInfo p = ps[i];
                object raw;
                if (!args.TryGetValue(p.Name, out raw) || raw == null)
                {
                    if (p.HasDefaultValue)
                    {
                        built[i] = p.DefaultValue;
                        continue;
                    }
                    error = "缺少必填参数: " + p.Name;
                    return false;
                }

                object converted;
                if (!TryConvert(raw, p.ParameterType, out converted))
                {
                    error = "参数 " + p.Name + " 类型不匹配，期望 " + p.ParameterType.Name;
                    return false;
                }
                built[i] = converted;
            }

            callArgs = built;
            return true;
        }

        private static bool TryConvert(object raw, Type target, out object converted)
        {
            converted = null;
            if (target == typeof(string))
            {
                converted = raw is string s ? s : Convert.ToString(raw, CultureInfo.InvariantCulture);
                return true;
            }
            if (target == typeof(bool))
            {
                if (raw is bool b) { converted = b; return true; }
                if (raw is double d) { converted = d != 0; return true; }
                bool parsed;
                if (bool.TryParse(Convert.ToString(raw, CultureInfo.InvariantCulture), out parsed)) { converted = parsed; return true; }
                return false;
            }
            if (target == typeof(int))
            {
                if (raw is double dd) { converted = (int)dd; return true; }
                int parsed;
                if (int.TryParse(Convert.ToString(raw, CultureInfo.InvariantCulture), NumberStyles.Integer, CultureInfo.InvariantCulture, out parsed)) { converted = parsed; return true; }
                return false;
            }
            if (target == typeof(float))
            {
                if (raw is double dd) { converted = (float)dd; return true; }
                float parsed;
                if (float.TryParse(Convert.ToString(raw, CultureInfo.InvariantCulture), NumberStyles.Float, CultureInfo.InvariantCulture, out parsed)) { converted = parsed; return true; }
                return false;
            }
            return false;
        }
    }
}
