// 环境探针：权限、系统版本、工作集。放在一处，避免各处重复 P/Invoke 与 WMI 调用。

using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace Hz
{
    internal static class SysEnv
    {
        [DllImport("shell32.dll", SetLastError = true)]
        private static extern IntPtr IsUserAnAdmin();

        public static bool IsAdmin()
        {
            try { return IsUserAnAdmin() != IntPtr.Zero; }
            catch { return false; }
        }

        public static string OsVersion()
        {
            try
            {
                using (var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion"))
                {
                    if (key != null)
                    {
                        object product = key.GetValue("ProductName");
                        object display = key.GetValue("DisplayVersion");
                        object build = key.GetValue("CurrentBuildNumber");
                        string name = product == null ? "Windows" : product.ToString();
                        string ver = display == null ? "" : " " + display;
                        string b = build == null ? "" : " (Build " + build + ")";
                        return name + ver + b;
                    }
                }
            }
            catch { }
            return Environment.OSVersion.VersionString;
        }

        public static double WorkingSetMb()
        {
            try
            {
                using (Process p = Process.GetCurrentProcess())
                    return p.WorkingSet64 / 1024.0 / 1024.0;
            }
            catch { return 0; }
        }

        public static string FrameworkVersion()
        {
            try
            {
                using (var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\NET Framework Setup\NDP\v4\Full"))
                {
                    if (key != null)
                    {
                        object release = key.GetValue("Release");
                        if (release != null) return ".NET Framework 4.x (Release " + release + ")";
                    }
                }
            }
            catch { }
            return ".NET Framework " + Environment.Version;
        }
    }
}
