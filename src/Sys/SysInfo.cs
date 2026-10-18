// 系统状态采集：CPU / 内存 / 磁盘 / GPU / CPU 温度。
//
// 与原 Python 版 booster_core.py 的差别：
//   · CPU 占用用 GetSystemTimes 差分（kernel32），不再依赖 psutil 的采样线程；
//   · GPU 仍走 nvidia-smi（原版同路，NVIDIA 卡上最省事且不引依赖）；
//   · CPU 温度走 WMI（MSAcpi_ThermalZoneTemperature），失败就明确显示"不可用/需管理员"，
//     而不是像原版那样静默返回 0 让人误以为 0℃；
//   · WMI 查询慢（几十毫秒），所以只在需要时刷新并缓存结果。

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Management;
using System.Runtime.InteropServices;

namespace Hz.Sys
{
    internal sealed class GpuInfo
    {
        public bool Available;
        public string Name = "";
        public double UtilPercent;
        public double MemUsedMb;
        public double MemTotalMb;
        public double TempC;
        public string Error = "";
    }

    internal sealed class DriveInfoRow
    {
        public string Letter = "";
        public string Label = "";
        public double TotalGb;
        public double FreeGb;
        public double UsedPercent;
    }

    internal sealed class SystemSnapshot
    {
        public double CpuPercent;
        public double MemUsedMb;
        public double MemTotalMb;
        public double MemPercent;
        public double AvailableMb;
        public double? CpuTempC;
        public string CpuTempNote = "";
        public GpuInfo Gpu = new GpuInfo();
        public readonly List<DriveInfoRow> Drives = new List<DriveInfoRow>();
        public DateTime At = DateTime.Now;
    }

    internal sealed class SystemMonitor
    {
        [StructLayout(LayoutKind.Sequential)]
        private struct FILETIME
        {
            public uint Low;
            public uint High;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct MEMORYSTATUSEX
        {
            public uint Length;
            public uint MemoryLoad;
            public ulong TotalPhys;
            public ulong AvailPhys;
            public ulong TotalPageFile;
            public ulong AvailPageFile;
            public ulong TotalVirtual;
            public ulong AvailVirtual;
            public ulong AvailExtendedVirtual;
        }

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool GetSystemTimes(out FILETIME idle, out FILETIME kernel, out FILETIME user);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX buffer);

        private ulong _prevIdle, _prevKernel, _prevUser;
        private bool _primed;

        private double? _tempCache;
        private string _tempNote = "";
        private DateTime _tempAt = DateTime.MinValue;
        private bool _tempPermanentFailure;
        private GpuInfo _gpuCache = new GpuInfo();

        public static double AvailableMb()
        {
            var m = new MEMORYSTATUSEX();
            m.Length = (uint)Marshal.SizeOf(typeof(MEMORYSTATUSEX));
            if (!GlobalMemoryStatusEx(ref m)) return 0;
            return m.AvailPhys / 1048576.0;
        }

        public SystemSnapshot Capture(bool withGpu, bool withTemp)
        {
            var snap = new SystemSnapshot();
            snap.At = DateTime.Now;

            // ── CPU（两次采样之间的差分）──
            FILETIME idle, kernel, user;
            if (GetSystemTimes(out idle, out kernel, out user))
            {
                ulong i = Combine(idle), k = Combine(kernel), u = Combine(user);
                if (_primed)
                {
                    ulong di = i - _prevIdle, dk = k - _prevKernel, du = u - _prevUser;
                    ulong total = dk + du;               // kernel 已包含 idle
                    if (total > 0)
                    {
                        double busy = total - Math.Min(di, total);
                        snap.CpuPercent = Math.Max(0, Math.Min(100, busy * 100.0 / total));
                    }
                }
                _prevIdle = i;
                _prevKernel = k;
                _prevUser = u;
                _primed = true;
            }

            // ── 内存 ──
            var mem = new MEMORYSTATUSEX();
            mem.Length = (uint)Marshal.SizeOf(typeof(MEMORYSTATUSEX));
            if (GlobalMemoryStatusEx(ref mem))
            {
                snap.MemTotalMb = mem.TotalPhys / 1048576.0;
                snap.AvailableMb = mem.AvailPhys / 1048576.0;
                snap.MemUsedMb = snap.MemTotalMb - snap.AvailableMb;
                snap.MemPercent = snap.MemTotalMb > 0 ? snap.MemUsedMb * 100.0 / snap.MemTotalMb : 0;
            }

            // ── 磁盘 ──
            try
            {
                foreach (DriveInfo d in DriveInfo.GetDrives())
                {
                    try
                    {
                        if (d.DriveType != DriveType.Fixed || !d.IsReady) continue;
                        var row = new DriveInfoRow
                        {
                            Letter = d.Name.TrimEnd('\\'),
                            TotalGb = d.TotalSize / 1073741824.0,
                            FreeGb = d.TotalFreeSpace / 1073741824.0
                        };
                        row.UsedPercent = row.TotalGb > 0 ? (row.TotalGb - row.FreeGb) * 100.0 / row.TotalGb : 0;
                        try { row.Label = d.VolumeLabel ?? ""; } catch { }
                        snap.Drives.Add(row);
                    }
                    catch { }
                }
            }
            catch { }

            if (withGpu)
            {
                _gpuCache = QueryGpu();
                snap.Gpu = _gpuCache;
            }
            else
            {
                // 同上：nvidia-smi 隔轮才查，未查的那轮沿用上次结果，别让 GPU 行忽有忽无
                snap.Gpu = _gpuCache;
            }
            if (withTemp)
            {
                snap.CpuTempC = QueryCpuTemperature();
                snap.CpuTempNote = _tempNote;
            }
            else
            {
                // 未到查询轮次也要带上上次的值，否则面板会在「50 ℃」与「不可用」之间来回跳
                snap.CpuTempC = _tempCache;
                snap.CpuTempNote = _tempNote;
            }
            return snap;
        }

        private static ulong Combine(FILETIME ft)
        {
            return ((ulong)ft.High << 32) | ft.Low;
        }

        // ── GPU：nvidia-smi（与原版同路）──

        private static GpuInfo QueryGpu()
        {
            var gpu = new GpuInfo();
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "nvidia-smi",
                    Arguments = "--query-gpu=utilization.gpu,memory.used,memory.total,temperature.gpu,name --format=csv,noheader,nounits",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                };
                using (Process p = Process.Start(psi))
                {
                    if (p == null) { gpu.Error = "无法启动 nvidia-smi"; return gpu; }
                    string output = p.StandardOutput.ReadToEnd();
                    if (!p.WaitForExit(3000))
                    {
                        try { p.Kill(); } catch { }
                        gpu.Error = "nvidia-smi 超时";
                        return gpu;
                    }
                    if (p.ExitCode != 0 || string.IsNullOrEmpty(output))
                    {
                        gpu.Error = "nvidia-smi 返回空（可能不是 NVIDIA 显卡）";
                        return gpu;
                    }
                    string line = output.Replace("\r", "").Split('\n')[0];
                    string[] parts = line.Split(',');
                    if (parts.Length < 5) { gpu.Error = "nvidia-smi 输出格式意外"; return gpu; }
                    gpu.UtilPercent = Parse(parts[0]);
                    gpu.MemUsedMb = Parse(parts[1]);
                    gpu.MemTotalMb = Parse(parts[2]);
                    gpu.TempC = Parse(parts[3]);
                    gpu.Name = parts[4].Trim();
                    gpu.Available = true;
                }
            }
            catch (Exception ex)
            {
                gpu.Error = ex.GetType().Name + ": " + ex.Message;
            }
            return gpu;
        }

        private static double Parse(string text)
        {
            double v;
            if (double.TryParse(text.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out v)) return v;
            return 0;
        }

        // ── CPU 温度：WMI（慢，缓存 10 秒；失败后不再反复重试）──

        private static double? QueryCpuTemperatureInternal(out string note)
        {
            note = "";
            try
            {
                using (var searcher = new ManagementObjectSearcher(
                    @"root\WMI", "SELECT CurrentTemperature FROM MSAcpi_ThermalZoneTemperature"))
                {
                    foreach (ManagementBaseObject obj in searcher.Get())
                    {
                        object raw = obj["CurrentTemperature"];
                        if (raw == null) continue;
                        double deciKelvin = Convert.ToDouble(raw, CultureInfo.InvariantCulture);
                        double celsius = deciKelvin / 10.0 - 273.15;
                        if (celsius > -20 && celsius < 150) return celsius;
                    }
                }
                note = "主板未暴露温度传感器";
            }
            catch (Exception ex)
            {
                note = "读取失败（" + ex.GetType().Name + "，通常需要管理员权限）";
            }
            return null;
        }

        private double? QueryCpuTemperature()
        {
            if (_tempPermanentFailure) return _tempCache;
            if ((DateTime.Now - _tempAt).TotalSeconds < 10) return _tempCache;

            string note;
            double? value = QueryCpuTemperatureInternal(out note);
            _tempCache = value;
            _tempNote = value.HasValue ? "" : note;
            _tempAt = DateTime.Now;
            // 连续两次拿不到就不再重试（WMI 每次都要几十毫秒，白等）
            if (!value.HasValue && !string.IsNullOrEmpty(note) && note.StartsWith("读取失败", StringComparison.Ordinal))
            {
                _tempPermanentFailure = true;
            }
            return value;
        }
    }
}
