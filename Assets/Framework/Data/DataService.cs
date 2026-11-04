using System;
using System.IO;
using UnityEngine;

namespace GameFramework.Data
{
    /// <summary>
    /// 数据服务：存档槽位管理 + 自动存档 + 对业务暴露的 CRUD 门面。
    /// 由 GameLauncher 在启动时创建并注册到 ServiceLocator，业务用
    /// <c>ServiceLocator.Get&lt;DataService&gt;()</c> 或 <c>GameEntry.Data</c> 取。
    ///
    /// **离线与联网的差别只在这一层**：
    ///   · 离线：本地就是权威数据（<see cref="IsAuthoritative"/> = true），改了就存
    ///   · 联网：本地是**缓存**（服务端为准），仍可读写用于断线续玩，但上线后应被服务端数据覆盖
    ///
    /// 存档位置：<c>Application.persistentDataPath/save/&lt;slot&gt;/</c>
    /// （Android = /storage/emulated/0/Android/data/&lt;pkg&gt;/files；iOS = 沙盒 Library；
    ///   WebGL / 小游戏 = IndexedDB 虚拟文件系统）
    /// </summary>
    public sealed class DataService
    {
        public const string DefaultSlot = "default";
        /// <summary>存档格式版本：改结构（删字段/换语义）时 +1，用于旧档迁移判断</summary>
        public const int FormatVersion = 1;

        private const string MetaFile = "slot.txt";

        private LocalDatabase _db;
        private KeyValueStore _settings;
        private KeyValueStore _meta;
        private float _sinceSave;
        private bool _ticking;

        /// <summary>当前运行模式（决定本地是否为权威数据）</summary>
        public GameMode Mode { get; private set; }

        /// <summary>本地数据是否权威：离线 = 是；联网 = 否（本地只是缓存）</summary>
        public bool IsAuthoritative { get { return Mode == GameMode.Offline; } }

        public string SlotName { get; private set; }
        public string SlotPath { get; private set; }
        public bool Initialized { get; private set; }

        /// <summary>存档数据表入口</summary>
        public LocalDatabase Database { get { return _db; } }
        /// <summary>设置项（音量/画质/语言…）</summary>
        public KeyValueStore Settings { get { return _settings; } }
        /// <summary>槽位元信息（创建时间/最后存档/游玩次数）</summary>
        public KeyValueStore SlotMeta { get { return _meta; } }

        /// <summary>自动存档间隔（秒）；&lt;=0 关闭。掉电/杀进程只丢这么久的进度</summary>
        public float AutoSaveSeconds = 20f;

        /// <summary>本次已运行的秒数（每次 Save 会累加并落盘，用于"游玩时长"展示）</summary>
        public float PlaySeconds { get; private set; }

        public event Action OnSaved;
        public event Action OnLoaded;

        public bool SlotExists
        {
            get { return !string.IsNullOrEmpty(SlotPath) && Directory.Exists(SlotPath); }
        }

        public DateTime? LastSaveUtc
        {
            get
            {
                if (_meta == null) return null;
                long ticks;
                if (!long.TryParse(_meta.Get("lastSaveTicks"), out ticks) || ticks <= 0) return null;
                return new DateTime(ticks, DateTimeKind.Utc);
            }
        }

        // ---------------- 生命周期 ----------------

        /// <summary>
        /// 打开存档槽位。返回 false 表示目录建不起来（权限/磁盘问题），
        /// 此时**游戏仍可继续跑**，只是所有 CRUD 都落在内存里（退出即丢）。
        /// </summary>
        public bool Init(GameMode mode, string slotName = DefaultSlot)
        {
            Mode = mode;
            SlotName = string.IsNullOrEmpty(slotName) ? DefaultSlot : SanitizeSlot(slotName);
            SlotPath = Path.Combine(Path.Combine(Application.persistentDataPath, "save"), SlotName);

            bool dirOk = true;
            try { DataFile.EnsureDirectory(SlotPath); dirOk = Directory.Exists(SlotPath); }
            catch (Exception e) { GameLog.Error("[Data] 存档目录不可用: " + SlotPath, e); dirOk = false; }

            _db = new LocalDatabase(SlotPath);
            _settings = new KeyValueStore(Path.Combine(SlotPath, "settings.txt"));
            _meta = new KeyValueStore(Path.Combine(SlotPath, MetaFile));

            // 槽位元信息
            // ★ ticks 是 18 位整数，用 float 存会丢精度（float 只有 ~7 位有效数字）→ 一律按字符串存
            if (!_meta.Has("createdTicks"))
                _meta.Set("createdTicks", DateTime.UtcNow.Ticks.ToString());
            _meta.SetInt("formatVersion", FormatVersion);
            _meta.SetInt("playCount", _meta.GetInt("playCount", 0) + 1);
            _meta.SetInt("lastMode", (int)mode);
            _meta.Save();

            Initialized = true;
            GameLog.Info(string.Format("[Data] 存档就绪 mode={0} slot={1} 路径={2}",
                GameModeUtil.NameOf(mode), SlotName, SlotPath));

            if (!dirOk)
            {
                GameLog.Warn("[Data] 存档目录不可写，本次数据只存在内存里（退出即丢）");
            }
            else if (!_meta.Has("lastSaveTicks"))
            {
                GameLog.Info("[Data] 这是全新存档（磁盘上没有历史数据）");
            }

            if (!_ticking && AutoSaveSeconds > 0f)
            {
                GameLoop.Ensure().OnTick += Tick;
                _ticking = true;
            }

            return dirOk;
        }

        public void Dispose()
        {
            try { Save(); } catch (Exception e) { GameLog.Error("[Data] 退出存档失败", e); }
            if (_ticking && GameLoop.Exists) GameLoop.Instance.OnTick -= Tick;
            _ticking = false;
            Initialized = false;
        }

        // ---------------- 存档 ----------------

        /// <summary>保存：脏表 + 设置 + 元信息。返回是否全部成功</summary>
        public bool Save()
        {
            if (!Initialized || _db == null) return false;

            bool ok = _db.SaveDirty();
            if (_settings != null && _settings.Dirty) ok &= _settings.Save();

            if (_meta != null)
            {
                _meta.Set("lastSaveTicks", DateTime.UtcNow.Ticks.ToString());
                _meta.SetFloat("playSeconds", PlaySeconds);
                ok &= _meta.Save();
            }

            if (ok)
            {
                _sinceSave = 0f;
                var h = OnSaved;
                if (h != null) { try { h(); } catch (Exception e) { GameLog.Error("[Data] OnSaved 回调异常", e); } }
                GameLog.Info(string.Format("[Data] 已存档（{0}）", SlotPath));
            }
            return ok;
        }

        /// <summary>重新从磁盘加载全部已打开的表（会丢弃内存改动）</summary>
        public int Load()
        {
            if (!Initialized || _db == null) return 0;
            int n = _db.LoadAll();
            _settings = new KeyValueStore(Path.Combine(SlotPath, "settings.txt"));
            var h = OnLoaded;
            if (h != null) { try { h(); } catch (Exception e) { GameLog.Error("[Data] OnLoaded 回调异常", e); } }
            return n;
        }

        /// <summary>强制落盘并打日志（切场景、退出、充值/关键操作后调用）</summary>
        public void Flush() { Save(); }

        /// <summary>清档：删除本槽位全部文件并重建空库（不可撤销）</summary>
        public bool ResetSlot()
        {
            if (_db == null) return false;
            bool ok = _db.DeleteAllFiles();
            _settings = new KeyValueStore(Path.Combine(SlotPath, "settings.txt"));
            _meta = new KeyValueStore(Path.Combine(SlotPath, MetaFile));
            _meta.Set("createdTicks", DateTime.UtcNow.Ticks.ToString());
            _meta.SetInt("formatVersion", FormatVersion);
            _meta.SetInt("playCount", 0);
            _meta.Save();
            PlaySeconds = 0f;
            GameLog.Warn("[Data] 存档已清空: " + SlotPath);
            return ok;
        }

        // ---------------- 自动存档 ----------------

        private void Tick(float dt)
        {
            PlaySeconds += dt;
            if (AutoSaveSeconds <= 0f) return;

            _sinceSave += dt;
            if (_sinceSave < AutoSaveSeconds) return;
            if (_db == null || _db.DirtyCount == 0) { _sinceSave = 0f; return; }

            Save();
        }

        // ---------------- 调试 ----------------

        /// <summary>一行摘要（调试面板用）</summary>
        public string Summary()
        {
            if (!Initialized) return "数据服务未初始化";
            return string.Format("模式={0} 槽位={1} 已建表={2} 待存={3} 游玩={4}s\n路径={5}",
                GameModeUtil.NameOf(Mode), SlotName,
                _db != null ? _db.LoadedTableCount : 0,
                _db != null ? _db.DirtyCount : 0,
                Mathf.RoundToInt(PlaySeconds), SlotPath);
        }

        private static string SanitizeSlot(string slot)
        {
            var sb = new System.Text.StringBuilder(slot.Length);
            for (int i = 0; i < slot.Length; i++)
            {
                char c = slot[i];
                if ((c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9') ||
                    c == '_' || c == '-') sb.Append(c);
            }
            return sb.Length == 0 ? DefaultSlot : sb.ToString();
        }
    }
}
