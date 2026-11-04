using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;

namespace GameFramework
{
    public enum LogLevel
    {
        Debug = 0,
        Info = 1,
        Warn = 2,
        Error = 3,
        None = 4,
    }

    /// <summary>
    /// 全局日志。分级过滤 + 文件落盘 + 可挂接上报通道（接入 Bugly/ Sentry 只改一处）。
    /// 落盘位置：persistentDataPath/logs/game_yyyyMMdd.log，逐条写、异常吞掉（日志不能拖垮游戏）。
    /// </summary>
    public static class GameLog
    {
        public static LogLevel Level = LogLevel.Debug;

        /// <summary>额外监听器（UI 控制台 / 上报 SDK）</summary>
        public static event Action<LogLevel, string> OnLog;

        private static readonly object _fileLock = new object();
        private static StreamWriter _writer;
        private static bool _fileEnabled;
        private static string _logDir;

        public static void EnableFile(bool enable)
        {
            _fileEnabled = enable;
            if (enable) OpenWriter();
        }

        public static void Debug(string msg) { Write(LogLevel.Debug, msg); }
        public static void Info(string msg) { Write(LogLevel.Info, msg); }
        public static void Warn(string msg) { Write(LogLevel.Warn, msg); }
        public static void Error(string msg) { Write(LogLevel.Error, msg); }

        public static void Error(string msg, Exception e)
        {
            Write(LogLevel.Error, e == null ? msg : msg + "\n" + e);
        }

        public static void Write(LogLevel level, string msg)
        {
            if (level < Level || Level == LogLevel.None) return;

            string line = string.Format("[{0}] {1}", level.ToString().ToUpper(), msg);

            switch (level)
            {
                case LogLevel.Debug: UnityEngine.Debug.Log(line); break;
                case LogLevel.Info: UnityEngine.Debug.Log(line); break;
                case LogLevel.Warn: UnityEngine.Debug.LogWarning(line); break;
                default: UnityEngine.Debug.LogError(line); break;
            }

            var handler = OnLog;
            if (handler != null)
            {
                try { handler(level, msg); } catch { }
            }

            if (_fileEnabled) WriteFile(line);
        }

        private static void OpenWriter()
        {
            try
            {
                _logDir = Path.Combine(Application.persistentDataPath, "logs");
                if (!Directory.Exists(_logDir)) Directory.CreateDirectory(_logDir);
                string file = Path.Combine(_logDir, "game_" + DateTime.Now.ToString("yyyyMMdd") + ".log");
                var fs = new FileStream(file, FileMode.Append, FileAccess.Write, FileShare.ReadWrite);
                _writer = new StreamWriter(fs, new UTF8Encoding(false));
                _writer.AutoFlush = true;
            }
            catch (Exception e)
            {
                _fileEnabled = false;
                UnityEngine.Debug.LogWarning("[GameLog] 日志文件打开失败，已降级为仅控制台: " + e.Message);
            }
        }

        private static void WriteFile(string line)
        {
            if (_writer == null) return;
            lock (_fileLock)
            {
                try { _writer.WriteLine(DateTime.Now.ToString("HH:mm:ss.fff") + " " + line); }
                catch { }
            }
        }

        public static void Shutdown()
        {
            lock (_fileLock)
            {
                try { if (_writer != null) { _writer.Flush(); _writer.Dispose(); } } catch { }
                _writer = null;
            }
        }
    }
}
