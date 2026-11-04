using System;
using GameFramework.Data;

namespace Game.Business
{
    /// <summary>
    /// 背包道具（多条记录，CRUD 演示的主角）。
    /// 主键由数据层自动生成（<c>items_时间戳_序号</c>），也可以自己指定业务 id。
    /// </summary>
    [Serializable]
    [DataTable("items")]
    public class ItemSave : IDataRecord
    {
        public string id;
        public string name = "道具";
        public int count = 1;
        /// <summary>品质：1 普通 / 2 稀有 / 3 史诗（示例用）</summary>
        public int quality = 1;

        public string Id { get { return id; } set { id = value; } }
    }
}
