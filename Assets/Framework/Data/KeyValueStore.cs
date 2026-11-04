using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace GameFramework.Data
{
    /// <summary>
    /// 极简键值存储（音量、画质、语言、新手引导是否弹过…），落一个 txt。
    ///
    /// 为什么不直接用 PlayerPrefs：
    ///   · PlayerPrefs 在 WebGL/小游戏上是 IndexedDB，容量与同步时机都不可控
    ///   · 想跟存档一起备份/清档，就必须和存档放一起
    ///
    /// 文件格式（一行一条，值里的换行/制表符会被转义）：
    /// <code>
    /// #kv v1
    /// volume_master=0.8
    /// language=zh_CN
    /// guide_done=1
    /// </code>
    /// </summary>
    public sealed class KeyValueStore
    {
        private const char Separator = '=';
        private readonly string _filePath;
        private readonly Dictionary<string, string> _map = new Dictionary<string, string>(32);
        private bool _dirty;

        public KeyValueStore(string filePath)
        {
            _filePath = filePath;
            Load();
        }

        public string FilePath { get { return _filePath; } }
        public bool Dirty { get { return _dirty; } }
        public int Count { get { return _map.Count; } }

        public IEnumerable<KeyValuePair<string, string>> All { get { return _map; } }

        // ---------------- 读 ----------------

        public bool Has(string key)
        {
            return !string.IsNullOrEmpty(key) && _map.ContainsKey(key);
        }

        public string Get(string key, string defaultValue = "")
        {
            if (string.IsNullOrEmpty(key)) return defaultValue;
            string v;
            return _map.TryGetValue(key, out v) ? v : defaultValue;
        }

        public int GetInt(string key, int defaultValue = 0)
        {
            int v;
            return int.TryParse(Get(key), NumberStyles.Integer, CultureInfo.InvariantCulture, out v) ? v : defaultValue;
        }

        public float GetFloat(string key, float defaultValue = 0f)
        {
            float v;
            return float.TryParse(Get(key), NumberStyles.Float, CultureInfo.InvariantCulture, out v) ? v : defaultValue;
        }

        public bool GetBool(string key, bool defaultValue = false)
        {
            string v = Get(key);
            if (string.IsNullOrEmpty(v)) return defaultValue;
            return v == "1" || string.Equals(v, "true", StringComparison.OrdinalIgnoreCase);
        }

        // ---------------- 写 ----------------

        public void Set(string key, string value)
        {
            if (string.IsNullOrEmpty(key)) return;
            string old;
            if (_map.TryGetValue(key, out old) && old == value) return;
            _map[key] = value ?? string.Empty;
            _dirty = true;
        }

        public void SetInt(string key, int value) { Set(key, value.ToString(CultureInfo.InvariantCulture)); }

        public void SetFloat(string key, float value)
        {
            Set(key, value.ToString("0.####", CultureInfo.InvariantCulture));
        }

        public void SetBool(string key, bool value) { Set(key, value ? "1" : "0"); }

        public bool Remove(string key)
        {
            if (string.IsNullOrEmpty(key)) return false;
            if (!_map.Remove(key)) return false;
            _dirty = true;
            return true;
        }

        public void Clear()
        {
            if (_map.Count == 0) return;
            _map.Clear();
            _dirty = true;
        }

        // ---------------- 落盘 ----------------

        public int Load()
        {
            _map.Clear();
            _dirty = false;

            string text = DataFile.ReadAllText(_filePath);
            if (text == null) return -1;

            int loaded = 0;
            string[] lines = text.Split('\n');
            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i];
                if (line.Length > 0 && line[line.Length - 1] == '\r') line = line.Substring(0, line.Length - 1);
                if (line.Length == 0 || line[0] == '#') continue;

                int idx = line.IndexOf(Separator);
                if (idx <= 0) continue;

                string key = Unescape(line.Substring(0, idx));
                string value = Unescape(line.Substring(idx + 1));
                _map[key] = value;
                loaded++;
            }
            return loaded;
        }

        public bool Save()
        {
            var sb = new StringBuilder(64 + _map.Count * 24);
            sb.Append("#kv v1\n");
            sb.Append("#saved=").Append(DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ")).Append('\n');
            foreach (var kv in _map)
            {
                sb.Append(Escape(kv.Key)).Append(Separator).Append(Escape(kv.Value)).Append('\n');
            }

            if (!DataFile.WriteAllTextAtomic(_filePath, sb.ToString())) return false;
            _dirty = false;
            return true;
        }

        private static string Escape(string s)
        {
            if (string.IsNullOrEmpty(s)) return string.Empty;
            return s.Replace("\\", "\\\\").Replace("\n", "\\n").Replace("\r", string.Empty).Replace("\t", "\\t");
        }

        private static string Unescape(string s)
        {
            if (string.IsNullOrEmpty(s)) return string.Empty;
            var sb = new StringBuilder(s.Length);
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                if (c == '\\' && i + 1 < s.Length)
                {
                    char n = s[++i];
                    if (n == 'n') sb.Append('\n');
                    else if (n == 't') sb.Append('\t');
                    else if (n == '\\') sb.Append('\\');
                    else sb.Append(n);
                }
                else sb.Append(c);
            }
            return sb.ToString();
        }
    }
}
