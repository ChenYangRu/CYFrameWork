using System;

namespace GameFramework.Data
{
    /// <summary>
    /// 可持久化数据记录的约定：必须有**唯一字符串主键**。
    ///
    /// ★ 实现时请用"字段 + 属性转发"，不要只写属性：
    ///   Unity 的 JsonUtility **只序列化字段**，纯属性不会被写进档案。
    ///   正确写法：
    ///   <code>
    ///   [Serializable]
    ///   public class ItemSave : IDataRecord
    ///   {
    ///       public string id;                        // ← 真正落盘的是这个字段
    ///       public string Id { get { return id; } set { id = value; } }
    ///       public string name;
    ///       public int count;
    ///   }
    ///   </code>
    /// </summary>
    public interface IDataRecord
    {
        string Id { get; set; }
    }

    /// <summary>
    /// 指定数据表名（决定存档文件名：<c>&lt;name&gt;.txt</c>）。
    /// 不标就用类名小写。**表名一旦上线不要改**，改了等于换了一张新表（旧档读不到）。
    /// </summary>
    [AttributeUsage(AttributeTargets.Class, Inherited = false)]
    public sealed class DataTableAttribute : Attribute
    {
        public readonly string Name;
        public DataTableAttribute(string name) { Name = name; }
    }

    /// <summary>
    /// 便捷基类：把 <c>id</c> 字段与接口属性接好。
    /// 单纯不想重复写转发时继承它即可（JsonUtility 会序列化基类的 public 字段）。
    /// </summary>
    [Serializable]
    public abstract class DataRecordBase : IDataRecord
    {
        public string id;
        public string Id { get { return id; } set { id = value; } }
    }
}
