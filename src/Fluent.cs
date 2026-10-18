// Fluent 控件库（Windows 11 观感）—— 取代 v1 的 SoftUi。
//
// 三条硬性决定，都是被 WinForms 的现实逼出来的：
//   1) **文字一律用 GDI+ DrawString**，不用 TextRenderer。原因：Mica/玻璃窗口要求客户区
//      带 alpha，而 GDI 画文字会把 alpha 写成 0 → 文字直接变透明（看不见）。GDI+ 会正确写 255。
//   2) **颜色带 alpha**：卡片是 rgba(255,255,255,0.05) 叠加而不是实色，这样同一套绘制代码
//      在「Mica 生效」与「退回实色底」两种情况下都对。
//   3) **控件自绘 + 4/8px 圆角**：Win11 的按钮、开关、导航项、进度条都不是系统控件的默认样子。
//
// 尺寸遵循 Win11 度量：控件高 32、卡片圆角 8、控件圆角 4、正文 14px、说明 12px。

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Windows.Forms;

namespace Hz
{
    internal enum FluentButtonStyle
    {
        Standard = 0,   // 次级底 + 描边
        Accent = 1,     // 强调色底
        Subtle = 2,     // 无底，悬停才出底色
        Danger = 3
    }

    internal static class Fluent
    {
        public const int ControlRadius = 4;
        public const int CardRadius = 8;
        public const int ControlHeight = 32;

        // ── 语义色（带 alpha，Mica 下也能正确叠）────────────────
        public static Color CardFill
        {
            get { return Theme.Mode == FluentTheme.Dark ? Color.FromArgb(0x16, 255, 255, 255) : Color.FromArgb(0xFF, 255, 255, 255); }
        }

        public static Color CardStroke
        {
            get { return Theme.Mode == FluentTheme.Dark ? Color.FromArgb(0x1C, 255, 255, 255) : Color.FromArgb(0xFF, 0xE5, 0xE5, 0xE5); }
        }

        public static Color ControlFill
        {
            get { return Theme.Mode == FluentTheme.Dark ? Color.FromArgb(0x18, 255, 255, 255) : Color.FromArgb(0xFF, 0xFD, 0xFD, 0xFD); }
        }

        public static Color ControlStroke
        {
            get { return Theme.Mode == FluentTheme.Dark ? Color.FromArgb(0x22, 255, 255, 255) : Color.FromArgb(0xFF, 0xD6, 0xD6, 0xD6); }
        }

        // ── 绘图助手 ────────────────────────────────────────────

        public static GraphicsPath Round(RectangleF r, float radius)
        {
            return Theme.Path(r, radius);
        }

        public static void Fill(Graphics g, RectangleF r, float radius, Color color)
        {
            if (color.A == 0) return;
            using (var path = Theme.Path(r, radius))
            using (var b = new SolidBrush(color))
                g.FillPath(b, path);
        }

        public static void Stroke(Graphics g, RectangleF r, float radius, Color color, float width = 1f)
        {
            if (color.A == 0 || width <= 0f) return;
            using (var path = Theme.Path(r, radius))
            using (var p = new Pen(color, width))
                g.DrawPath(p, path);
        }

        /// GDI+ 文字（alpha 正确，Mica 下不会消失）。
        public static void Text(Graphics g, string text, Font font, PointF at, Color color)
        {
            if (string.IsNullOrEmpty(text)) return;
            g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
            using (var b = new SolidBrush(color))
                g.DrawString(text, font, b, at);
        }

        public static void Text(Graphics g, string text, Font font, RectangleF box, Color color, ContentAlignment align)
        {
            if (string.IsNullOrEmpty(text)) return;
            g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
            using (var b = new SolidBrush(color))
            using (var sf = new StringFormat())
            {
                sf.Alignment = align == ContentAlignment.MiddleCenter || align == ContentAlignment.TopCenter || align == ContentAlignment.BottomCenter
                    ? StringAlignment.Center
                    : (align == ContentAlignment.TopRight || align == ContentAlignment.MiddleRight || align == ContentAlignment.BottomRight
                        ? StringAlignment.Far : StringAlignment.Near);
                sf.LineAlignment = align == ContentAlignment.MiddleLeft || align == ContentAlignment.MiddleCenter || align == ContentAlignment.MiddleRight
                    ? StringAlignment.Center
                    : (align == ContentAlignment.BottomLeft || align == ContentAlignment.BottomCenter || align == ContentAlignment.BottomRight
                        ? StringAlignment.Far : StringAlignment.Near);
                sf.FormatFlags = StringFormatFlags.NoWrap;
                g.DrawString(text, font, b, box, sf);
            }
        }

        public static SizeF Measure(Graphics g, string text, Font font)
        {
            if (string.IsNullOrEmpty(text)) return SizeF.Empty;
            g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
            return g.MeasureString(text, font);
        }

        /// Segoe Fluent Icons 图标字形。
        public static void Icon(Graphics g, string glyph, RectangleF box, Color color, Font font)
        {
            if (string.IsNullOrEmpty(glyph)) return;
            Text(g, glyph, font ?? Theme.IconFont, box, color, ContentAlignment.MiddleCenter);
        }

        /// Win11 进度条：4px 圆角轨道 + 强调色填充。
        public static void Meter(Graphics g, RectangleF r, double ratio, Color color)
        {
            if (ratio < 0) ratio = 0;
            if (ratio > 1) ratio = 1;
            Fill(g, r, r.Height / 2f, Theme.Mode == FluentTheme.Dark
                ? Color.FromArgb(0x24, 255, 255, 255)
                : Color.FromArgb(0x1A, 0, 0, 0));
            if (ratio <= 0.001) return;
            var fill = new RectangleF(r.X, r.Y, Math.Max(r.Height, (float)(r.Width * ratio)), r.Height);
            Fill(g, fill, r.Height / 2f, color);
        }

        public static Color MeterColor(double percent)
        {
            if (percent >= 85) return Theme.Danger;
            if (percent >= 65) return Theme.Warning;
            return Theme.Accent;
        }

        public static string Shorten(string text, int max)
        {
            if (string.IsNullOrEmpty(text)) return "";
            return text.Length <= max ? text : text.Substring(0, max) + "…";
        }

        public static string FormatMb(double mb)
        {
            return mb >= 1024 ? (mb / 1024.0).ToString("0.0") + " GB" : mb.ToString("0") + " MB";
        }
    }

    /// 卡片：8px 圆角、半透明底 + 1px 描边、标题 14px 半粗 + 说明 12px。
    internal class FluentCard : Panel
    {
        public string TitleText = "";
        public string DescriptionText = "";
        public int TitleAreaHeight = 56;

        public FluentCard(string title, string description)
        {
            TitleText = title ?? "";
            DescriptionText = description ?? "";
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer
                     | ControlStyles.UserPaint | ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor, true);
            SetStyle(ControlStyles.SupportsTransparentBackColor, true);
            SetStyle(ControlStyles.SupportsTransparentBackColor, true);
            BackColor = Color.Transparent;
            if (string.IsNullOrEmpty(TitleText)) TitleAreaHeight = 16;
            else if (string.IsNullOrEmpty(DescriptionText)) TitleAreaHeight = 44;
            Padding = new Padding(16, TitleAreaHeight, 16, 16);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            var box = new RectangleF(0.5f, 0.5f, Width - 1f, Height - 1f);
            Fluent.Fill(g, box, Fluent.CardRadius, Fluent.CardFill);
            Fluent.Stroke(g, box, Fluent.CardRadius, Fluent.CardStroke);

            if (!string.IsNullOrEmpty(TitleText))
                Fluent.Text(g, TitleText, Theme.BodyStrong, new PointF(16f, 13f), Theme.TextPrimary);
            if (!string.IsNullOrEmpty(DescriptionText))
                Fluent.Text(g, DescriptionText, Theme.Caption, new PointF(16f, 32f), Theme.TextTertiary);
        }
    }

    /// 按钮：Win11 标准/强调/次要/危险四种样式，可带图标，也可当"开关式"按钮用（Selected）。
    internal sealed class FluentButton : Control
    {
        public FluentButtonStyle Style = FluentButtonStyle.Standard;
        public string Glyph = "";
        public bool Selected;

        private bool _hover;
        private bool _press;

        public FluentButton(string text, int width, int height)
        {
            Text = text;
            Size = new Size(width, height);
            Font = Theme.Body;
            SetStyle(ControlStyles.SupportsTransparentBackColor, true);
            SetStyle(ControlStyles.SupportsTransparentBackColor, true);
            BackColor = Color.Transparent;
            Cursor = Cursors.Hand;
            TabStop = false;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer
                     | ControlStyles.UserPaint | ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor, true);
        }

        protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { _hover = false; _press = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnMouseDown(MouseEventArgs e) { _press = true; Invalidate(); base.OnMouseDown(e); }
        protected override void OnMouseUp(MouseEventArgs e) { _press = false; Invalidate(); base.OnMouseUp(e); }
        protected override void OnEnabledChanged(EventArgs e) { Invalidate(); base.OnEnabledChanged(e); }
        protected override void OnTextChanged(EventArgs e) { Invalidate(); base.OnTextChanged(e); }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            bool en = base.Enabled;

            Color fill, stroke, fg;
            switch (Style)
            {
                case FluentButtonStyle.Accent:
                    fill = _hover ? Theme.AccentHover : Theme.Accent;
                    if (_press) fill = Theme.Darken(Theme.Accent, 0.12f);
                    stroke = Color.Transparent;
                    fg = Theme.AccentText;
                    break;
                case FluentButtonStyle.Danger:
                    fill = _hover ? Theme.Alpha(Theme.Danger, 40) : Theme.Alpha(Theme.Danger, 22);
                    stroke = Theme.Alpha(Theme.Danger, 110);
                    fg = Theme.Danger;
                    break;
                case FluentButtonStyle.Subtle:
                    fill = _hover ? Fluent.ControlFill : Color.Transparent;
                    stroke = Color.Transparent;
                    fg = _hover ? Theme.TextPrimary : Theme.TextSecondary;
                    break;
                default:
                    fill = _hover ? Theme.LayerHover : Fluent.ControlFill;
                    if (_press) fill = Theme.SubtleFillPress;
                    stroke = Fluent.ControlStroke;
                    fg = Theme.TextPrimary;
                    break;
            }
            if (Selected && Style != FluentButtonStyle.Accent)
            {
                fill = Theme.Alpha(Theme.Accent, Theme.Mode == FluentTheme.Dark ? 46 : 30);
                stroke = Theme.Alpha(Theme.Accent, 150);
                fg = Theme.Mode == FluentTheme.Dark ? Theme.Accent : Theme.Accent;
            }
            if (!en)
            {
                fill = Theme.SubtleFill;
                stroke = Theme.Alpha(Fluent.ControlStroke, 90);
                fg = Theme.TextDisabled;
            }

            var box = new RectangleF(0.5f, 0.5f, Width - 1f, Height - 1f);
            Fluent.Fill(g, box, Fluent.ControlRadius, fill);
            Fluent.Stroke(g, box, Fluent.ControlRadius, stroke);

            float iconSide = Math.Min(Height - 12f, 16f);
            float gap = string.IsNullOrEmpty(Glyph) || string.IsNullOrEmpty(Text) ? 0f : 8f;
            SizeF ts = Fluent.Measure(g, Text, Font);
            float iconW = string.IsNullOrEmpty(Glyph) ? 0f : iconSide;
            float total = iconW + gap + (string.IsNullOrEmpty(Text) ? 0f : ts.Width);
            float x = (Width - total) / 2f;

            if (!string.IsNullOrEmpty(Glyph))
            {
                Fluent.Icon(g, Glyph, new RectangleF(x, (Height - iconSide) / 2f, iconSide, iconSide), fg, Theme.IconSmallFont);
                x += iconW + gap;
            }
            if (!string.IsNullOrEmpty(Text))
            {
                Fluent.Text(g, Text, Font, new RectangleF(x, 0, Width - x, Height), fg, ContentAlignment.MiddleLeft);
            }
        }
    }

    /// Win11 开关（ToggleSwitch）：40x20 胶囊轨道 + 12px 圆钮，左侧带文字。
    internal sealed class FluentToggleSwitch : Control
    {
        private bool _checked;
        private bool _hover;

        public event Action CheckedChanged;

        public FluentToggleSwitch(string text, int width)
        {
            Text = text;
            Size = new Size(width, 34);
            Font = Theme.Body;
            SetStyle(ControlStyles.SupportsTransparentBackColor, true);
            SetStyle(ControlStyles.SupportsTransparentBackColor, true);
            BackColor = Color.Transparent;
            Cursor = Cursors.Hand;
            TabStop = false;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer
                     | ControlStyles.UserPaint | ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor, true);
        }

        public bool Checked
        {
            get { return _checked; }
            set
            {
                if (_checked == value) return;
                _checked = value;
                Invalidate();
                if (CheckedChanged != null) CheckedChanged();
            }
        }

        protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { _hover = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnMouseUp(MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left) Checked = !Checked;
            base.OnMouseUp(e);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;

            float trackW = 40f, trackH = 20f;
            float tx = Width - trackW;
            float ty = (Height - trackH) / 2f;
            var track = new RectangleF(tx, ty, trackW, trackH);

            Color trackFill = _checked ? Theme.Accent : Theme.Alpha(Theme.TextTertiary, _hover ? 90 : 70);
            if (!_checked && _hover) trackFill = Theme.Alpha(Theme.TextSecondary, 110);
            Fluent.Fill(g, track, trackH / 2f, trackFill);
            if (!_checked) Fluent.Stroke(g, track, trackH / 2f, Fluent.ControlStroke);

            float knob = 12f;
            float kx = _checked ? tx + trackW - knob - 4f : tx + 4f;
            var knobBox = new RectangleF(kx, ty + (trackH - knob) / 2f, knob, knob);
            using (var b = new SolidBrush(_checked ? Theme.AccentText : Theme.TextPrimary))
                g.FillEllipse(b, knobBox);

            Color fg = base.Enabled ? Theme.TextPrimary : Theme.TextDisabled;
            Fluent.Text(g, Text, Font, new RectangleF(0, 0, tx - 10f, Height), fg, ContentAlignment.MiddleLeft);
        }
    }

    /// 左侧导航（NavigationView）：图标 + 文字，选中项 = 次级底 + 左侧强调色竖条。
    internal sealed class FluentNavView : Control
    {
        private sealed class Item
        {
            public string Glyph;
            public string Text;
            public string Badge;
        }

        private readonly List<Item> _items = new List<Item>();
        private int _selected;
        private int _hover = -1;

        public event Action<int> SelectedChanged;
        public string FooterText = "";
        public string HeaderText = "HEAOZIE";
        public string HeaderSubText = "";

        private const int ItemHeight = 40;
        private const int ItemGap = 4;
        private const int TopPad = 8;

        public FluentNavView()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer
                     | ControlStyles.UserPaint | ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor, true);
            SetStyle(ControlStyles.SupportsTransparentBackColor, true);
            SetStyle(ControlStyles.SupportsTransparentBackColor, true);
            BackColor = Color.Transparent;
        }

        public int SelectedIndex
        {
            get { return _selected; }
            set
            {
                if (value < 0 || value >= _items.Count || value == _selected) return;
                _selected = value;
                Invalidate();
                if (SelectedChanged != null) SelectedChanged(_selected);
            }
        }

        public void AddItem(string glyph, string text, string badge)
        {
            _items.Add(new Item { Glyph = glyph, Text = text, Badge = badge });
            Invalidate();
        }

        public void SetBadge(int index, string badge)
        {
            if (index < 0 || index >= _items.Count) return;
            _items[index].Badge = badge;
            Invalidate();
        }

        private int HeaderHeight
        {
            get { return string.IsNullOrEmpty(HeaderSubText) ? 52 : 66; }
        }

        private Rectangle ItemRect(int index)
        {
            return new Rectangle(8, HeaderHeight + TopPad + index * (ItemHeight + ItemGap), Width - 16, ItemHeight);
        }

        private int HitTest(Point p)
        {
            for (int i = 0; i < _items.Count; i++)
            {
                if (ItemRect(i).Contains(p)) return i;
            }
            return -1;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            int h = HitTest(e.Location);
            if (h != _hover) { _hover = h; Cursor = h >= 0 ? Cursors.Hand : Cursors.Default; Invalidate(); }
            base.OnMouseMove(e);
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            if (_hover != -1) { _hover = -1; Invalidate(); }
            base.OnMouseLeave(e);
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            int h = HitTest(e.Location);
            if (h >= 0 && e.Button == MouseButtons.Left) SelectedIndex = h;
            base.OnMouseUp(e);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;

            // 顶部应用标识（Win11 应用把名字放在导航栏顶端）
            Fluent.Text(g, HeaderText, Theme.BodyStrong, new PointF(18f, 16f), Theme.TextPrimary);
            if (!string.IsNullOrEmpty(HeaderSubText))
                Fluent.Text(g, HeaderSubText, Theme.Caption, new PointF(18f, 36f), Theme.TextTertiary);

            for (int i = 0; i < _items.Count; i++)
            {
                Rectangle r = ItemRect(i);
                bool sel = i == _selected;
                if (sel)
                {
                    Fluent.Fill(g, r, Fluent.ControlRadius, Theme.SubtleFillHover);
                    var bar = new RectangleF(2f, r.Y + 10f, 3f, r.Height - 20f);
                    Fluent.Fill(g, bar, 1.5f, Theme.Accent);
                }
                else if (i == _hover)
                {
                    Fluent.Fill(g, r, Fluent.ControlRadius, Theme.SubtleFill);
                }

                Color fg = sel ? Theme.TextPrimary : Theme.TextSecondary;
                Fluent.Icon(g, _items[i].Glyph, new RectangleF(r.X + 12f, r.Y + (r.Height - 16f) / 2f, 16f, 16f), fg, Theme.IconFont);
                Fluent.Text(g, _items[i].Text, Theme.Body, new RectangleF(r.X + 38f, r.Y, r.Width - 46f, r.Height), fg, ContentAlignment.MiddleLeft);

                if (!string.IsNullOrEmpty(_items[i].Badge))
                {
                    SizeF bs = Fluent.Measure(g, _items[i].Badge, Theme.Caption);
                    var pill = new RectangleF(r.Right - bs.Width - 18f, r.Y + (r.Height - 18f) / 2f, bs.Width + 12f, 18f);
                    Fluent.Fill(g, pill, 9f, Theme.Alpha(Theme.Accent, Theme.Mode == FluentTheme.Dark ? 60 : 40));
                    Fluent.Text(g, _items[i].Badge, Theme.Caption, pill, Theme.Accent, ContentAlignment.MiddleCenter);
                }
            }

            if (!string.IsNullOrEmpty(FooterText))
            {
                Fluent.Text(g, FooterText, Theme.Caption, new RectangleF(18f, Height - 34f, Width - 36f, 20f),
                    Theme.TextTertiary, ContentAlignment.MiddleLeft);
            }
        }
    }

    /// 自绘文字（替代 Label）。
    /// 为什么不能用 Label：WinForms 的 Label 走 GDI（TextRenderer），在 Mica/透明背景上
    /// 会把文字像素的 alpha 写成 0 → 文字看不见。这里用 GDI+ 画，alpha 正确。
    internal sealed class FluentText : Control
    {
        public string TextValue = "";
        public Font TextFont;
        public Color TextColor;
        public bool Wrap = true;
        public ContentAlignment Align = ContentAlignment.MiddleLeft;

        public FluentText(string text, Font font, Color color)
        {
            TextValue = text ?? "";
            TextFont = font ?? Theme.Body;
            TextColor = color;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer
                     | ControlStyles.UserPaint | ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor, true);
            SetStyle(ControlStyles.SupportsTransparentBackColor, true);
            SetStyle(ControlStyles.SupportsTransparentBackColor, true);
            BackColor = Color.Transparent;
        }

        public void SetText(string text)
        {
            if (TextValue == text) return;
            TextValue = text ?? "";
            Invalidate();
        }

        public void SetColor(Color color)
        {
            if (TextColor == color) return;
            TextColor = color;
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            if (string.IsNullOrEmpty(TextValue)) return;
            g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
            using (var b = new SolidBrush(TextColor))
            using (var sf = new StringFormat())
            {
                sf.Alignment = Align == ContentAlignment.MiddleCenter || Align == ContentAlignment.TopCenter || Align == ContentAlignment.BottomCenter
                    ? StringAlignment.Center
                    : (Align == ContentAlignment.TopRight || Align == ContentAlignment.MiddleRight || Align == ContentAlignment.BottomRight
                        ? StringAlignment.Far : StringAlignment.Near);
                sf.LineAlignment = Align == ContentAlignment.MiddleLeft || Align == ContentAlignment.MiddleCenter || Align == ContentAlignment.MiddleRight
                    ? StringAlignment.Center
                    : (Align == ContentAlignment.BottomLeft || Align == ContentAlignment.BottomCenter || Align == ContentAlignment.BottomRight
                        ? StringAlignment.Far : StringAlignment.Near);
                if (!Wrap) sf.FormatFlags = StringFormatFlags.NoWrap;
                g.DrawString(TextValue, TextFont, b, new RectangleF(0, 0, Width, Height), sf);
            }
        }

        /// 按给定宽度算换行后的高度（布局时用）。
        public static int MeasureHeight(string text, Font font, int width)
        {
            if (string.IsNullOrEmpty(text)) return 0;
            using (var bmp = new Bitmap(1, 1))
            using (var g = Graphics.FromImage(bmp))
            {
                g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
                return (int)Math.Ceiling(g.MeasureString(text, font, width).Height) + 2;
            }
        }
    }

    /// 底部状态条：左侧状态文字 + 右侧时钟/内存，GDI+ 绘制（Mica 下 alpha 正确）。
    internal sealed class FluentStatusBar : Control
    {
        private string _left = "就绪";
        private string _right = "";

        public FluentStatusBar()
        {
            SetStyle(ControlStyles.SupportsTransparentBackColor, true);
            SetStyle(ControlStyles.SupportsTransparentBackColor, true);
            BackColor = Color.Transparent;
            Height = 32;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer
                     | ControlStyles.UserPaint | ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor, true);
        }

        public void SetLeft(string text)
        {
            if (_left == text) return;
            _left = text ?? "";
            Invalidate();
        }

        public void SetRight(string text)
        {
            if (_right == text) return;
            _right = text ?? "";
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (var pen = new Pen(Theme.Divider))
                g.DrawLine(pen, 0, 0, Width, 0);
            Fluent.Text(g, _left, Theme.Caption, new RectangleF(24f, 0f, Width - 320f, Height), Theme.TextSecondary, ContentAlignment.MiddleLeft);
            Fluent.Text(g, _right, Theme.Caption, new RectangleF(Width - 320f, 0f, 300f, Height), Theme.TextTertiary, ContentAlignment.MiddleRight);
        }
    }

    /// 可滚动容器：内容放 Content 里，滚动条是自绘的细条（原生滚动条是浅灰方块，很跳）。
    internal sealed class FluentScrollPanel : Panel
    {
        private const int ScrollWidth = 10;
        private readonly VScrollBar _bar;
        private int _offset;
        private bool _dragging;

        public readonly Panel Content;

        public FluentScrollPanel()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer
                     | ControlStyles.UserPaint | ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor, true);
            SetStyle(ControlStyles.SupportsTransparentBackColor, true);
            SetStyle(ControlStyles.SupportsTransparentBackColor, true);
            BackColor = Color.Transparent;

            Content = new Panel { BackColor = Color.Transparent, Location = new Point(0, 0) };
            Content.SizeChanged += delegate { UpdateScroll(); Invalidate(); };
            Controls.Add(Content);

            _bar = new VScrollBar { Visible = false, SmallChange = 1 };
            _bar.ValueChanged += delegate { _offset = _bar.Value; Content.Top = -_offset; Invalidate(); };
            Controls.Add(_bar);
        }

        private int ContentHeight
        {
            get { return Content.Height; }
        }

        private int ViewHeight
        {
            get { return Height - Padding.Vertical; }
        }

        private int MaxOffset
        {
            get { return Math.Max(0, ContentHeight - ViewHeight); }
        }

        private bool NeedsScroll
        {
            get { return MaxOffset > 0; }
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            Content.Width = Width - (NeedsScroll ? ScrollWidth : 0) - Padding.Horizontal;
            Content.Left = Padding.Left;
            UpdateScroll();
        }

        private void UpdateScroll()
        {
            Content.Width = Math.Max(60, Width - (NeedsScroll ? ScrollWidth : 0) - Padding.Horizontal);
            _bar.Maximum = Math.Max(0, ContentHeight - 1);
            _bar.LargeChange = Math.Max(1, ViewHeight);
            if (_offset > MaxOffset) _offset = MaxOffset;
            if (_offset < 0) _offset = 0;
            if (_bar.Value != _offset) _bar.Value = _offset;
            Content.Top = Padding.Top - _offset;
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            _offset = Math.Max(0, Math.Min(MaxOffset, _offset + (e.Delta > 0 ? -60 : 60)));
            UpdateScroll();
            Invalidate();
            base.OnMouseWheel(e);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            if (NeedsScroll && e.X >= Width - ScrollWidth) { _dragging = true; ScrollTo(e.Y); }
            base.OnMouseDown(e);
        }

        protected override void OnMouseUp(MouseEventArgs e) { _dragging = false; base.OnMouseUp(e); }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            if (_dragging) ScrollTo(e.Y);
            base.OnMouseMove(e);
        }

        private void ScrollTo(int y)
        {
            if (!NeedsScroll) return;
            int trackTop = Padding.Top + 2;
            int trackHeight = Math.Max(10, Height - Padding.Vertical - 4);
            int thumbHeight = Math.Max(28, (int)(trackHeight * (double)ViewHeight / ContentHeight));
            int travel = Math.Max(1, trackHeight - thumbHeight);
            int rel = y - trackTop - thumbHeight / 2;
            if (rel < 0) rel = 0;
            if (rel > travel) rel = travel;
            _offset = (int)Math.Round(MaxOffset * (double)rel / travel);
            UpdateScroll();
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            if (!NeedsScroll) return;
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            int trackTop = Padding.Top + 2;
            int trackHeight = Math.Max(10, Height - Padding.Vertical - 4);
            int thumbHeight = Math.Max(28, (int)(trackHeight * (double)ViewHeight / ContentHeight));
            int travel = Math.Max(1, trackHeight - thumbHeight);
            int thumbTop = trackTop + (MaxOffset == 0 ? 0 : (int)Math.Round(travel * (double)_offset / MaxOffset));
            Fluent.Fill(g, new RectangleF(Width - 7f, trackTop, 4f, trackHeight), 2f, Theme.Alpha(Theme.TextTertiary, 26));
            Fluent.Fill(g, new RectangleF(Width - 7f, thumbTop, 4f, thumbHeight), 2f,
                _dragging ? Theme.Accent : Theme.Alpha(Theme.TextTertiary, 140));
        }
    }
}
