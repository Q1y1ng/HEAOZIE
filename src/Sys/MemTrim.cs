// 内存整理：本机已有两套做法，这里取正确的那个。
//
//   · 原 Python 版 booster_core.py：逐进程 OpenProcess + EmptyWorkingSet（有效，但只能清用户态工作集），
//     后面那段所谓"清理 Standby Memory"其实是两条毫无作用的 PowerShell 咒语（ClearLog / ReleaseComObject），
//     纯属装饰 —— 不照抄。
//   · 本机 memclean（E:\AI\tools\memclean）：启用 SeProfileSingleProcessPrivilege 后调
//     NtSetSystemInformation(SystemMemoryListInformation, MemoryEmptyWorkingSets)，
//     这是与 RAMMap「Empty Working Sets」等价的系统级调用，需要管理员。
//
// 这里：有管理员就用系统级调用，没有就回退逐进程空工作集，并且**如实报告**释放了多少。

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;

namespace Hz.Sys
{
    internal sealed class MemTrimResult
    {
        public double AvailableBeforeMb;
        public double AvailableAfterMb;
        public double FreedMb;
        public int ProcessesTouched;
        public int ProcessesFailed;
        public bool UsedSystemCall;
        public string Mode = "";
        public readonly List<string> Lines = new List<string>();
    }

    internal static class MemTrim
    {
        private const int SystemMemoryListInformation = 0x50;
        private const int CmdEmptyWorkingSets = 2;
        private const int CmdFlushModifiedList = 3;

        private const string PrivilegeProfileSingleProcess = "SeProfileSingleProcessPrivilege";
        private const uint TokenAdjustPrivileges = 0x0020;
        private const uint TokenQuery = 0x0008;
        private const uint SePrivilegeEnabled = 0x00000002;
        private const int ProcessSetQuota = 0x0100;
        private const int ProcessQueryInformation = 0x0400;

        [StructLayout(LayoutKind.Sequential)]
        private struct LUID
        {
            public uint LowPart;
            public int HighPart;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct LUID_AND_ATTRIBUTES
        {
            public LUID Luid;
            public uint Attributes;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct TOKEN_PRIVILEGES
        {
            public uint PrivilegeCount;
            public LUID_AND_ATTRIBUTES Privilege;
        }

        [DllImport("ntdll.dll", ExactSpelling = true)]
        private static extern int NtSetSystemInformation(int infoClass, IntPtr info, int length);

        [DllImport("kernel32.dll", ExactSpelling = true)]
        private static extern IntPtr GetCurrentProcess();

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr OpenProcess(int access, bool inheritHandle, int pid);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool CloseHandle(IntPtr handle);

        [DllImport("psapi.dll", SetLastError = true)]
        private static extern bool EmptyWorkingSet(IntPtr process);

        [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool OpenProcessToken(IntPtr process, uint access, out IntPtr token);

        [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool LookupPrivilegeValue(string system, string name, out LUID luid);

        [DllImport("advapi32.dll", SetLastError = true)]
        private static extern bool AdjustTokenPrivileges(IntPtr token, bool disableAll, ref TOKEN_PRIVILEGES newState,
                                                         int bufferLength, IntPtr previous, IntPtr returnLength);

        public static bool EnablePrivilege(string name)
        {
            IntPtr token;
            if (!OpenProcessToken(GetCurrentProcess(), TokenAdjustPrivileges | TokenQuery, out token)) return false;
            try
            {
                LUID luid;
                if (!LookupPrivilegeValue(null, name, out luid)) return false;
                var tp = new TOKEN_PRIVILEGES
                {
                    PrivilegeCount = 1,
                    Privilege = new LUID_AND_ATTRIBUTES { Luid = luid, Attributes = SePrivilegeEnabled }
                };
                if (!AdjustTokenPrivileges(token, false, ref tp, 0, IntPtr.Zero, IntPtr.Zero)) return false;
                return Marshal.GetLastWin32Error() == 0;   // ERROR_NOT_ALL_ASSIGNED = 1300 表示没拿到
            }
            finally
            {
                CloseHandle(token);
            }
        }

        /// strong=true 时优先走系统级调用（需管理员），否则逐进程空工作集。
        public static MemTrimResult Run(bool strong)
        {
            var result = new MemTrimResult();
            result.AvailableBeforeMb = SystemMonitor.AvailableMb();
            bool admin = SysEnv.IsAdmin();

            if (strong && admin && EnablePrivilege(PrivilegeProfileSingleProcess))
            {
                result.UsedSystemCall = true;
                result.Mode = "强效 · 系统级空工作集";
                IntPtr buffer = Marshal.AllocHGlobal(4);
                try
                {
                    Marshal.WriteInt32(buffer, CmdEmptyWorkingSets);
                    int status = NtSetSystemInformation(SystemMemoryListInformation, buffer, 4);
                    result.Lines.Add(status == 0
                        ? "已执行系统级「清空工作集」（等同 RAMMap 的 Empty Working Sets）"
                        : "系统级清空工作集返回 " + status + "（0x" + status.ToString("X") + "）");

                    Marshal.WriteInt32(buffer, CmdFlushModifiedList);
                    int status2 = NtSetSystemInformation(SystemMemoryListInformation, buffer, 4);
                    result.Lines.Add(status2 == 0
                        ? "已刷新已修改页列表"
                        : "刷新已修改页列表返回 " + status2);
                }
                catch (Exception ex)
                {
                    result.Lines.Add("系统级调用异常：" + ex.Message);
                    result.UsedSystemCall = false;
                }
                finally
                {
                    Marshal.FreeHGlobal(buffer);
                }
            }
            else if (strong)
            {
                result.Mode = "强效不可用 · 已回退温和";
                result.Lines.Add(admin
                    ? "未能启用 SeProfileSingleProcessPrivilege，回退逐进程空工作集"
                    : "非管理员运行：系统级空工作集需要管理员，已回退逐进程空工作集");
            }
            else
            {
                result.Mode = "温和 · 逐进程空工作集";
            }

            int touched = 0;
            int failed = 0;
            Process[] all;
            try { all = Process.GetProcesses(); }
            catch { all = new Process[0]; }

            foreach (Process p in all)
            {
                try
                {
                    if (p.Id <= 4) continue;
                    IntPtr handle = OpenProcess(ProcessSetQuota | ProcessQueryInformation, false, p.Id);
                    if (handle == IntPtr.Zero) { failed++; continue; }
                    try
                    {
                        if (EmptyWorkingSet(handle)) touched++;
                        else failed++;
                    }
                    finally
                    {
                        CloseHandle(handle);
                    }
                }
                catch { failed++; }
                finally { p.Dispose(); }
            }

            result.ProcessesTouched = touched;
            result.ProcessesFailed = failed;
            result.Lines.Add("逐进程清空工作集：成功 " + touched + " 个，跳过/失败 " + failed + " 个（多为系统进程，属正常）");

            Thread.Sleep(500);   // 给系统一点时间把页面还回去，再测量
            result.AvailableAfterMb = SystemMonitor.AvailableMb();
            result.FreedMb = Math.Round(result.AvailableAfterMb - result.AvailableBeforeMb, 1);
            result.Lines.Add("可用内存：" + result.AvailableBeforeMb.ToString("0") + " MB → " +
                             result.AvailableAfterMb.ToString("0") + " MB（变化 " +
                             (result.FreedMb >= 0 ? "+" : "") + result.FreedMb.ToString("0.0") + " MB）");
            return result;
        }
    }
}
