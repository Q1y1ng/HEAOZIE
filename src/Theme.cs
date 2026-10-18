// 主题：深色液态玻璃调色板 + 字体 + 运行时绘制的图标（零资源文件）。
//
// 视觉语言借自 launch-center 的设计系统 v3（同机、同作者、已验收），但代码自持：
// 不跨仓库引用 LC 的文件，避免两边的令牌体系互相漂移。
// 三件事：三层底色（底/面板/卡片）、胶囊状态、GDI+ 矢量图标。

using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace Hz
{
    internal static class Theme
    {
        // ── 三层底色 ────────────────────────────────────────────
        public static readonly Color Bg = Color.FromArgb(0x0B, 0x0F, 0x14);
        public static readonly Color Surface = Color.FromArgb(0x11, 0x17, 0x1F);
        public static readonly Color Card = Color.FromArgb(0x16, 0x1D, 0x26);
        public static readonly Color CardHover = Color.FromArgb(0x1D, 0x26, 0x32);
        public static readonly Color CardBorder = Color.FromArgb(0x25, 0x2F, 0x3B);

        // ── 文字 ────────────────────────────────────────────────
        public static readonly Color Text = Color.FromArgb(0xE6, 0xED, 0xF3);
        public static readonly Color TextDim = Color.FromArgb(0x93, 0x9E, 0xAB);
        public static readonly Color TextFaint = Color.FromArgb(0x5D, 0x68, 0x75);

        // ── 语义色 ──────────────────────────────────────────────
        public static readonly Color Accent = Color.FromArgb(0xE9, 0x45, 0x60);
        public static readonly Color AccentHover = Color.FromArgb(0xFF, 0x6B, 0x81);
        public static readonly Color Ok = Color.FromArgb(0x2E, 0xA0, 0x43);
        public static readonly Color OkHover = Color.FromArgb(0x3F, 0xBF, 0x5A);
        public static readonly Color Warn = Color.FromArgb(0xFF, 0xC1, 0x07);
        public static readonly Color Info = Color.FromArgb(0x3B, 0x82, 0xF6);
        public static readonly Color Danger = Color.FromArgb(0xDA, 0x36, 0x33);

        // ── 字体 ────────────────────────────────────────────────
        public static readonly Font FontUi = new Font("Microsoft YaHei UI", 9.5f);
        public static readonly Font FontUiBold = new Font("Microsoft YaHei UI", 9.5f, FontStyle.Bold);
        public static readonly Font FontSmall = new Font("Microsoft YaHei UI", 8.5f);
        public static readonly Font FontTitle = new Font("Microsoft YaHei UI", 13f, FontStyle.Bold);
        public static readonly Font FontMono = new Font("Consolas", 9.5f);
        public static readonly Font FontMonoBig = new Font("Consolas", 11f, FontStyle.Bold);
        public static readonly Font FontDigit = new Font("Consolas", 40f, FontStyle.Bold);

        public static Color Alpha(Color c, int a)
        {
            return Color.FromArgb(a, c.R, c.G, c.B);
        }

        /// 圆角矩形路径（半径自动收敛，半径大于半高时不会画成畸形）。
        public static GraphicsPath Path(RectangleF r, float radius)
        {
            var p = new GraphicsPath();
            float max = Math.Min(r.Width, r.Height) / 2f;
            if (radius > max) radius = max;
            if (radius <= 0.5f) { p.AddRectangle(r); return p; }
            float d = radius * 2f;
            p.AddArc(r.X, r.Y, d, d, 180f, 90f);
            p.AddArc(r.Right - d, r.Y, d, d, 270f, 90f);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0f, 90f);
            p.AddArc(r.X, r.Bottom - d, d, d, 90f, 90f);
            p.CloseFigure();
            return p;
        }

        /// 应用图标：圆角方块 + 中央「H」横杠（运行时画，不需要 .ico 文件）。
        public static Icon AppIcon(int size)
        {
            if (size < 8) size = 8;
            var bmp = new Bitmap(size, size);
            using (var g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.Clear(Color.Transparent);

                float pad = size * 0.06f;
                var box = new RectangleF(pad, pad, size - pad * 2f, size - pad * 2f);
                float radius = size * 0.24f;
                using (var path = Path(box, radius))
                using (var b = new SolidBrush(Surface))
                    g.FillPath(b, path);
                using (var path = Path(box, radius))
                using (var p = new Pen(Accent, Math.Max(1f, size * 0.075f)))
                    g.DrawPath(p, path);

                // 中央「H」：两竖一横，小尺寸下也认得出
                float w = size * 0.085f;
                float left = size * 0.305f;
                float right = size * 0.61f;
                float top = size * 0.30f;
                float bot = size * 0.70f;
                using (var b = new SolidBrush(Accent))
                {
                    g.FillRectangle(b, left, top, w, bot - top);
                    g.FillRectangle(b, right, top, w, bot - top);
                    g.FillRectangle(b, left, size * 0.4575f, (right - left) + w, w);
                }
            }
            return FromBitmap(bmp);
        }

        /// 托盘用的纯色圆点（LC 同款做法：状态色直接画，不预置图片资源）。
        public static Icon DotIcon(Color color, int size)
        {
            if (size < 8) size = 8;
            var bmp = new Bitmap(size, size);
            using (var g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.Clear(Color.Transparent);
                float pad = size * 0.14f;
                using (var b = new SolidBrush(color))
                    g.FillEllipse(b, pad, pad, size - pad * 2f, size - pad * 2f);
            }
            return FromBitmap(bmp);
        }

        /// 应用图标 + 右下角状态点（托盘用：既认得出是 HZ，又能看出状态）。
        public static Icon AppIconWithDot(int size, Color dot)
        {
            if (size < 10) size = 10;
            var bmp = new Bitmap(size, size);
            using (var g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.Clear(Color.Transparent);

                float pad = size * 0.06f;
                var box = new RectangleF(pad, pad, size - pad * 2f, size - pad * 2f);
                float radius = size * 0.24f;
                using (var path = Path(box, radius))
                using (var b = new SolidBrush(Surface))
                    g.FillPath(b, path);
                using (var path = Path(box, radius))
                using (var p = new Pen(Accent, Math.Max(1f, size * 0.075f)))
                    g.DrawPath(p, path);

                float w = size * 0.085f;
                float left = size * 0.305f;
                float right = size * 0.61f;
                float top = size * 0.30f;
                float bot = size * 0.70f;
                using (var b = new SolidBrush(Accent))
                {
                    g.FillRectangle(b, left, top, w, bot - top);
                    g.FillRectangle(b, right, top, w, bot - top);
                    g.FillRectangle(b, left, size * 0.4575f, (right - left) + w, w);
                }

                // 右下角状态点：白边 + 状态色，小尺寸下也分得清
                float d = size * 0.46f;
                var dotBox = new RectangleF(size - d - size * 0.02f, size - d - size * 0.02f, d, d);
                using (var b = new SolidBrush(Color.FromArgb(230, Bg)))
                    g.FillEllipse(b, dotBox);
                var inner = new RectangleF(dotBox.X + d * 0.18f, dotBox.Y + d * 0.18f, d * 0.64f, d * 0.64f);
                using (var b = new SolidBrush(dot))
                    g.FillEllipse(b, inner);
            }
            return FromBitmap(bmp);
        }

        private static Icon FromBitmap(Bitmap bmp)
        {
            IntPtr h = bmp.GetHicon();
            try
            {
                using (var tmp = Icon.FromHandle(h))
                    return (Icon)tmp.Clone();
            }
            finally
            {
                Native.DestroyIcon(h);
                bmp.Dispose();
            }
        }
    }
}
