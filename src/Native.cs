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

        [StructLayout(LayoutKind.Sequential)]
        public struct POINT
        {
            public int X;
            public int Y;
        }

        [DllImport("user32.dll")]
        public static extern bool GetCursorPos(out POINT point);

        [DllImport("user32.dll")]
        public static extern IntPtr WindowFromPoint(POINT point);

        [DllImport("user32.dll")]
        public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint pid);

        [DllImport("user32.dll")]
        private static extern short GetAsyncKeyState(int vKey);

        public const int VK_LBUTTON = 0x01;
        public const int VK_ESCAPE = 0x1B;

        /// 全局键状态（点选目标时判断「用户在外面点了左键」/「按了 ESC」）。
        public static bool KeyDown(int vk)
        {
            try { return (GetAsyncKeyState(vk) & 0x8000) != 0; }
            catch { return false; }
        }

        /// 光标下窗口所属进程（点选目标用；与原 Python 版 WindowFromPoint + GetWindowThreadProcessId 同路）。
        public static int ProcessIdUnderCursor()
        {
            try
            {
                POINT pt;
                if (!GetCursorPos(out pt)) return 0;
                IntPtr hwnd = WindowFromPoint(pt);
                if (hwnd == IntPtr.Zero) return 0;
                uint pid;
                GetWindowThreadProcessId(hwnd, out pid);
                return (int)pid;
            }
            catch { return 0; }
        }

        // ── DWM：Win11 的 Mica 背景 / 圆角 / 暗色标题栏 ──────────────

        [StructLayout(LayoutKind.Sequential)]
        public struct MARGINS
        {
            public int Left;
            public int Right;
            public int Top;
            public int Bottom;
        }

        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

        [DllImport("dwmapi.dll")]
        private static extern int DwmExtendFrameIntoClientArea(IntPtr hwnd, ref MARGINS margins);

        [DllImport("dwmapi.dll")]
        public static extern int DwmGetColorizationColor(out uint colorization, out bool opaqueBlend);

        private const int DwmwaUseImmersiveDarkMode = 20;
        private const int DwmwaWindowCornerPreference = 33;
        private const int DwmwaSystemBackdropType = 38;

        private const int DwmsbtAuto = 0;
        private const int DwmsbtNone = 1;
        private const int DwmsbtMainWindow = 2;     // Mica
        private const int DwmsbtTransientWindow = 3;
        private const int DwmsbtTabbedWindow = 4;

        /// Win11（build >= 22000）才支持 Mica / 圆角。
        public static bool IsWindows11
        {
            get { return Environment.OSVersion.Version.Major >= 10 && Environment.OSVersion.Version.Build >= 22000; }
        }

        /// 给窗口套上 Mica + 圆角 + 暗色标题栏；返回是否成功套上 Mica。
        public static bool ApplyMica(IntPtr hwnd, bool dark, bool extendFrame)
        {
            if (hwnd == IntPtr.Zero || !IsWindows11) return false;
            bool ok = false;
            try
            {
                int backdrop = DwmsbtMainWindow;
                ok = DwmSetWindowAttribute(hwnd, DwmwaSystemBackdropType, ref backdrop, sizeof(int)) == 0;

                int corner = 2;   // DWMWCP_ROUND
                DwmSetWindowAttribute(hwnd, DwmwaWindowCornerPreference, ref corner, sizeof(int));

                int darkFlag = dark ? 1 : 0;
                DwmSetWindowAttribute(hwnd, DwmwaUseImmersiveDarkMode, ref darkFlag, sizeof(int));

                if (extendFrame)
                {
                    var margins = new MARGINS { Left = -1, Right = -1, Top = -1, Bottom = -1 };
                    DwmExtendFrameIntoClientArea(hwnd, ref margins);
                }
            }
            catch { return false; }
            return ok;
        }

        /// 主题切换后重套一次（DWM 只认调用时刻的状态）。
        public static void RefreshBackdrop(IntPtr hwnd, bool dark)
        {
            if (hwnd == IntPtr.Zero || !IsWindows11) return;
            try
            {
                int darkFlag = dark ? 1 : 0;
                DwmSetWindowAttribute(hwnd, DwmwaUseImmersiveDarkMode, ref darkFlag, sizeof(int));
                int backdrop = DwmsbtMainWindow;
                DwmSetWindowAttribute(hwnd, DwmwaSystemBackdropType, ref backdrop, sizeof(int));
            }
            catch { }
        }

        // ── 无边框窗口的命中测试（自绘标题栏 + 原生缩放/贴边/Snap 布局）──

        public const int WM_NCHITTEST = 0x0084;
        public const int HTCLIENT = 1;
        public const int HTCAPTION = 2;
        public const int HTLEFT = 10;
        public const int HTRIGHT = 11;
        public const int HTTOP = 12;
        public const int HTTOPLEFT = 13;
        public const int HTTOPRIGHT = 14;
        public const int HTBOTTOM = 15;
        public const int HTBOTTOMLEFT = 16;
        public const int HTBOTTOMRIGHT = 17;
        public const int HTMINBUTTON = 8;
        public const int HTMAXBUTTON = 9;
        public const int HTCLOSE = 20;

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
