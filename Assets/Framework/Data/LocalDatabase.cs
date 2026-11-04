using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;

namespace GameFramework.Data
{
    /// <summary>
    /// 一个存档槽位 = 一个目录，目录里一张表一个 txt。
    ///
    /// <code>
    /// &lt;persistentDataPath&gt;/save/&lt;slot&gt;/
    ///   player.txt     # 单条记录（主角）
    ///   items.txt      # 多条记录（背包）
    ///   settings.txt   # 键值对（音量/画质等）
    ///   slot.txt       # 槽位元信息（创建时间/最后存档/游玩次数）
    /// </code>
    ///
    /// 表是**懒加载**的：第一次 <see cref="Table{T}"/> 时才读盘，
    /// 没打开的表不会占启动时间（存档多了也不拖慢启动）。
    /// </summary>
    public sealed class LocalDatabase
    {
        private readonly string _rootDir;
        private readonly Dictionary<Type, DataTableBase> _tables = new Dictionary<Type, DataTableBase>(8);

        public LocalDatabase(string rootDir)
        {
            _rootDir = rootDir;
            DataFile.EnsureDirectory(_rootDir);
        }

        public string RootDir { get { return _rootDir; } }

        /// <summary>已加载的表数量</summary>
        public int LoadedTableCount { get { return _tables.Count; } }

        /// <summary>有未落盘改动的表数量</summary>
        public int DirtyCount
        {
            get
            {
                int n = 0;
                foreach (var t in _tables.Values) if (t.Dirty) n++;
                return n;
            }
        }

        public IEnumerable<DataTableBase> Tables { get { return _tables.Values; } }

        /// <summary>
        /// 取一张表（懒加载：首次调用会读盘并把历史数据装进内存）。
        /// 表名优先用 <see cref="DataTableAttribute"/>，否则用类名小写。
        /// </summary>
        public DataTable<T> Table<T>() where T : class, IDataRecord, new()
        {
            DataTableBase existed;
            if (_tables.TryGetValue(typeof(T), out existed)) return (DataTable<T>)existed;

            string name = ResolveTableName(typeof(T));
            string path = Path.Combine(_rootDir, name + ".txt");

            var table = new DataTable<T>(name, path);
            int loaded = table.Load();
            if (loaded < 0)
                GameLog.Info("[Data] 新表（磁盘上没有，首次保存时创建）: " + name);
            else
                GameLog.Info("[Data] 已加载表 " + name + "：" + loaded + " 条");

            _tables[typeof(T)] = table;
            return table;
        }

        /// <summary>把所有已加载的表写回磁盘（只写脏表）</summary>
        public bool SaveDirty()
        {
            bool ok = true;
            foreach (var t in _tables.Values)
            {
                if (!t.Dirty) continue;
                if (!t.Save()) ok = false;
            }
            return ok;
        }

        /// <summary>强制全部写回（切场景/退出/手动存档时用）</summary>
        public bool SaveAll()
        {
            bool ok = true;
            foreach (var t in _tables.Values)
            {
                if (!t.Save()) ok = false;
            }
            return ok;
        }

        /// <summary>重新读盘（覆盖内存里的改动，慎用）</summary>
        public int LoadAll()
        {
            int total = 0;
            foreach (var t in _tables.Values)
            {
                int n = t.Load();
                if (n > 0) total += n;
            }
            return total;
        }

        /// <summary>清空内存数据（不删文件）</summary>
        public void ClearMemory()
        {
            foreach (var t in _tables.Values) t.Clear();
        }

        /// <summary>删除本槽位所有数据文件（不可撤销）</summary>
        public bool DeleteAllFiles()
        {
            try
            {
                if (!Directory.Exists(_rootDir)) return true;
                var files = Directory.GetFiles(_rootDir, "*.txt*");
                for (int i = 0; i < files.Length; i++) DataFile.SafeDelete(files[i]);
                foreach (var t in _tables.Values) t.Clear();
                return true;
            }
            catch (Exception e)
            {
                GameLog.Error("[Data] 清空槽位失败: " + _rootDir, e);
                return false;
            }
        }

        // ---------------- 内部 ----------------

        private static string ResolveTableName(Type type)
        {
            var attr = type.GetCustomAttribute<DataTableAttribute>(false);
            string name = attr != null && !string.IsNullOrEmpty(attr.Name) ? attr.Name : type.Name.ToLowerInvariant();
            return SanitizeFileName(name);
        }

        /// <summary>表名会直接变成文件名，必须挡住非法字符与中文（小游戏/安卓沙盒上中文名容易出问题）</summary>
        private static string SanitizeFileName(string name)
        {
            if (string.IsNullOrEmpty(name)) return "table";

            var sb = new System.Text.StringBuilder(name.Length);
            for (int i = 0; i < name.Length; i++)
            {
                char c = name[i];
                if ((c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9') || c == '_' || c == '-')
                    sb.Append(c);
            }
            return sb.Length == 0 ? "table" : sb.ToString().ToLowerInvariant();
        }
    }
}
