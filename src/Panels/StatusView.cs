// 系统状态视图（Fluent 版）：标签 + 数值 + 4px 进度条，全部 GDI+ 绘制（Mica 下 alpha 才正确）。

using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using Hz.Sys;

namespace Hz.Panels
{
    internal sealed class StatusView : Control
    {
        private const int RowHeight = 33;
        private SystemSnapshot _snap;

        public StatusView()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer
                     | ControlStyles.UserPaint | ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor, true);
            SetStyle(ControlStyles.SupportsTransparentBackColor, true);
            BackColor = Color.Transparent;
        }

        public void Update(SystemSnapshot snapshot)
        {
            _snap = snapshot;
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;

            if (_snap == null)
            {
                Fluent.Text(g, "正在采集…", Theme.Body, new PointF(0, 6), Theme.TextTertiary);
                return;
            }

            int capacity = Math.Max(3, (Height - 6) / RowHeight);
            int y = 0;
            Row(g, ref y, "CPU 占用", _snap.CpuPercent.ToString("0") + " %", _snap.CpuPercent / 100.0, Fluent.MeterColor(_snap.CpuPercent));
            Row(g, ref y, "内存", Fluent.FormatMb(_snap.MemUsedMb) + " / " + Fluent.FormatMb(_snap.MemTotalMb) +
                             "（" + _snap.MemPercent.ToString("0") + "%）", _snap.MemPercent / 100.0, Fluent.MeterColor(_snap.MemPercent));

            GpuInfo gpu = _snap.Gpu;
            if (gpu != null && gpu.Available)
            {
                string text = gpu.UtilPercent.ToString("0") + " %" +
                              (gpu.MemTotalMb > 0 ? "   显存 " + Fluent.FormatMb(gpu.MemUsedMb) + " / " + Fluent.FormatMb(gpu.MemTotalMb) : "") +
                              (gpu.TempC > 0 ? "   " + gpu.TempC.ToString("0") + " ℃" : "");
                Row(g, ref y, "GPU " + Fluent.Shorten(gpu.Name, 14), text, gpu.UtilPercent / 100.0, Theme.Accent);
            }
            else
            {
                Row(g, ref y, "GPU", gpu == null || string.IsNullOrEmpty(gpu.Error) ? "不可用" : gpu.Error, 0, Theme.TextTertiary);
            }

            int slots = capacity - 4;
            if (_snap.Drives.Count > 0 && slots >= _snap.Drives.Count)
            {
                foreach (DriveInfoRow d in _snap.Drives)
                {
                    string label = "磁盘 " + d.Letter + (string.IsNullOrEmpty(d.Label) ? "" : "（" + Fluent.Shorten(d.Label, 8) + "）");
                    Row(g, ref y, label, "剩余 " + d.FreeGb.ToString("0.0") + " / " + d.TotalGb.ToString("0.0") + " GB",
                        d.UsedPercent / 100.0, Fluent.MeterColor(d.UsedPercent));
                }
            }
            else if (_snap.Drives.Count > 0 && slots >= 1)
            {
                double free = 0, total = 0, worst = 0;
                foreach (DriveInfoRow d in _snap.Drives)
                {
                    free += d.FreeGb;
                    total += d.TotalGb;
                    if (d.UsedPercent > worst) worst = d.UsedPercent;
                }
                Row(g, ref y, "磁盘 " + _snap.Drives.Count + " 个（合计）",
                    "剩余 " + free.ToString("0.0") + " / " + total.ToString("0.0") + " GB", worst / 100.0, Fluent.MeterColor(worst));
            }

            if (y + RowHeight <= Height + 4)
            {
                string tempText = _snap.CpuTempC.HasValue
                    ? _snap.CpuTempC.Value.ToString("0.0") + " ℃"
                    : ShortTempNote(_snap.CpuTempNote);
                Row(g, ref y, "CPU 温度", tempText, 0,
                    _snap.CpuTempC.HasValue ? Fluent.MeterColor(_snap.CpuTempC.Value * 1.4) : Theme.TextTertiary);
            }
        }

        /// 温度拿不到时给一句人话，而不是把 ManagementException 原文糊上去（会溢出卡片）。
        private static string ShortTempNote(string note)
        {
            if (string.IsNullOrEmpty(note)) return "不可用";
            if (note.StartsWith("读取失败", StringComparison.Ordinal)) return "不可用（需管理员权限）";
            if (note.StartsWith("主板未暴露", StringComparison.Ordinal)) return "不可用（主板无传感器）";
            return note;
        }

        private void Row(Graphics g, ref int y, string label, string value, double ratio, Color color)
        {
            Fluent.Text(g, label, Theme.Body, new RectangleF(0, y, Width * 0.52f, 22f), Theme.TextSecondary, ContentAlignment.MiddleLeft);
            Fluent.Text(g, value, Theme.Mono, new RectangleF(Width * 0.48f, y, Width * 0.52f, 22f), Theme.TextPrimary, ContentAlignment.MiddleRight);
            if (ratio > 0)
            {
                Fluent.Meter(g, new RectangleF(0f, y + 26f, Math.Max(60, Width), 4f), ratio, color);
            }
            y += RowHeight;
        }
    }
}
