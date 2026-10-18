// Win32 互操作：单实例广播、工作集修剪、图标句柄释放。
//
// 单实例不用命名管道（沙箱/权限下容易碎），用最老也最稳的「注册窗口消息 + 广播」：
// 第二个实例 PostMessage(HWND_BROADCAST) 通知第一个实例把自己显示出来，然后退出。

using System;
using System.Runtime.InteropServices;

namespace Hz
{
    internal static class Native
    {
        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern uint RegisterWindowMessage(string lpString);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool PostMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll")]
        public static extern bool SetForegroundWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        public static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

        [DllImport("user32.dll")]
        public static extern bool DestroyIcon(IntPtr hIcon);

        [DllImport("psapi.dll")]
        private static extern bool EmptyWorkingSet(IntPtr hProcess);

        [DllImport("kernel32.dll")]
        private static extern IntPtr GetCurrentProcess();

        public static readonly IntPtr HWND_BROADCAST = new IntPtr(0xFFFF);

        /// 唤醒已运行实例的私有消息（值由系统分配，进程间一致）。
        public static readonly uint WM_HZ_ACTIVATE = RegisterWindowMessage("HZ_ACTIVATE_9E31C7A4");

        public const int SW_RESTORE = 9;
        public const int SW_SHOW = 5;

        /// 把当前进程的工作集还给系统（托盘化/空闲时调用，实测能显著压低常驻内存）。
        public static void TrimWorkingSet()
        {
            try { EmptyWorkingSet(GetCurrentProcess()); }
            catch { /* 修剪失败不影响功能 */ }
        }
    }
}
