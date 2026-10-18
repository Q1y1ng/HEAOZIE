// 进程枚举 / 安全名单 / 结束进程。
//
// 对照原 Python 版 app_killer.py 的 psutil 用法，但这里不依赖任何第三方包：
// 全部走 System.Diagnostics。安全名单照原版 never_kill 的思路，按 Windows 10/11
// 实际进程名补齐 —— 系统关键进程**不进列表、也不能通过点选加入**。

using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace Hz.Sys
{
    internal sealed class ProcEntry
    {
        public int Pid;
        public string Name;
        public string Title;
        public double MemoryMb;
    }

    internal static class ProcList
    {
        private static readonly HashSet<string> Critical = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "system", "system idle process", "idle", "registry", "memory compression", "secure system",
            "smss", "csrss", "wininit", "services", "lsass", "lsaiso", "winlogon", "svchost",
            "fontdrvhost", "dwm", "explorer", "sihost", "shellexperiencehost", "startmenuexperiencehost",
            "textinputhost", "applicationframehost", "searchhost", "searchapp", "taskhostw", "ctfmon",
            "runtimebroker", "audiodg", "audiosrv", "conhost", "wudfhost", "searchindexer", "spoolsv",
            "msmpeng", "nissrv", "securityhealthservice", "securityhealthsystray", "sppsvc",
            "lsm", "dllhost", "wmiprvse", "smartscreen", "sppsvc", "usoclient", "trustedinstaller",
            "tiworker", "wuauclt", "wininit"
        };

        /// 系统关键进程（关掉会掉桌面/掉服务/掉安全防护）。
        public static bool IsProtected(string processName)
        {
            if (string.IsNullOrEmpty(processName)) return true;
            string n = processName.Trim();
            if (n.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) n = n.Substring(0, n.Length - 4);
            return Critical.Contains(n);
        }

        /// 列表里显示的名字：ProcessName 不带 .exe，补上更接近任务管理器的观感。
        public static string DisplayName(string processName)
        {
            if (string.IsNullOrEmpty(processName)) return "(未知)";
            if (processName.IndexOf(' ') >= 0) return processName;          // System Idle Process / Memory Compression
            if (processName.Equals("System", StringComparison.OrdinalIgnoreCase)) return processName;
            if (processName.Equals("Idle", StringComparison.OrdinalIgnoreCase)) return processName;
            if (processName.Equals("Registry", StringComparison.OrdinalIgnoreCase)) return processName;
            return processName + ".exe";
        }

        /// 枚举可关闭的进程（关键进程与本进程已排除），按内存占用从大到小。
        public static List<ProcEntry> Enumerate()
        {
            var list = new List<ProcEntry>();
            int self;
            using (Process me = Process.GetCurrentProcess()) self = me.Id;

            Process[] all;
            try { all = Process.GetProcesses(); }
            catch { return list; }

            foreach (Process p in all)
            {
                try
                {
                    if (p.Id <= 0) continue;               // PID 0 = System Idle Process，不可结束
                    if (p.Id == self) continue;
                    if (IsProtected(p.ProcessName)) continue;

                    double mb = 0;
                    try { mb = p.WorkingSet64 / 1048576.0; } catch { }

                    string title = "";
                    try { title = p.MainWindowTitle ?? ""; } catch { }

                    list.Add(new ProcEntry
                    {
                        Pid = p.Id,
                        Name = DisplayName(p.ProcessName),
                        Title = title,
                        MemoryMb = mb
                    });
                }
                catch { }
                finally { p.Dispose(); }
            }

            list.Sort(delegate(ProcEntry a, ProcEntry b) { return b.MemoryMb.CompareTo(a.MemoryMb); });
            return list;
        }

        public static bool IsSelf(int pid)
        {
            try
            {
                using (Process me = Process.GetCurrentProcess()) return me.Id == pid;
            }
            catch { return false; }
        }

        /// 取进程显示名（点选目标的预览文案用）。
        public static string NameOf(int pid)
        {
            if (pid <= 0) return "";
            try
            {
                using (Process p = Process.GetProcessById(pid)) return DisplayName(p.ProcessName);
            }
            catch { return ""; }
        }

        /// 结束进程：先 Kill，等 3 秒确认；失败原因逐条返回，不静默。
        public static bool Kill(int pid, out string error)
        {
            error = "";
            if (pid <= 0) { error = "无效 PID"; return false; }
            try
            {
                using (Process p = Process.GetProcessById(pid))
                {
                    p.Kill();
                    if (!p.WaitForExit(3000))
                    {
                        error = "已发出结束请求，但 3 秒内未退出";
                        return false;
                    }
                }
                return true;
            }
            catch (ArgumentException) { error = "进程已不存在"; return false; }
            catch (InvalidOperationException) { error = "进程已退出"; return false; }
            catch (Exception ex) { error = ex.GetType().Name + ": " + ex.Message; return false; }
        }
    }
}
