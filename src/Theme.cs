// 主题：Windows 11 Fluent 设计令牌（跟随系统深浅色 + 系统强调色）。
//
// 与 v1 的"深色 Web 仪表盘"配色彻底分开：这里用的是 Win11 真实观感的一组值 ——
//   · 基底：Mica 背景（DWM 提供），窗口底色取 Mica 的等效实色作兜底
//   · 卡片：rgba(255,255,255,0.05) 叠加 + 1px 描边（不是硬边框），圆角 8
//   · 文字：Primary / Secondary / Tertiary / Disabled 四级
//   · 强调色：优先系统强调色，拿不到就退回 Win11 默认蓝（深色下是浅蓝 #4CC2FF，按钮上用黑字）
//   · 字体：Segoe UI Variable（Win11 系统字体）+ Segoe Fluent Icons（图标字体，不再手画 GDI+ 图标）
//
// 深浅色**跟随系统**（HKCU\...\Personalize\AppsUseLightTheme），运行期可切换。

using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Windows.Forms;
using Microsoft.Win32;

namespace Hz
{
    internal enum FluentTheme
    {
        Dark = 0,
        Light = 1
    }

    internal static class Theme
    {
        public static FluentTheme Mode { get; private set; }

        // ── 颜色令牌（由 Apply 填充）────────────────────────────
        public static Color Base;          // 窗口基底（Mica 兜底色）
        public static Color Layer;         // 卡片
        public static Color LayerHover;    // 卡片/控件悬停
        public static Color LayerAlt;      // 次级控件底（按钮、输入框）
        public static Color CardBorder;    // 卡片描边
        public static Color ControlBorder; // 控件描边
        public static Color Divider;       // 分隔线
        public static Color TextPrimary;
        public static Color TextSecondary;
        public static Color TextTertiary;
        public static Color TextDisabled;
        public static Color Accent;
        public static Color AccentHover;
        public static Color AccentText;    // 强调色底上的文字色
        public static Color Danger;
        public static Color Success;
        public static Color Warning;
        public static Color SubtleFill;        // rgba(255,255,255,0.06)
        public static Color SubtleFillHover;   // rgba(255,255,255,0.09)
        public static Color SubtleFillPress;   // rgba(255,255,255,0.04)
        public static Color MicaTint;          // Mica 不可用时的实色

        // ── 字体（Win11 字号阶梯：px ≈ pt * 96/72）──────────────
        public static Font Caption;      // 12px
        public static Font Body;         // 14px
        public static Font BodyStrong;   // 14px 半粗
        public static Font Subtitle;     // 20px 半粗
        public static Font Title;        // 28px 半粗
        public static Font Display;      // 44px 半粗（倒计时）
        public static Font Mono;         // Cascadia Mono 13px
        public static Font MonoSmall;    // Cascadia Mono 12px
        // 注意：图标字体字段名不能叫 Icon —— 会遮蔽 System.Drawing.Icon，导致 Icon.FromHandle 编译不过
        public static Font IconFont;     // Segoe Fluent Icons 16px
        public static Font IconSmallFont;// 14px
        public static Font IconLargeFont;// 20px

        public static string UiFamily = "Segoe UI Variable Text";
        public static string DisplayFamily = "Segoe UI Variable Display";
        public static string IconFamily = "Segoe Fluent Icons";
        public static string MonoFamily = "Cascadia Mono";

        /// 图标字形（Segoe Fluent Icons 码位）。
        public static class Ico
        {
            public const string Bolt = "\uE945";        // 加速
            public const string Power = "\uE7E8";       // 定时电源
            public const string Clock = "\uE823";       // 定时关闭
            public const string Settings = "\uE713";    // 设置
            public const string Refresh = "\uE72C";
            public const string Delete = "\uE74D";
            public const string Folder = "\uE8B7";
            public const string Info = "\uE946";
            public const string Check = "\uE73E";
            public const string Close = "\uE711";
            public const string ChevronDown = "\uE70D";
            public const string ChevronRight = "\uE76C";
            public const string Search = "\uE721";
            public const string Lock = "\uE72E";        // 白名单
            public const string Warning = "\uE7BA";
            public const string Minimize = "\uE921";
            public const string Maximize = "\uE922";
            public const string Restore = "\uE923";
            public const string CaptionClose = "\uE8BB";
            public const string Copy = "\uE8C8";
            public const string Stop = "\uE71A";        // 重置
            public const string Play = "\uE768";
            public const string Pause = "\uE769";
            public const string History = "\uE81C";
        }

        static Theme()
        {
            Apply(DetectSystemTheme());
        }

        /// 读系统设置：AppsUseLightTheme = 1 → 浅色。
        public static FluentTheme DetectSystemTheme()
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(
                    @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize"))
                {
                    if (key != null)
                    {
                        object v = key.GetValue("AppsUseLightTheme");
                        if (v != null && Convert.ToInt32(v) == 1) return FluentTheme.Light;
                    }
                }
            }
            catch { }
            return FluentTheme.Dark;
        }

        public static void Apply(FluentTheme mode)
        {
            Mode = mode;
            bool dark = mode == FluentTheme.Dark;

            if (dark)
            {
                Base = Color.FromArgb(0x20, 0x20, 0x20);
                MicaTint = Color.FromArgb(0x20, 0x20, 0x20);
                Layer = Color.FromArgb(0x2B, 0x2B, 0x2B);
                LayerHover = Color.FromArgb(0x32, 0x32, 0x32);
                LayerAlt = Color.FromArgb(0x2D, 0x2D, 0x2D);
                CardBorder = Color.FromArgb(0x38, 0x38, 0x38);
                ControlBorder = Color.FromArgb(0x44, 0x44, 0x44);
                Divider = Color.FromArgb(0x30, 0x30, 0x30);
                TextPrimary = Color.FromArgb(0xFF, 0xFF, 0xFF);
                TextSecondary = Color.FromArgb(0xC8, 0xC8, 0xC8);
                TextTertiary = Color.FromArgb(0x8B, 0x8B, 0x8B);
                TextDisabled = Color.FromArgb(0x5C, 0x5C, 0x5C);
                Accent = SystemAccent(Color.FromArgb(0x4C, 0xC2, 0xFF));
                AccentHover = Lighten(Accent, 0.12f);
                AccentText = Color.FromArgb(0x00, 0x00, 0x00);   // 深色下强调色按钮用黑字（Win11 行为）
                Danger = Color.FromArgb(0xFF, 0x99, 0xA4);
                Success = Color.FromArgb(0x6C, 0xCB, 0x5F);
                Warning = Color.FromArgb(0xFC, 0xE1, 0x00);
                SubtleFill = Color.FromArgb(0x0F, 0xFF, 0xFF, 0xFF);
                SubtleFillHover = Color.FromArgb(0x18, 0xFF, 0xFF, 0xFF);
                SubtleFillPress = Color.FromArgb(0x0A, 0xFF, 0xFF, 0xFF);
            }
            else
            {
                Base = Color.FromArgb(0xF3, 0xF3, 0xF3);
                MicaTint = Color.FromArgb(0xF3, 0xF3, 0xF3);
                Layer = Color.FromArgb(0xFF, 0xFF, 0xFF);
                LayerHover = Color.FromArgb(0xF7, 0xF7, 0xF7);
                LayerAlt = Color.FromArgb(0xFB, 0xFB, 0xFB);
                CardBorder = Color.FromArgb(0xE5, 0xE5, 0xE5);
                ControlBorder = Color.FromArgb(0xD6, 0xD6, 0xD6);
                Divider = Color.FromArgb(0xE8, 0xE8, 0xE8);
                TextPrimary = Color.FromArgb(0x1B, 0x1B, 0x1B);
                TextSecondary = Color.FromArgb(0x5C, 0x5C, 0x5C);
                TextTertiary = Color.FromArgb(0x8A, 0x8A, 0x8A);
                TextDisabled = Color.FromArgb(0xA8, 0xA8, 0xA8);
                Accent = SystemAccent(Color.FromArgb(0x00, 0x67, 0xC0));
                AccentHover = Darken(Accent, 0.08f);
                AccentText = Color.FromArgb(0xFF, 0xFF, 0xFF);
                Danger = Color.FromArgb(0xC4, 0x2B, 0x1C);
                Success = Color.FromArgb(0x0F, 0x7B, 0x0F);
                Warning = Color.FromArgb(0x9D, 0x5D, 0x00);
                SubtleFill = Color.FromArgb(0x0A, 0x00, 0x00, 0x00);
                SubtleFillHover = Color.FromArgb(0x14, 0x00, 0x00, 0x00);
                SubtleFillPress = Color.FromArgb(0x06, 0x00, 0x00, 0x00);
            }

            BuildFonts();
        }

        private static void BuildFonts()
        {
            // 字体只在首次构建：深浅色切换不换字体，避免"控件还握着已释放的 Font"
            if (Body != null) return;
            string ui = PickFamily(UiFamily, "Segoe UI");
            string display = PickFamily(DisplayFamily, ui);
            string icon = PickFamily(IconFamily, "Segoe MDL2 Assets");
            string mono = PickFamily(MonoFamily, "Consolas");

            Caption = new Font(ui, 9f);
            Body = new Font(ui, 10.5f);
            BodyStrong = new Font(ui, 10.5f, FontStyle.Bold);
            Subtitle = new Font(display, 15f, FontStyle.Bold);
            Title = new Font(display, 21f, FontStyle.Bold);
            Display = new Font(display, 32f, FontStyle.Bold);
            Mono = new Font(mono, 10f);
            MonoSmall = new Font(mono, 9f);
            IconFont = new Font(icon, 12f);
            IconSmallFont = new Font(icon, 10.5f);
            IconLargeFont = new Font(icon, 15f);
        }

        private static string PickFamily(string preferred, string fallback)
        {
            try
            {
                using (var installed = new InstalledFontCollection())
                {
                    foreach (FontFamily fam in installed.Families)
                    {
                        if (string.Equals(fam.Name, preferred, StringComparison.OrdinalIgnoreCase)) return preferred;
                    }
                }
            }
            catch { }
            return fallback;
        }

        /// 系统强调色：AccentColor（ABGR）→ DWM 颜色化色 → 默认值。
        private static Color SystemAccent(Color fallback)
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\DWM"))
                {
                    if (key != null)
                    {
                        object raw = key.GetValue("AccentColor");
                        if (raw != null)
                        {
                            int abgr = unchecked((int)Convert.ToInt64(raw));
                            int a = (abgr >> 24) & 0xFF;
                            int b = (abgr >> 16) & 0xFF;
                            int g = (abgr >> 8) & 0xFF;
                            int r = abgr & 0xFF;
                            if (a > 0 && (r + g + b) > 30)
                            {
                                var c = Color.FromArgb(r, g, b);
                                return Mode == FluentTheme.Dark ? Lighten(c, 0.35f) : c;
                            }
                        }
                    }
                }
            }
            catch { }

            try
            {
                uint colorization;
                bool opaque;
                if (Native.DwmGetColorizationColor(out colorization, out opaque) == 0)
                {
                    int r = (int)((colorization >> 16) & 0xFF);
                    int g = (int)((colorization >> 8) & 0xFF);
                    int b = (int)(colorization & 0xFF);
                    if ((r + g + b) > 60)
                    {
                        var c = Color.FromArgb(r, g, b);
                        return Mode == FluentTheme.Dark ? Lighten(c, 0.30f) : c;
                    }
                }
            }
            catch { }
            return fallback;
        }

        public static Color Alpha(Color c, int a)
        {
            return Color.FromArgb(Math.Max(0, Math.Min(255, a)), c.R, c.G, c.B);
        }

        public static Color Lighten(Color c, float amount)
        {
            return Color.FromArgb(c.A,
                (int)Math.Min(255, c.R + (255 - c.R) * amount),
                (int)Math.Min(255, c.G + (255 - c.G) * amount),
                (int)Math.Min(255, c.B + (255 - c.B) * amount));
        }

        public static Color Darken(Color c, float amount)
        {
            return Color.FromArgb(c.A,
                (int)Math.Max(0, c.R * (1f - amount)),
                (int)Math.Max(0, c.G * (1f - amount)),
                (int)Math.Max(0, c.B * (1f - amount)));
        }

        /// 圆角矩形路径（Win11：控件 4、卡片 8）。
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

        /// 应用/托盘图标：Win11 圆角方块 + 强调色「H」。
        public static Icon AppIcon(int size)
        {
            if (size < 8) size = 8;
            var bmp = new Bitmap(size, size);
            using (var g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.Clear(Color.Transparent);
                float pad = size * 0.07f;
                var box = new RectangleF(pad, pad, size - pad * 2f, size - pad * 2f);
                using (var path = Path(box, size * 0.22f))
                using (var b = new SolidBrush(Color.FromArgb(0x2B, 0x2B, 0x2B)))
                    g.FillPath(b, path);
                using (var path = Path(box, size * 0.22f))
                using (var p = new Pen(Accent, Math.Max(1f, size * 0.07f)))
                    g.DrawPath(p, path);

                float w = size * 0.085f;
                float left = size * 0.305f, right = size * 0.61f, top = size * 0.30f, bot = size * 0.70f;
                using (var b = new SolidBrush(Accent))
                {
                    g.FillRectangle(b, left, top, w, bot - top);
                    g.FillRectangle(b, right, top, w, bot - top);
                    g.FillRectangle(b, left, size * 0.4575f, (right - left) + w, w);
                }
            }
            return FromBitmap(bmp);
        }

        /// 托盘状态点（右下角小圆点）。
        public static Icon AppIconWithDot(int size, Color dot)
        {
            if (size < 10) size = 10;
            var bmp = new Bitmap(size, size);
            using (var g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.Clear(Color.Transparent);
                using (Icon baseIcon = AppIcon(size))
                {
                    g.DrawIcon(baseIcon, new Rectangle(0, 0, size, size));
                }
                float d = size * 0.46f;
                var dotBox = new RectangleF(size - d - size * 0.02f, size - d - size * 0.02f, d, d);
                using (var b = new SolidBrush(Color.FromArgb(230, Base)))
                    g.FillEllipse(b, dotBox);
                using (var b = new SolidBrush(dot))
                    g.FillEllipse(b, new RectangleF(dotBox.X + d * 0.18f, dotBox.Y + d * 0.18f, d * 0.64f, d * 0.64f));
            }
            return FromBitmap(bmp);
        }

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
