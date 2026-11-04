using System.Text;
using GameFramework;
using GameFramework.Data;
using GameFramework.UI;
using UnityEngine;
using UnityEngine.UI;

namespace Game.Business
{
    /// <summary>
    /// 存档 / CRUD 演示面板。
    ///
    /// 演示的是"离线存档"这条主链路：
    ///   增（Insert）→ 查（Get / Find）→ 改（就地改 + MarkDirty）→ 删（Delete）→ 存（Save）→ 清档（ResetSlot）
    ///
    /// 文件落在 <c>persistentDataPath/save/&lt;槽位&gt;/</c>：
    ///   player.txt（主角，单条）、items.txt（背包，多条）
    /// 出问题时把那两个 txt 拷出来就能看到全部数据。
    /// </summary>
    public class DataDemoWindow : UIWindow
    {
        private Text _info;
        private Text _list;
        private readonly StringBuilder _sb = new StringBuilder(512);

        protected override void BuildUI()
        {
            var bg = UIUtil.CreatePanel("BG", transform, new Color(0.08f, 0.1f, 0.13f, 1f));
            UIUtil.Stretch(bg.rectTransform);

            var title = UIUtil.CreateText("Title", transform, "存 档 / CRUD", 46, new Color(0.86f, 0.9f, 0.95f));
            Anchor(title.rectTransform, new Vector2(0, -60), new Vector2(1000, 70));

            _info = UIUtil.CreateText("Info", transform, "", 22, new Color(0.7f, 0.78f, 0.86f), TextAnchor.UpperLeft);
            Anchor(_info.rectTransform, new Vector2(0, -10), new Vector2(1400, 200));

            _list = UIUtil.CreateText("List", transform, "", 22, new Color(0.62f, 0.85f, 0.72f), TextAnchor.UpperLeft);
            Anchor(_list.rectTransform, new Vector2(0, -180), new Vector2(1400, 320));

            CreateButton("Add", "新增道具", OnAdd, -560, -260);
            CreateButton("Update", "修改第一条", OnUpdateFirst, -190, -260);
            CreateButton("Delete", "删除第一条", OnDeleteFirst, 190, -260);
            CreateButton("Clear", "清空存档", OnResetSlot, 560, -260);

            CreateButton("Save", "保存到磁盘", OnSave, -560, -360);
            CreateButton("Reload", "从磁盘重读", OnReload, -190, -360);
            CreateButton("Dump", "打印存档路径", OnDump, 190, -360);
            CreateButton("Back", "返回", delegate { Close(); }, 560, -360);
        }

        protected override void OnShow()
        {
            EnsurePlayer();
            Refresh();
        }

        // ---------------- CRUD 动作 ----------------

        /// <summary>增：主键为空时数据层自动生成</summary>
        private void OnAdd()
        {
            var data = GameEntry.Data;
            if (data == null || data.Database == null) return;

            var table = data.Database.Table<ItemSave>();
            var item = new ItemSave
            {
                name = "道具_" + (table.Count + 1),
                count = Random.Range(1, 6),
                quality = Random.Range(1, 4),
            };

            bool ok = table.Insert(item);
            GameLog.Info("[Demo] 新增道具 " + (ok ? "成功 id=" + item.Id : "失败"));
            Refresh();
        }

        /// <summary>改：Get 返回的是引用，就地改后必须 MarkDirty，否则不会落盘</summary>
        private void OnUpdateFirst()
        {
            var data = GameEntry.Data;
            if (data == null || data.Database == null) return;

            var table = data.Database.Table<ItemSave>();
            if (table.Count == 0) { GameLog.Warn("[Demo] 没有可修改的记录"); return; }

            var first = table.Records[0];
            first.count += 1;
            first.quality = Mathf.Clamp(first.quality + 1, 1, 3);
            table.MarkDirty();

            GameLog.Info("[Demo] 已修改 id=" + first.id + " count=" + first.count);
            Refresh();
        }

        /// <summary>删：按主键删</summary>
        private void OnDeleteFirst()
        {
            var data = GameEntry.Data;
            if (data == null || data.Database == null) return;

            var table = data.Database.Table<ItemSave>();
            if (table.Count == 0) { GameLog.Warn("[Demo] 没有可删除的记录"); return; }

            string id = table.Records[0].id;
            bool ok = table.Delete(id);
            GameLog.Info("[Demo] 删除 " + id + (ok ? " 成功" : " 失败"));
            Refresh();
        }

        private void OnSave()
        {
            var data = GameEntry.Data;
            if (data == null) return;
            data.Save();
            Refresh();
        }

        private void OnReload()
        {
            var data = GameEntry.Data;
            if (data == null) return;
            int n = data.Load();
            GameLog.Info("[Demo] 从磁盘重读，共 " + n + " 条");
            Refresh();
        }

        private void OnResetSlot()
        {
            var data = GameEntry.Data;
            if (data == null) return;
            data.ResetSlot();
            EnsurePlayer();
            Refresh();
        }

        private void OnDump()
        {
            var data = GameEntry.Data;
            if (data == null) return;
            GameLog.Info("[Demo] 存档目录：" + data.SlotPath);
            GameLog.Info("[Demo] " + data.Summary());
        }

        // ---------------- 界面 ----------------

        private void EnsurePlayer()
        {
            var data = GameEntry.Data;
            if (data == null || data.Database == null) return;

            var table = data.Database.Table<PlayerSave>();
            var player = table.Get(PlayerSave.FixedId);
            if (player != null) { player.TouchLogin(); table.MarkDirty(); return; }

            table.Insert(PlayerSave.CreateDefault());
            GameLog.Info("[Demo] 首次进入，已创建主角存档");
        }

        private void Refresh()
        {
            var data = GameEntry.Data;
            if (data == null || data.Database == null)
            {
                if (_info != null) _info.text = "数据服务未初始化（GameLauncher 没起来？）";
                if (_list != null) _list.text = string.Empty;
                return;
            }

            var playerTable = data.Database.Table<PlayerSave>();
            var player = playerTable.Get(PlayerSave.FixedId);
            var itemTable = data.Database.Table<ItemSave>();

            _sb.Length = 0;
            _sb.Append("游戏模式：").Append(GameModeUtil.NameOf(data.Mode))
               .Append(data.IsAuthoritative ? "（本地为权威数据）" : "（本地仅缓存）").Append('\n');
            _sb.Append("存档槽位：").Append(data.SlotName)
               .Append("    自动存档：").Append(data.AutoSaveSeconds.ToString("0")).Append("s\n");
            _sb.Append("存档路径：").Append(data.SlotPath).Append('\n');
            if (player != null)
            {
                _sb.Append("主角：").Append(player.nickname)
                   .Append("  Lv.").Append(player.level)
                   .Append("  金币 ").Append(player.coin).Append('\n');
            }
            _sb.Append("表：player ").Append(playerTable.Count).Append(" 条 / items ")
               .Append(itemTable.Count).Append(" 条");
            if (_info != null) _info.text = _sb.ToString();

            _sb.Length = 0;
            if (itemTable.Count == 0)
            {
                _sb.Append("（背包为空，点「新增道具」试试）");
            }
            else
            {
                // 查：Get（按主键）与 Find（按条件）两种都演示一下
                var rare = itemTable.Find(delegate (ItemSave x) { return x.quality >= 2; });
                _sb.Append("查询：共 ").Append(itemTable.Count)
                   .Append(" 条，其中品质≥2 的有 ").Append(rare.Count).Append(" 条\n\n");

                int show = Mathf.Min(itemTable.Count, 8);
                for (int i = 0; i < show; i++)
                {
                    var it = itemTable.Records[i];
                    _sb.Append(i + 1).Append(". ").Append(it.name)
                       .Append("  ×").Append(it.count)
                       .Append("  品质 ").Append(it.quality)
                       .Append("  id=").Append(it.id).Append('\n');
                }
                if (itemTable.Count > show) _sb.Append("… 还有 ").Append(itemTable.Count - show).Append(" 条");
            }
            if (_list != null) _list.text = _sb.ToString();
        }

        private void CreateButton(string name, string label, System.Action action, float x, float y)
        {
            var btn = UIUtil.CreateButton(name, transform, label, action, new Vector2(340, 64), 22);
            var rt = btn.transform as RectTransform;
            rt.anchorMin = new Vector2(0.5f, 0.5f);
            rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = new Vector2(x, y);
        }

        private static void Anchor(RectTransform rt, Vector2 pos, Vector2 size)
        {
            rt.anchorMin = new Vector2(0.5f, 0.5f);
            rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = size;
            rt.anchoredPosition = pos;
        }
    }
}
