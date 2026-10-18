// 日志：单文件追加 + 超过上限自动轮转。定位问题时它是唯一的现场证据。

using System;
using System.Globalization;
using System.IO;
using System.Text;

namespace Hz
{
    internal static class Log
    {
        private static readonly object Gate = new object();
        private const long MaxBytes = 512 * 1024;
        private static bool _failed;

        public static void Info(string msg) { Write("INFO", msg); }
        public static void Warn(string msg) { Write("WARN", msg); }
        public static void Error(string msg) { Write("ERROR", msg); }

        public static void Error(string msg, Exception ex)
        {
            Write("ERROR", msg + " :: " + (ex == null ? "(无异常对象)" : ex.GetType().Name + ": " + ex.Message + "\n" + ex.StackTrace));
        }

        private static void Write(string level, string msg)
        {
            if (_failed) return;
            try
            {
                lock (Gate)
                {
                    string path = AppConfig.LogPath;
                    string dir = Path.GetDirectoryName(path);
                    if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir)) Directory.CreateDirectory(dir);
                    Rotate(path);
                    string line = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture)
                                  + " [" + level + "] " + msg + Environment.NewLine;
                    File.AppendAllText(path, line, new UTF8Encoding(false));
                }
            }
            catch
            {
                // 日志写不进去（只读目录/磁盘满）不该拖垮应用：静默降级，只记一次
                _failed = true;
            }
        }

        private static void Rotate(string path)
        {
            try
            {
                var fi = new FileInfo(path);
                if (!fi.Exists || fi.Length < MaxBytes) return;
                string old = path + ".1";
                if (File.Exists(old)) File.Delete(old);
                File.Move(path, old);
            }
            catch { /* 轮转失败就继续往原文件写 */ }
        }

        /// 读回最后 N 行（设置页/诊断导出用）。
        public static string Tail(int lines)
        {
            try
            {
                string path = AppConfig.LogPath;
                if (!File.Exists(path)) return "(暂无日志)";
                string[] all = File.ReadAllLines(path, Encoding.UTF8);
                int start = Math.Max(0, all.Length - Math.Max(1, lines));
                var sb = new StringBuilder();
                for (int i = start; i < all.Length; i++) sb.AppendLine(all[i]);
                return sb.ToString();
            }
            catch (Exception ex)
            {
                return "(日志读取失败: " + ex.Message + ")";
            }
        }
    }
}
