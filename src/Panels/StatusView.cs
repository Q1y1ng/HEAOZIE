// 状态面板：把一次 SystemSnapshot 画成「标签 + 数值 + 进度条」的几行。
//
// 用自绘而不是堆几十个 Label/ProgressBar：刷新是每秒级的，控件越多重排越贵，
// 而且深色主题下原生 ProgressBar 是亮色的，还得额外自绘 —— 不如整块自己画。

using System;
using System.Drawing;
using System.Windows.Forms;
using Hz.Sys;

namespace Hz.Panels
{
    internal sealed class StatusView : Control
    {
        private const int RowHeight = 34;
        private SystemSnapshot _snap;
        private string _extraNote = "";

        public StatusView()
        {
            BackColor = Theme.Card;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer
                     | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
        }

        public void Update(SystemSnapshot snapshot, string extraNote)
        {
            _snap = snapshot;
            _extraNote = extraNote ?? "";
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            g.Clear(BackColor);

            if (_snap == null)
            {
                TextRenderer.DrawText(g, "正在采集…", Theme.FontUi, new Point(0, 6), Theme.TextFaint, TextFormatFlags.NoPadding);
                return;
            }

            int capacity = Math.Max(3, (Height - 6) / RowHeight);
            int y = 0;
            Row(g, ref y, "CPU 占用", _snap.CpuPercent.ToString("0") + " %", _snap.CpuPercent / 100.0, ColorFor(_snap.CpuPercent));

            Row(g, ref y, "内存", _snap.MemUsedMb.ToString("0") + " / " + _snap.MemTotalMb.ToString("0") + " MB（" +
                             _snap.MemPercent.ToString("0") + "%）", _snap.MemPercent / 100.0, ColorFor(_snap.MemPercent));

            GpuInfo gpu = _snap.Gpu;
            if (gpu != null && gpu.Available)
            {
                string text = gpu.UtilPercent.ToString("0") + " %" +
                              (gpu.MemTotalMb > 0 ? "  显存 " + gpu.MemUsedMb.ToString("0") + "/" + gpu.MemTotalMb.ToString("0") + " MB" : "") +
                              (gpu.TempC > 0 ? "  " + gpu.TempC.ToString("0") + " ℃" : "");
                Row(g, ref y, "GPU " + Shorten(gpu.Name, 22), text, gpu.UtilPercent / 100.0, Theme.Info);
            }
            else
            {
                Row(g, ref y, "GPU", gpu == null || string.IsNullOrEmpty(gpu.Error) ? "不可用" : gpu.Error, 0, Theme.TextFaint);
            }

            // 磁盘：行数够就一块一块画，不够就合成一行汇总（卡片高度有限，不做无意义的裁剪）
            int slotsForDrives = capacity - 4;          // 已画 3 行，给温度留 1 行
            if (_snap.Drives.Count > 0 && slotsForDrives >= _snap.Drives.Count)
            {
                foreach (DriveInfoRow d in _snap.Drives)
                {
                    string label = "磁盘 " + d.Letter + (string.IsNullOrEmpty(d.Label) ? "" : "（" + Shorten(d.Label, 10) + "）");
                    string text = "剩余 " + d.FreeGb.ToString("0.0") + " / " + d.TotalGb.ToString("0.0") + " GB";
                    Row(g, ref y, label, text, d.UsedPercent / 100.0, ColorFor(d.UsedPercent));
                }
            }
            else if (_snap.Drives.Count > 0 && slotsForDrives >= 1)
            {
                double free = 0, total = 0, worst = 0;
                foreach (DriveInfoRow d in _snap.Drives)
                {
                    free += d.FreeGb;
                    total += d.TotalGb;
                    if (d.UsedPercent > worst) worst = d.UsedPercent;
                }
                Row(g, ref y, "磁盘 " + _snap.Drives.Count + " 个（合计）",
                    "剩余 " + free.ToString("0.0") + " / " + total.ToString("0.0") + " GB", worst / 100.0, ColorFor(worst));
            }

            if (y + RowHeight <= Height + 4)
            {
                string tempText;
                if (_snap.CpuTempC.HasValue) tempText = _snap.CpuTempC.Value.ToString("0.0") + " ℃";
                else tempText = string.IsNullOrEmpty(_snap.CpuTempNote) ? "不可用" : _snap.CpuTempNote;
                Row(g, ref y, "CPU 温度", tempText, 0, _snap.CpuTempC.HasValue ? ColorFor(_snap.CpuTempC.Value * 1.4) : Theme.TextFaint);
            }

            if (!string.IsNullOrEmpty(_extraNote))
            {
                TextRenderer.DrawText(g, _extraNote, Theme.FontSmall, new Point(0, y + 4), Theme.TextFaint, TextFormatFlags.NoPadding);
            }
        }

        private void Row(Graphics g, ref int y, string label, string value, double ratio, Color color)
        {
            TextRenderer.DrawText(g, label, Theme.FontUi, new Point(0, y + 2), Theme.TextDim, TextFormatFlags.NoPadding);
            Size vs = TextRenderer.MeasureText(value, Theme.FontMono);
            TextRenderer.DrawText(g, value, Theme.FontMono, new Point(Math.Max(150, Width - vs.Width), y + 2),
                Theme.Text, TextFormatFlags.NoPadding);
            if (ratio > 0)
            {
                var bar = new RectangleF(0f, y + RowHeight - 12f, Math.Max(60, Width), 5f);
                Soft.DrawMeter(g, bar, ratio, color);
            }
            y += RowHeight;
        }

        private static Color ColorFor(double percent)
        {
            if (percent >= 85) return Theme.Danger;
            if (percent >= 65) return Theme.Warn;
            return Theme.Ok;
        }

        private static string Shorten(string text, int max)
        {
            if (string.IsNullOrEmpty(text)) return "";
            return text.Length <= max ? text : text.Substring(0, max) + "…";
        }
    }
}
