using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace GameFramework.EditorTools.Mcp
{
    /// <summary>
    /// 极简 JSON 解析与序列化。
    /// 只依赖 BCL，避免引入第三方库；承载类型为 object：
    /// Dictionary&lt;string,object&gt; / List&lt;object&gt; / string / double / bool / null。
    /// </summary>
    public static class McpJson
    {
        public static object Parse(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return null;
            int i = 0;
            SkipWs(text, ref i);
            object value = ParseValue(text, ref i);
            return value;
        }

        public static string Write(object value)
        {
            var sb = new StringBuilder(256);
            WriteValue(sb, value);
            return sb.ToString();
        }

        public static string WritePretty(object value)
        {
            var sb = new StringBuilder(256);
            WriteValue(sb, value, 0);
            return sb.ToString();
        }

        // ---------- 取值辅助 ----------

        public static Dictionary<string, object> AsObject(object value)
        {
            return value as Dictionary<string, object>;
        }

        public static List<object> AsArray(object value)
        {
            return value as List<object>;
        }

        public static string GetString(Dictionary<string, object> obj, string key, string fallback = null)
        {
            if (obj == null || !obj.TryGetValue(key, out var v)) return fallback;
            if (v == null) return fallback;
            if (v is string s) return s;
            return Convert.ToString(v, CultureInfo.InvariantCulture);
        }

        public static int GetInt(Dictionary<string, object> obj, string key, int fallback = 0)
        {
            if (obj == null || !obj.TryGetValue(key, out var v) || v == null) return fallback;
            if (v is double d) return (int)d;
            if (v is long l) return (int)l;
            if (v is bool b) return b ? 1 : 0;
            int.TryParse(Convert.ToString(v, CultureInfo.InvariantCulture), out var r);
            return r;
        }

        public static bool GetBool(Dictionary<string, object> obj, string key, bool fallback = false)
        {
            if (obj == null || !obj.TryGetValue(key, out var v) || v == null) return fallback;
            if (v is bool b) return b;
            if (v is double d) return d != 0;
            bool.TryParse(Convert.ToString(v, CultureInfo.InvariantCulture), out var r);
            return r;
        }

        public static float GetFloat(Dictionary<string, object> obj, string key, float fallback = 0f)
        {
            if (obj == null || !obj.TryGetValue(key, out var v) || v == null) return fallback;
            if (v is double d) return (float)d;
            float.TryParse(Convert.ToString(v, CultureInfo.InvariantCulture), NumberStyles.Float, CultureInfo.InvariantCulture, out var r);
            return r;
        }

        // ---------- 解析 ----------

        private static void SkipWs(string s, ref int i)
        {
            while (i < s.Length && (s[i] == ' ' || s[i] == '\t' || s[i] == '\r' || s[i] == '\n')) i++;
        }

        private static object ParseValue(string s, ref int i)
        {
            SkipWs(s, ref i);
            if (i >= s.Length) throw new FormatException("JSON 意外结束");

            char c = s[i];
            switch (c)
            {
                case '{': return ParseObject(s, ref i);
                case '[': return ParseArray(s, ref i);
                case '"': return ParseString(s, ref i);
                case 't': Expect(s, ref i, "true"); return true;
                case 'f': Expect(s, ref i, "false"); return false;
                case 'n': Expect(s, ref i, "null"); return null;
                default: return ParseNumber(s, ref i);
            }
        }

        private static Dictionary<string, object> ParseObject(string s, ref int i)
        {
            var obj = new Dictionary<string, object>(StringComparer.Ordinal);
            i++; // {
            SkipWs(s, ref i);
            if (i < s.Length && s[i] == '}') { i++; return obj; }

            while (i < s.Length)
            {
                SkipWs(s, ref i);
                if (s[i] != '"') throw new FormatException("JSON 对象键必须是字符串，位置 " + i);
                string key = ParseString(s, ref i);
                SkipWs(s, ref i);
                if (i >= s.Length || s[i] != ':') throw new FormatException("JSON 缺少冒号，位置 " + i);
                i++;
                obj[key] = ParseValue(s, ref i);
                SkipWs(s, ref i);
                if (i < s.Length && s[i] == ',') { i++; continue; }
                if (i < s.Length && s[i] == '}') { i++; return obj; }
                throw new FormatException("JSON 对象未闭合，位置 " + i);
            }
            throw new FormatException("JSON 对象未闭合");
        }

        private static List<object> ParseArray(string s, ref int i)
        {
            var list = new List<object>();
            i++; // [
            SkipWs(s, ref i);
            if (i < s.Length && s[i] == ']') { i++; return list; }

            while (i < s.Length)
            {
                list.Add(ParseValue(s, ref i));
                SkipWs(s, ref i);
                if (i < s.Length && s[i] == ',') { i++; continue; }
                if (i < s.Length && s[i] == ']') { i++; return list; }
                throw new FormatException("JSON 数组未闭合，位置 " + i);
            }
            throw new FormatException("JSON 数组未闭合");
        }

        private static string ParseString(string s, ref int i)
        {
            i++; // 起始引号
            var sb = new StringBuilder();
            while (i < s.Length)
            {
                char c = s[i];
                if (c == '"') { i++; return sb.ToString(); }
                if (c != '\\') { sb.Append(c); i++; continue; }

                i++;
                if (i >= s.Length) break;
                char e = s[i++];
                switch (e)
                {
                    case '"': sb.Append('"'); break;
                    case '\\': sb.Append('\\'); break;
                    case '/': sb.Append('/'); break;
                    case 'b': sb.Append('\b'); break;
                    case 'f': sb.Append('\f'); break;
                    case 'n': sb.Append('\n'); break;
                    case 'r': sb.Append('\r'); break;
                    case 't': sb.Append('\t'); break;
                    case 'u':
                        if (i + 4 <= s.Length)
                        {
                            int code;
                            if (int.TryParse(s.Substring(i, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out code))
                                sb.Append((char)code);
                            i += 4;
                        }
                        break;
                    default: sb.Append(e); break;
                }
            }
            throw new FormatException("JSON 字符串未闭合");
        }

        private static object ParseNumber(string s, ref int i)
        {
            int start = i;
            while (i < s.Length && (char.IsDigit(s[i]) || s[i] == '-' || s[i] == '+' || s[i] == '.' || s[i] == 'e' || s[i] == 'E')) i++;
            string raw = s.Substring(start, i - start);
            double d;
            if (!double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out d))
                throw new FormatException("JSON 数字解析失败: " + raw);
            return d;
        }

        private static void Expect(string s, ref int i, string word)
        {
            if (i + word.Length > s.Length || string.CompareOrdinal(s, i, word, 0, word.Length) != 0)
                throw new FormatException("JSON 字面量解析失败，位置 " + i);
            i += word.Length;
        }

        // ---------- 序列化 ----------

        private static void WriteValue(StringBuilder sb, object value, int depth = -1)
        {
            if (value == null) { sb.Append("null"); return; }

            if (value is string str) { WriteString(sb, str); return; }
            if (value is bool b) { sb.Append(b ? "true" : "false"); return; }
            if (value is double dbl)
            {
                if (double.IsNaN(dbl) || double.IsInfinity(dbl)) sb.Append("null");
                else if (dbl == Math.Floor(dbl) && Math.Abs(dbl) < 9e15) sb.Append(((long)dbl).ToString(CultureInfo.InvariantCulture));
                else sb.Append(dbl.ToString("R", CultureInfo.InvariantCulture));
                return;
            }
            if (value is float f) { WriteValue(sb, (double)f, depth); return; }
            if (value is int || value is long || value is short || value is byte || value is uint || value is ulong || value is ushort)
            { sb.Append(Convert.ToInt64(value, CultureInfo.InvariantCulture).ToString(CultureInfo.InvariantCulture)); return; }

            var dict = value as IDictionary;
            if (dict != null)
            {
                sb.Append('{');
                bool first = true;
                foreach (DictionaryEntry e in dict)
                {
                    if (!first) sb.Append(',');
                    first = false;
                    WriteString(sb, Convert.ToString(e.Key, CultureInfo.InvariantCulture));
                    sb.Append(':');
                    WriteValue(sb, e.Value, depth);
                }
                sb.Append('}');
                return;
            }

            var enumerable = value as IEnumerable;
            if (enumerable != null)
            {
                sb.Append('[');
                bool first = true;
                foreach (var item in enumerable)
                {
                    if (!first) sb.Append(',');
                    first = false;
                    WriteValue(sb, item, depth);
                }
                sb.Append(']');
                return;
            }

            WriteString(sb, Convert.ToString(value, CultureInfo.InvariantCulture));
        }

        private static void WriteString(StringBuilder sb, string s)
        {
            sb.Append('"');
            foreach (char c in s)
            {
                switch (c)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\b': sb.Append("\\b"); break;
                    case '\f': sb.Append("\\f"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default:
                        if (c < 0x20) sb.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                        else sb.Append(c);
                        break;
                }
            }
            sb.Append('"');
        }
    }
}
