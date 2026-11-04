using System;
using GameFramework.Data;

namespace Game.Business
{
    /// <summary>
    /// 主角存档（单条记录，主键固定为 "player"）。
    ///
    /// ★ 落盘字段必须是 **public 字段**：JsonUtility 只序列化字段，属性不写盘。
    ///   所以这里既有字段 <c>id</c>，又有接口要求的属性 <c>Id</c>（转发到字段）。
    /// </summary>
    [Serializable]
    [DataTable("player")]
    public class PlayerSave : IDataRecord
    {
        /// <summary>固定主键：主角只有一条记录，用固定 id 便于 Get(player)</summary>
        public const string FixedId = "player";

        public string id = FixedId;
        public string nickname = "Player";
        public int level = 1;
        public int coin = 0;
        public int exp = 0;
        /// <summary>最后登录时间（UTC ticks），用来算"离线收益"之类</summary>
        public long lastLoginTicks;

        public string Id { get { return id; } set { id = value; } }

        public static PlayerSave CreateDefault()
        {
            var p = new PlayerSave();
            p.lastLoginTicks = DateTime.UtcNow.Ticks;
            return p;
        }

        public void TouchLogin() { lastLoginTicks = DateTime.UtcNow.Ticks; }
    }
}
