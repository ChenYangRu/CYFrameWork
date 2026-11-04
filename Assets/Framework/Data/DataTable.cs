using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace GameFramework.Data
{
    /// <summary>
    /// 数据表的非泛型基类（只为了能让 LocalDatabase 统一遍历/保存，业务不要直接用）。
    /// </summary>
    public abstract class DataTableBase
    {
        public abstract string Name { get; }
        public abstract string FilePath { get; }
        public abstract bool Dirty { get; }
        public abstract int Count { get; }

        /// <summary>把内存写回磁盘；返回是否成功（不脏也算成功）</summary>
        public abstract bool Save();
        /// <summary>从磁盘读回内存；返回读到的条数，-1 表示文件不存在</summary>
        public abstract int Load();
        public abstract void Clear();
        public abstract void MarkDirty();
    }

    /// <summary>
    /// 一张数据表 = 一个 txt 文件 + 一组同类型记录，提供**增删改查**。
    ///
    /// 文件格式（一行一条记录，便于人读与 git diff）：
    /// <code>
    /// #table=items v1
    /// #saved=2026-11-04T14:02:11Z
    /// #count=2
    /// {"id":"item_1","name":"药水","count":3}
    /// {"id":"item_2","name":"钥匙","count":1}
    /// </code>
    ///
    /// 为什么"一行一个 JSON"而不是一个大 JSON 数组：
    ///   · JsonUtility 不支持在根上序列化 List，包一层壳要多一个类型，还容易踩泛型坑
    ///   · 单条记录损坏只丢那一条，不会整表读不出来
    /// </summary>
    public sealed class DataTable<T> : DataTableBase where T : class, IDataRecord, new()
    {
        private readonly string _name;
        private readonly string _filePath;
        private readonly List<T> _records = new List<T>(32);
        private readonly Dictionary<string, T> _index = new Dictionary<string, T>(32);
        private bool _dirty;

        public DataTable(string name, string filePath)
        {
            _name = name;
            _filePath = filePath;
        }

        public override string Name { get { return _name; } }
        public override string FilePath { get { return _filePath; } }
        public override bool Dirty { get { return _dirty; } }
        public override int Count { get { return _records.Count; } }

        /// <summary>只读视图（不要在遍历时增删）</summary>
        public IReadOnlyList<T> Records { get { return _records; } }

        // ---------------- 查 ----------------

        /// <summary>按主键查；找不到返回 null</summary>
        public T Get(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            T v;
            return _index.TryGetValue(id, out v) ? v : null;
        }

        public bool Contains(string id) { return Get(id) != null; }

        /// <summary>条件查询（返回新列表，可安全遍历）</summary>
        public List<T> Find(Predicate<T> match)
        {
            var list = new List<T>();
            if (match == null) return list;
            for (int i = 0; i < _records.Count; i++)
            {
                if (match(_records[i])) list.Add(_records[i]);
            }
            return list;
        }

        /// <summary>条件取第一条；没有返回 null</summary>
        public T FirstOrDefault(Predicate<T> match)
        {
            if (match == null) return null;
            for (int i = 0; i < _records.Count; i++)
            {
                if (match(_records[i])) return _records[i];
            }
            return null;
        }

        // ---------------- 增 ----------------

        /// <summary>
        /// 新增一条。主键为空会自动生成；主键已存在则**失败并返回 false**（不静默覆盖）。
        /// </summary>
        public bool Insert(T record)
        {
            if (record == null) return false;

            if (string.IsNullOrEmpty(record.Id)) record.Id = NewId();
            if (_index.ContainsKey(record.Id))
            {
                GameLog.Warn("[Data] " + _name + " 主键已存在，新增被拒绝: " + record.Id);
                return false;
            }

            _records.Add(record);
            _index[record.Id] = record;
            MarkDirty();
            return true;
        }

        /// <summary>批量新增，返回成功条数</summary>
        public int InsertRange(IEnumerable<T> records)
        {
            if (records == null) return 0;
            int n = 0;
            foreach (var r in records)
            {
                if (Insert(r)) n++;
            }
            return n;
        }

        // ---------------- 改 ----------------

        /// <summary>按主键整体替换；不存在返回 false（想"没有就插"请用 Upsert）</summary>
        public bool Update(T record)
        {
            if (record == null || string.IsNullOrEmpty(record.Id)) return false;

            T old;
            if (!_index.TryGetValue(record.Id, out old)) return false;

            int i = _records.IndexOf(old);
            if (i < 0) return false;

            _records[i] = record;
            _index[record.Id] = record;
            MarkDirty();
            return true;
        }

        /// <summary>存在则替换、不存在则新增</summary>
        public bool Upsert(T record)
        {
            if (record == null) return false;
            return Contains(record.Id) ? Update(record) : Insert(record);
        }

        /// <summary>
        /// 就地修改：读出记录 → 改字段 → 调用本方法标记变脏。
        /// 因为 Get 返回的是引用，改完必须打脏，否则不会落盘。
        /// </summary>
        public override void MarkDirty() { _dirty = true; }

        // ---------------- 删 ----------------

        public bool Delete(string id)
        {
            if (string.IsNullOrEmpty(id)) return false;

            T old;
            if (!_index.TryGetValue(id, out old)) return false;

            _index.Remove(id);
            _records.Remove(old);
            MarkDirty();
            return true;
        }

        /// <summary>条件删除，返回删除条数</summary>
        public int DeleteWhere(Predicate<T> match)
        {
            if (match == null) return 0;

            int removed = 0;
            for (int i = _records.Count - 1; i >= 0; i--)
            {
                if (!match(_records[i])) continue;
                _index.Remove(_records[i].Id);
                _records.RemoveAt(i);
                removed++;
            }
            if (removed > 0) MarkDirty();
            return removed;
        }

        public override void Clear()
        {
            if (_records.Count == 0) return;
            _records.Clear();
            _index.Clear();
            MarkDirty();
        }

        // ---------------- 落盘 / 读盘 ----------------

        public override int Load()
        {
            _records.Clear();
            _index.Clear();
            _dirty = false;

            string text = DataFile.ReadAllText(_filePath);
            if (text == null) return -1;      // 文件不存在 = 全新存档，不算错误

            int loaded = 0, failed = 0, lineNo = 0;
            string[] lines = text.Split('\n');
            for (int i = 0; i < lines.Length; i++)
            {
                lineNo++;
                string line = lines[i].Trim();
                if (line.Length == 0) continue;
                if (line.StartsWith(DataFile.CommentPrefix)) continue;

                T rec = null;
                try { rec = JsonUtility.FromJson<T>(line); }
                catch (Exception e)
                {
                    failed++;
                    GameLog.Warn("[Data] " + _name + " 第 " + lineNo + " 行解析失败（已跳过）: " + e.Message);
                    continue;
                }

                if (rec == null) { failed++; continue; }
                if (string.IsNullOrEmpty(rec.Id))
                {
                    failed++;
                    GameLog.Warn("[Data] " + _name + " 第 " + lineNo + " 行缺少主键 id（已跳过）");
                    continue;
                }

                if (_index.ContainsKey(rec.Id))
                {
                    failed++;
                    GameLog.Warn("[Data] " + _name + " 第 " + lineNo + " 行主键重复（已跳过）: " + rec.Id);
                    continue;
                }

                _records.Add(rec);
                _index[rec.Id] = rec;
                loaded++;
            }

            if (failed > 0)
                GameLog.Warn("[Data] " + _name + " 读取完成，成功 " + loaded + " 条，跳过 " + failed + " 条坏数据");

            return loaded;
        }

        public override bool Save()
        {
            var sb = new StringBuilder(256 + _records.Count * 64);
            sb.Append(DataFile.CommentPrefix).Append("table=").Append(_name).Append(" v1\n");
            sb.Append(DataFile.CommentPrefix).Append("saved=")
              .Append(DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ")).Append('\n');
            sb.Append(DataFile.CommentPrefix).Append("count=").Append(_records.Count).Append('\n');

            for (int i = 0; i < _records.Count; i++)
            {
                string line;
                try { line = JsonUtility.ToJson(_records[i]); }
                catch (Exception e)
                {
                    GameLog.Error("[Data] " + _name + " 序列化失败: " + _records[i].Id, e);
                    continue;
                }
                if (string.IsNullOrEmpty(line)) continue;
                // 一条记录必须在一行内（JsonUtility 默认就是紧凑格式，这里再兜一层）
                sb.Append(line.Replace("\r", string.Empty).Replace("\n", " ")).Append('\n');
            }

            if (!DataFile.WriteAllTextAtomic(_filePath, sb.ToString())) return false;
            _dirty = false;
            return true;
        }

        /// <summary>主键生成：表名_时间戳_序号，保证同一毫秒内也不撞</summary>
        private string NewId()
        {
            return _name + "_" + DateTime.UtcNow.Ticks.ToString() + "_" + _records.Count;
        }
    }
}
