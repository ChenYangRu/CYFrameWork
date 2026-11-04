using System;
using System.IO;
using System.Text;

namespace GameFramework.Data
{
    /// <summary>
    /// 文本档案的低层读写。
    ///
    /// 为什么用 txt 而不是二进制：
    ///   · 线上出问题能直接打开看（真机上拷回来就能定位）
    ///   · 进 git 能看到 diff（策划改数值、程序查字段都方便）
    ///   · 小游戏/WebGL 的虚拟文件系统对纯文本最友好，不涉及大小端/对齐
    ///
    /// 为什么写文件要"临时文件 + 原子替换"：
    ///   手机/小游戏被系统杀进程时，直接覆盖原文件会留下**半截文件**（下次读档必炸）。
    ///   先写 .tmp 再替换，最坏情况是丢掉这次写入，而不是毁掉整个存档。
    /// </summary>
    internal static class DataFile
    {
        /// <summary>档案里的注释/元信息行前缀（读取时整行跳过）</summary>
        public const string CommentPrefix = "#";

        private static readonly Encoding Utf8NoBom = new UTF8Encoding(false);

        public static bool Exists(string path)
        {
            try { return !string.IsNullOrEmpty(path) && File.Exists(path); }
            catch (Exception) { return false; }
        }

        public static string ReadAllText(string path)
        {
            try { return File.Exists(path) ? File.ReadAllText(path, Utf8NoBom) : null; }
            catch (Exception e)
            {
                GameLog.Error("[Data] 读取失败: " + path, e);
                return null;
            }
        }

        /// <summary>原子写入：.tmp → 替换目标（旧文件留一份 .bak 兜底）</summary>
        public static bool WriteAllTextAtomic(string path, string text)
        {
            if (string.IsNullOrEmpty(path)) return false;
            try
            {
                EnsureDirectory(Path.GetDirectoryName(path));

                string tmp = path + ".tmp";
                File.WriteAllText(tmp, text ?? string.Empty, Utf8NoBom);

                if (File.Exists(path))
                {
                    try
                    {
                        // 支持时走原子替换，并留一份 .bak
                        File.Replace(tmp, path, path + ".bak");
                    }
                    catch (Exception)
                    {
                        // 部分平台（WebGL 虚拟文件系统 / 某些 Android 分区）不支持 Replace
                        SafeDelete(path);
                        File.Move(tmp, path);
                    }
                }
                else
                {
                    File.Move(tmp, path);
                }
                return true;
            }
            catch (Exception e)
            {
                GameLog.Error("[Data] 写入失败: " + path, e);
                return false;
            }
        }

        /// <summary>读上一版备份（主档损坏时的手动兜底入口）</summary>
        public static string ReadBackup(string path)
        {
            return ReadAllText(path + ".bak");
        }

        public static void EnsureDirectory(string dir)
        {
            if (string.IsNullOrEmpty(dir)) return;
            try { if (!Directory.Exists(dir)) Directory.CreateDirectory(dir); }
            catch (Exception e) { GameLog.Error("[Data] 建目录失败: " + dir, e); }
        }

        public static void SafeDelete(string path)
        {
            try { if (File.Exists(path)) File.Delete(path); }
            catch (Exception e) { GameLog.Warn("[Data] 删除失败: " + path + " → " + e.Message); }
        }
    }
}
