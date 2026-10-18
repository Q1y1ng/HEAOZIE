// 临时文件清理：只清白名单目录的**内容**，且**绝不跟随重解析点**。
//
// 为什么自己写递归删除而不是 Directory.Delete(path, true)：
// 本机曾因递归删除跟随 NTFS 联接（junction）被清空整盘（见 E:\报告.md）。
// 这里显式检查 FileAttributes.ReparsePoint：遇到联接/符号链接只摘链接本身，
// 一步都不进它指向的目标。

using System;
using System.Collections.Generic;
using System.IO;

namespace Hz.Sys
{
    internal sealed class CleanResult
    {
        public double FreedMb;
        public int FilesDeleted;
        public int Failed;
        public int SkippedLinks;
        public readonly List<string> Lines = new List<string>();
    }

    internal static class Cleanup
    {
        /// 允许清理的目录（白名单，写死在这里，不接受外部传入路径）。
        public static List<string> TargetDirs(bool includePrefetch)
        {
            var list = new List<string>();
            Add(list, Environment.GetEnvironmentVariable("TEMP"), "用户临时文件 (%TEMP%)");
            Add(list, Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Temp"), "Windows 临时文件");
            if (includePrefetch)
            {
                Add(list, Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Prefetch"), "Prefetch（管理员）");
            }
            string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            Add(list, Path.Combine(local, "D3DSCache"), "DirectX 着色器缓存");
            Add(list, Path.Combine(local, @"Microsoft\Edge\User Data\Default\Cache"), "Edge 缓存");
            Add(list, Path.Combine(local, @"Google\Chrome\User Data\Default\Cache"), "Chrome 缓存");
            return list;
        }

        private static void Add(List<string> list, string path, string label)
        {
            if (string.IsNullOrEmpty(path)) return;
            list.Add(path + "|" + label);
        }

        public static CleanResult Clean(bool includePrefetch, Action<string> log)
        {
            var result = new CleanResult();
            foreach (string entry in TargetDirs(includePrefetch))
            {
                int sep = entry.LastIndexOf('|');
                string dir = entry.Substring(0, sep);
                string label = entry.Substring(sep + 1);
                try
                {
                    if (!Directory.Exists(dir)) continue;
                    if (!IsSafeTarget(dir))
                    {
                        result.Lines.Add("跳过（路径过于宽泛，安全起见不清理）：" + dir);
                        continue;
                    }
                    long freed = 0;
                    int failed = 0;
                    int links = 0;
                    int deleted = 0;
                    foreach (string child in Directory.GetFileSystemEntries(dir))
                    {
                        DeleteTree(child, ref freed, ref failed, ref links, ref deleted);
                    }
                    if (freed > 0 || failed > 0)
                    {
                        result.Lines.Add(label + "：" + (freed / 1048576.0).ToString("0.0") + " MB，删除 " + deleted +
                                         " 项，失败 " + failed + " 项" +
                                         (links > 0 ? "，跳过联接 " + links + " 个" : ""));
                    }
                    result.FreedMb += freed / 1048576.0;
                    result.FilesDeleted += deleted;
                    result.Failed += failed;
                    result.SkippedLinks += links;
                }
                catch (Exception ex)
                {
                    result.Lines.Add(label + " 清理异常：" + ex.Message);
                }
            }
            result.FreedMb = Math.Round(result.FreedMb, 1);
            if (log != null)
            {
                foreach (string line in result.Lines) log(line);
            }
            return result;
        }

        /// 拒绝清理盘根、Windows 目录本身这类过于宽泛的目标。
        public static bool IsSafeTarget(string dir)
        {
            string full;
            try { full = Path.GetFullPath(dir).TrimEnd('\\'); }
            catch { return false; }
            if (full.Length < 10) return false;
            string root = Path.GetPathRoot(full);
            if (string.IsNullOrEmpty(root)) return false;
            if (string.Equals(full, root.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase)) return false;
            if (string.Equals(full, Environment.GetFolderPath(Environment.SpecialFolder.Windows).TrimEnd('\\'),
                    StringComparison.OrdinalIgnoreCase)) return false;
            if (string.Equals(full, Environment.GetFolderPath(Environment.SpecialFolder.UserProfile).TrimEnd('\\'),
                    StringComparison.OrdinalIgnoreCase)) return false;
            return true;
        }

        private static void DeleteTree(string path, ref long freed, ref int failed, ref int links, ref int deleted)
        {
            FileAttributes attr;
            try { attr = File.GetAttributes(path); }
            catch { failed++; return; }

            if ((attr & FileAttributes.ReparsePoint) != 0)
            {
                // 联接 / 符号链接：只摘链接本身（非递归），目标一个字节都不动
                try
                {
                    if ((attr & FileAttributes.Directory) != 0) Directory.Delete(path, false);
                    else File.Delete(path);
                }
                catch { failed++; }
                links++;
                return;
            }

            if ((attr & FileAttributes.Directory) != 0)
            {
                string[] children;
                try { children = Directory.GetFileSystemEntries(path); }
                catch { failed++; return; }
                foreach (string child in children) DeleteTree(child, ref freed, ref failed, ref links, ref deleted);
                try { Directory.Delete(path, false); }
                catch { failed++; }
                return;
            }

            try
            {
                long size = 0;
                try { size = new FileInfo(path).Length; } catch { }
                File.Delete(path);
                freed += size;
                deleted++;
            }
            catch
            {
                failed++;   // 被占用 / 无权限：正常，不报错
            }
        }

        /// 只测量不删除（状态卡片展示"可清理多少"用）。
        public static double Measure()
        {
            double total = 0;
            foreach (string entry in TargetDirs(false))
            {
                string dir = entry.Substring(0, entry.LastIndexOf('|'));
                try
                {
                    if (!Directory.Exists(dir) || !IsSafeTarget(dir)) continue;
                    foreach (string child in Directory.GetFileSystemEntries(dir))
                    {
                        total += MeasureTree(child, 0);
                    }
                }
                catch { }
            }
            return total / 1048576.0;
        }

        private static double MeasureTree(string path, int depth)
        {
            if (depth > 8) return 0;
            try
            {
                FileAttributes attr = File.GetAttributes(path);
                if ((attr & FileAttributes.ReparsePoint) != 0) return 0;
                if ((attr & FileAttributes.Directory) == 0)
                {
                    try { return new FileInfo(path).Length; } catch { return 0; }
                }
                double sum = 0;
                foreach (string child in Directory.GetFileSystemEntries(path)) sum += MeasureTree(child, depth + 1);
                return sum;
            }
            catch { return 0; }
        }
    }
}
