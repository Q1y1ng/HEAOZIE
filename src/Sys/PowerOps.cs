// 电源操作：睡眠 / 关机 / 重启。原 Python 版用的是同一套底层调用
// （powrprof.SetSuspendState(0,1,0) 与 shutdown.exe），这里只是换成受控的托管调用。

using System;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Hz.Sys
{
    internal enum PowerAction
    {
        Sleep = 0,
        Shutdown = 1,
        Restart = 2
    }

    internal static class PowerOps
    {
        [DllImport("powrprof.dll", SetLastError = true)]
        private static extern bool SetSuspendState(bool hibernate, bool forceCritical, bool disableWakeEvent);

        public static PowerAction Parse(string text, PowerAction fallback)
        {
            if (string.IsNullOrEmpty(text)) return fallback;
            switch (text.Trim().ToLowerInvariant())
            {
                case "sleep": return PowerAction.Sleep;
                case "shutdown": return PowerAction.Shutdown;
                case "restart": return PowerAction.Restart;
                default: return fallback;
            }
        }

        public static string ToConfig(PowerAction action)
        {
            switch (action)
            {
                case PowerAction.Shutdown: return "shutdown";
                case PowerAction.Restart: return "restart";
                default: return "sleep";
            }
        }

        /// 短标签（按钮/胶囊用）。
        public static string Label(PowerAction action)
        {
            switch (action)
            {
                case PowerAction.Shutdown: return "关机";
                case PowerAction.Restart: return "重启";
                default: return "睡眠";
            }
        }

        /// 完整动词（状态文案用）。
        public static string Verb(PowerAction action)
        {
            switch (action)
            {
                case PowerAction.Shutdown: return "关闭电脑";
                case PowerAction.Restart: return "重启电脑";
                default: return "让电脑睡眠";
            }
        }

        public static GI Glyph(PowerAction action)
        {
            switch (action)
            {
                case PowerAction.Shutdown: return GI.Power;
                case PowerAction.Restart: return GI.Refresh;
                default: return GI.Clock;
            }
        }

        /// 执行动作。睡眠不会返回「失败」以外的信息 —— 成功时本进程会被挂起，唤醒后继续往下走。
        public static bool Execute(PowerAction action, out string error)
        {
            error = "";
            try
            {
                switch (action)
                {
                    case PowerAction.Sleep:
                        // hibernate=false, forceCritical=true, disableWakeEvent=false —— 与 Python 版一致
                        if (!SetSuspendState(false, true, false))
                        {
                            error = "SetSuspendState 返回失败（可能被电源策略或权限拒绝）";
                            return false;
                        }
                        return true;
                    case PowerAction.Shutdown:
                        return RunShutdown("/s /t 0", out error);
                    case PowerAction.Restart:
                        return RunShutdown("/r /t 0", out error);
                    default:
                        error = "未知操作";
                        return false;
                }
            }
            catch (Exception ex)
            {
                error = ex.GetType().Name + ": " + ex.Message;
                return false;
            }
        }

        private static bool RunShutdown(string args, out string error)
        {
            error = "";
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "shutdown.exe",
                    Arguments = args,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                using (Process p = Process.Start(psi))
                {
                    if (p == null) { error = "无法启动 shutdown.exe"; return false; }
                    if (!p.WaitForExit(8000)) { error = "shutdown.exe 超时未返回"; return false; }
                    if (p.ExitCode != 0) { error = "shutdown.exe 退出码 " + p.ExitCode; return false; }
                }
                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
        }

        /// 撤销一个已排入系统的关机（本应用自己倒计时，正常路径用不到，留作兜底）。
        public static void AbortPendingShutdown()
        {
            try
            {
                using (Process p = Process.Start(new ProcessStartInfo
                {
                    FileName = "shutdown.exe",
                    Arguments = "/a",
                    UseShellExecute = false,
                    CreateNoWindow = true
                }))
                {
                    if (p != null) p.WaitForExit(5000);
                }
            }
            catch { }
        }
    }
}
