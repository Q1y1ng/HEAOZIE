// 设计系统：自绘控件与绘图助手（视觉语言对齐 launch-center 的设计系统 v3）。
//
// 为什么全自绘而不是用系统控件默认观感：深色主题下 WinForms 原生控件是亮色系的，
// 靠改 BackColor/ForeColor 只能得到"半亮半暗"的杂色界面；自绘才能拿到统一的
// 圆角、胶囊、矢量图标。代价是要自己处理悬停/按下/禁用三态 —— 这里做掉。

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace Hz
{
    /// 矢量图标（GDI+ 画，零图片资源）。
    internal enum GI
    {
        None, Play, Pause, Stop, Refresh, Power, Bolt, Gear, Info, Check,
        Close, Clock, Trash, Chip, Shield, Folder, Search, Up, Down, Minus, Plus, Flame
    }

    internal static class Soft
    {
        public static GraphicsPath Round(RectangleF r, float radius) { return Theme.Path(r, radius); }

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

        /// 胶囊：底色用同色低透明度，文字用本色 —— 比裸圆点更耐看，也更省地方。
        public static float DrawPill(Graphics g, float x, float y, string text, Font font, Color color, int alpha)
        {
            Size sz = TextRenderer.MeasureText(text, font);
            float w = sz.Width + 18f;
            float h = sz.Height + 6f;
            var r = new RectangleF(x, y, w, h);
            Fill(g, r, h / 2f, Theme.Alpha(color, alpha));
            Stroke(g, r, h / 2f, Theme.Alpha(color, Math.Min(255, alpha + 70)));
            TextRenderer.DrawText(g, text, font, new Point((int)(x + 9f), (int)(y + 3f)), color, TextFormatFlags.NoPadding);
            return w;
        }

        public static float PillWidth(string text, Font font)
        {
            return TextRenderer.MeasureText(text, font).Width + 18f;
        }

        public static float PillHeight(string text, Font font)
        {
            return TextRenderer.MeasureText(text, font).Height + 6f;
        }

        /// 进度条（内存/磁盘/占用率）。
        public static void DrawMeter(Graphics g, RectangleF r, double ratio, Color color)
        {
            if (ratio < 0) ratio = 0;
            if (ratio > 1) ratio = 1;
            Fill(g, r, r.Height / 2f, Theme.Alpha(Theme.TextFaint, 40));
            if (ratio <= 0) return;
            var fill = new RectangleF(r.X, r.Y, (float)(r.Width * ratio), r.Height);
            Fill(g, fill, r.Height / 2f, color);
        }

        /// 矢量图标：所有几何按外框比例算，任意尺寸都清晰。
        public static void DrawGlyph(Graphics g, GI glyph, RectangleF b, Color color)
        {
            if (glyph == GI.None || color.A == 0) return;
            float s = Math.Min(b.Width, b.Height);
            float cx = b.X + b.Width / 2f;
            float cy = b.Y + b.Height / 2f;
            float pen = Math.Max(1.3f, s / 11f);
            using (var p = new Pen(color, pen))
            using (var br = new SolidBrush(color))
            {
                p.StartCap = LineCap.Round;
                p.EndCap = LineCap.Round;
                p.LineJoin = LineJoin.Round;
                switch (glyph)
                {
                    case GI.Play:
                        g.FillPolygon(br, new[]
                        {
                            new PointF(cx - s * 0.24f, cy - s * 0.34f),
                            new PointF(cx + s * 0.32f, cy),
                            new PointF(cx - s * 0.24f, cy + s * 0.34f)
                        });
                        break;
                    case GI.Pause:
                        g.FillRectangle(br, cx - s * 0.26f, cy - s * 0.32f, s * 0.18f, s * 0.64f);
                        g.FillRectangle(br, cx + s * 0.08f, cy - s * 0.32f, s * 0.18f, s * 0.64f);
                        break;
                    case GI.Stop:
                        g.FillRectangle(br, cx - s * 0.28f, cy - s * 0.28f, s * 0.56f, s * 0.56f);
                        break;
                    case GI.Refresh:
                        g.DrawArc(p, cx - s * 0.32f, cy - s * 0.32f, s * 0.64f, s * 0.64f, 40f, 280f);
                        g.FillPolygon(br, new[]
                        {
                            new PointF(cx + s * 0.30f, cy - s * 0.36f),
                            new PointF(cx + s * 0.40f, cy - s * 0.12f),
                            new PointF(cx + s * 0.14f, cy - s * 0.18f)
                        });
                        break;
                    case GI.Power:
                        g.DrawArc(p, cx - s * 0.34f, cy - s * 0.30f, s * 0.68f, s * 0.68f, -60f, 300f);
                        g.DrawLine(p, cx, cy - s * 0.46f, cx, cy - s * 0.06f);
                        break;
                    case GI.Bolt:
                        g.FillPolygon(br, new[]
                        {
                            new PointF(cx + s * 0.10f, cy - s * 0.46f),
                            new PointF(cx - s * 0.30f, cy + s * 0.06f),
                            new PointF(cx - s * 0.02f, cy + s * 0.06f),
                            new PointF(cx - s * 0.10f, cy + s * 0.46f),
                            new PointF(cx + s * 0.30f, cy - s * 0.08f),
                            new PointF(cx + s * 0.02f, cy - s * 0.08f)
                        });
                        break;
                    case GI.Flame:
                        using (var path = new GraphicsPath())
                        {
                            path.AddBezier(cx, cy - s * 0.46f, cx + s * 0.40f, cy - s * 0.02f, cx + s * 0.22f, cy + s * 0.46f, cx, cy + s * 0.46f);
                            path.AddBezier(cx, cy + s * 0.46f, cx - s * 0.22f, cy + s * 0.46f, cx - s * 0.40f, cy - s * 0.02f, cx, cy - s * 0.46f);
                            g.FillPath(br, path);
                        }
                        break;
                    case GI.Gear:
                        g.DrawEllipse(p, cx - s * 0.20f, cy - s * 0.20f, s * 0.40f, s * 0.40f);
                        for (int i = 0; i < 6; i++)
                        {
                            double a = i * Math.PI / 3.0;
                            float x1 = cx + (float)(Math.Cos(a) * s * 0.28f);
                            float y1 = cy + (float)(Math.Sin(a) * s * 0.28f);
                            float x2 = cx + (float)(Math.Cos(a) * s * 0.44f);
                            float y2 = cy + (float)(Math.Sin(a) * s * 0.44f);
                            g.DrawLine(p, x1, y1, x2, y2);
                        }
                        break;
                    case GI.Info:
                        g.DrawEllipse(p, cx - s * 0.40f, cy - s * 0.40f, s * 0.80f, s * 0.80f);
                        g.FillRectangle(br, cx - s * 0.045f, cy - s * 0.22f, s * 0.09f, s * 0.10f);
                        g.FillRectangle(br, cx - s * 0.045f, cy - s * 0.04f, s * 0.09f, s * 0.28f);
                        break;
                    case GI.Check:
                        g.DrawLines(p, new[]
                        {
                            new PointF(cx - s * 0.34f, cy + s * 0.02f),
                            new PointF(cx - s * 0.10f, cy + s * 0.26f),
                            new PointF(cx + s * 0.34f, cy - s * 0.26f)
                        });
                        break;
                    case GI.Close:
                        g.DrawLine(p, cx - s * 0.28f, cy - s * 0.28f, cx + s * 0.28f, cy + s * 0.28f);
                        g.DrawLine(p, cx + s * 0.28f, cy - s * 0.28f, cx - s * 0.28f, cy + s * 0.28f);
                        break;
                    case GI.Clock:
                        g.DrawEllipse(p, cx - s * 0.40f, cy - s * 0.40f, s * 0.80f, s * 0.80f);
                        g.DrawLine(p, cx, cy, cx, cy - s * 0.26f);
                        g.DrawLine(p, cx, cy, cx + s * 0.22f, cy + s * 0.08f);
                        break;
                    case GI.Trash:
                        g.DrawRectangle(p, cx - s * 0.26f, cy - s * 0.20f, s * 0.52f, s * 0.56f);
                        g.DrawLine(p, cx - s * 0.36f, cy - s * 0.32f, cx + s * 0.36f, cy - s * 0.32f);
                        g.DrawLine(p, cx - s * 0.10f, cy - s * 0.44f, cx + s * 0.10f, cy - s * 0.44f);
                        break;
                    case GI.Chip:
                        g.DrawRectangle(p, cx - s * 0.26f, cy - s * 0.26f, s * 0.52f, s * 0.52f);
                        for (int i = -1; i <= 1; i++)
                        {
                            float o = i * s * 0.16f;
                            g.DrawLine(p, cx + o, cy - s * 0.42f, cx + o, cy - s * 0.26f);
                            g.DrawLine(p, cx + o, cy + s * 0.26f, cx + o, cy + s * 0.42f);
                        }
                        break;
                    case GI.Shield:
                        using (var path = new GraphicsPath())
                        {
                            path.AddLines(new[]
                            {
                                new PointF(cx, cy - s * 0.42f),
                                new PointF(cx + s * 0.34f, cy - s * 0.24f),
                                new PointF(cx + s * 0.34f, cy + s * 0.06f),
                                new PointF(cx, cy + s * 0.44f),
                                new PointF(cx - s * 0.34f, cy + s * 0.06f),
                                new PointF(cx - s * 0.34f, cy - s * 0.24f)
                            });
                            path.CloseFigure();
                            g.DrawPath(p, path);
                        }
                        break;
                    case GI.Folder:
                        g.DrawLines(p, new[]
                        {
                            new PointF(cx - s * 0.40f, cy + s * 0.30f),
                            new PointF(cx - s * 0.40f, cy - s * 0.26f),
                            new PointF(cx - s * 0.10f, cy - s * 0.26f),
                            new PointF(cx - s * 0.02f, cy - s * 0.12f),
                            new PointF(cx + s * 0.40f, cy - s * 0.12f),
                            new PointF(cx + s * 0.40f, cy + s * 0.30f),
                            new PointF(cx - s * 0.40f, cy + s * 0.30f)
                        });
                        break;
                    case GI.Search:
                        g.DrawEllipse(p, cx - s * 0.38f, cy - s * 0.38f, s * 0.52f, s * 0.52f);
                        g.DrawLine(p, cx + s * 0.10f, cy + s * 0.10f, cx + s * 0.38f, cy + s * 0.38f);
                        break;
                    case GI.Up:
                        g.FillPolygon(br, new[]
                        {
                            new PointF(cx, cy - s * 0.30f),
                            new PointF(cx + s * 0.32f, cy + s * 0.22f),
                            new PointF(cx - s * 0.32f, cy + s * 0.22f)
                        });
                        break;
                    case GI.Down:
                        g.FillPolygon(br, new[]
                        {
                            new PointF(cx, cy + s * 0.30f),
                            new PointF(cx + s * 0.32f, cy - s * 0.22f),
                            new PointF(cx - s * 0.32f, cy - s * 0.22f)
                        });
                        break;
                    case GI.Minus:
                        g.DrawLine(p, cx - s * 0.30f, cy, cx + s * 0.30f, cy);
                        break;
                    case GI.Plus:
                        g.DrawLine(p, cx - s * 0.30f, cy, cx + s * 0.30f, cy);
                        g.DrawLine(p, cx, cy - s * 0.30f, cx, cy + s * 0.30f);
                        break;
                }
            }
        }

        /// 深色底上的标签工厂：卡片内底色必须显式给 Card，否则会露出底层的 Bg。
        public static Label Label(string text, Color color, Font font, bool onCard)
        {
            return new Label
            {
                Text = text,
                ForeColor = color,
                Font = font,
                BackColor = onCard ? Theme.Card : Theme.Bg,
                AutoSize = true,
                Margin = new Padding(0)
            };
        }

        /// 可换行的说明文字（固定宽度，用于卡片正文）。
        public static Label Wrap(string text, Color color, Font font, int width, bool onCard)
        {
            var lbl = Label(text, color, font, onCard);
            lbl.AutoSize = false;
            lbl.Width = width;
            lbl.Height = TextRenderer.MeasureText(text, font, new Size(width, 0), TextFormatFlags.WordBreak).Height + 4;
            return lbl;
        }
    }

    /// 自绘按钮：圆角、悬停/按下/禁用三态、可带矢量图标。
    internal sealed class SoftButton : Control
    {
        public GI Icon = GI.None;
        public Color Tone = Theme.TextDim;
        public bool Primary;
        public bool Danger;
        public bool Ghost;

        private bool _hover;
        private bool _press;

        public SoftButton(string text, int width, int height)
        {
            Text = text;
            Size = new Size(width, height);
            Font = Theme.FontUi;
            BackColor = Theme.Bg;
            Cursor = Cursors.Hand;
            TabStop = false;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer
                     | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
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
            g.Clear(BackColor);

            bool en = base.Enabled;
            Color fill, border, fg;
            if (Primary)
            {
                fill = _hover ? Theme.AccentHover : Theme.Accent;
                if (_press) fill = Theme.Alpha(Theme.Accent, 220);
                border = Color.Transparent;
                fg = Color.White;
            }
            else if (Danger)
            {
                fill = _hover ? Theme.Danger : Theme.Alpha(Theme.Danger, 42);
                border = Theme.Alpha(Theme.Danger, 130);
                fg = _hover ? Color.White : Theme.Danger;
            }
            else if (Ghost)
            {
                fill = _hover ? Theme.CardHover : Color.Transparent;
                border = _hover ? Theme.CardBorder : Color.Transparent;
                fg = _hover ? Theme.Text : Tone;
            }
            else
            {
                fill = _hover ? Theme.CardHover : Theme.Card;
                border = Theme.CardBorder;
                fg = Tone;
            }
            if (!en)
            {
                if (fill.A > 0) fill = Theme.Alpha(fill, 70);
                border = Theme.Alpha(Theme.CardBorder, 90);
                fg = Theme.TextFaint;
            }

            var box = new RectangleF(0.5f, 0.5f, Width - 1f, Height - 1f);
            Soft.Fill(g, box, 8f, fill);
            Soft.Stroke(g, box, 8f, border);

            Size textSize = TextRenderer.MeasureText(Text, Font);
            float iconSide = Math.Min(Height - 10f, 16f);
            float iconW = Icon == GI.None ? 0f : iconSide + 6f;
            float total = iconW + textSize.Width;
            float x = (Width - total) / 2f;
            if (Icon != GI.None)
            {
                Soft.DrawGlyph(g, Icon, new RectangleF(x, (Height - iconSide) / 2f, iconSide, iconSide), fg);
                x += iconW;
            }
            TextRenderer.DrawText(g, Text, Font, new Point((int)Math.Round(x), (Height - textSize.Height) / 2), fg, TextFormatFlags.NoPadding);
        }
    }

    /// 分段切换条（顶部 Tab 用）：胶囊选中态 + 悬停反馈。
    internal sealed class SegmentedTabs : Control
    {
        public string[] Items = new string[0];
        public Action<int> OnChange;

        private int _selected;
        private int _hover = -1;

        public int Selected
        {
            get { return _selected; }
            set
            {
                if (value < 0 || value >= Items.Length || value == _selected) return;
                _selected = value;
                Invalidate();
                if (OnChange != null) OnChange(_selected);
            }
        }

        public SegmentedTabs()
        {
            Height = 42;
            Font = Theme.FontUi;
            BackColor = Theme.Bg;
            Cursor = Cursors.Hand;
            TabStop = false;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer
                     | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
        }

        private float ItemWidth(int i)
        {
            return TextRenderer.MeasureText(Items[i], Theme.FontUiBold).Width + 34f;
        }

        /// 装得下所有项所需的最小宽度（Dock=Left 时用，避免胶囊条被拉满整行）。
        public int PreferredWidth
        {
            get
            {
                float x = 10f;
                for (int i = 0; i < Items.Length; i++) x += ItemWidth(i) + 2f;
                return (int)Math.Ceiling(x);
            }
        }

        private int HitTest(Point p)
        {
            float x = 4f;
            for (int i = 0; i < Items.Length; i++)
            {
                float w = ItemWidth(i);
                if (p.X >= x && p.X <= x + w) return i;
                x += w + 2f;
            }
            return -1;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            int h = HitTest(e.Location);
            if (h != _hover) { _hover = h; Invalidate(); }
            base.OnMouseMove(e);
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            if (_hover != -1) { _hover = -1; Invalidate(); }
            base.OnMouseLeave(e);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            int h = HitTest(e.Location);
            if (h >= 0) Selected = h;
            base.OnMouseDown(e);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(BackColor);

            var box = new RectangleF(0.5f, 0.5f, Width - 1f, Height - 1f);
            Soft.Fill(g, box, Height / 2f, Theme.Surface);
            Soft.Stroke(g, box, Height / 2f, Theme.CardBorder);

            float x = 4f;
            for (int i = 0; i < Items.Length; i++)
            {
                float w = ItemWidth(i);
                var r = new RectangleF(x, 4f, w, Height - 8f);
                bool sel = i == _selected;
                if (sel)
                {
                    Soft.Fill(g, r, r.Height / 2f, Theme.Alpha(Theme.Accent, 48));
                    Soft.Stroke(g, r, r.Height / 2f, Theme.Alpha(Theme.Accent, 150));
                }
                else if (i == _hover)
                {
                    Soft.Fill(g, r, r.Height / 2f, Theme.CardHover);
                }
                Font f = sel ? Theme.FontUiBold : Theme.FontUi;
                Color fg = sel ? Theme.Accent : (i == _hover ? Theme.Text : Theme.TextDim);
                Size ts = TextRenderer.MeasureText(Items[i], f);
                TextRenderer.DrawText(g, Items[i], f,
                    new Point((int)(r.X + (r.Width - ts.Width) / 2f), (int)(r.Y + (r.Height - ts.Height) / 2f)),
                    fg, TextFormatFlags.NoPadding);
                x += w + 2f;
            }
        }
    }

    /// 卡片容器：圆角、低对比边框、可选标题/副标题；子控件底色需用 Theme.Card。
    internal sealed class SoftCard : Panel
    {
        public string Title = "";
        public string Subtitle = "";

        public SoftCard()
        {
            BackColor = Theme.Bg;
            Padding = new Padding(16);
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer
                     | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
        }

        public SoftCard(string title, string subtitle) : this()
        {
            Title = title ?? "";
            Subtitle = subtitle ?? "";
            Padding = new Padding(16, string.IsNullOrEmpty(Title) ? 16 : (string.IsNullOrEmpty(Subtitle) ? 44 : 58), 16, 16);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(BackColor);

            var box = new RectangleF(0.5f, 0.5f, Width - 1f, Height - 1f);
            Soft.Fill(g, box, 12f, Theme.Card);
            Soft.Stroke(g, box, 12f, Theme.CardBorder);

            if (!string.IsNullOrEmpty(Title))
                TextRenderer.DrawText(g, Title, Theme.FontUiBold, new Point(16, 14), Theme.Text, TextFormatFlags.NoPadding);
            if (!string.IsNullOrEmpty(Subtitle))
                TextRenderer.DrawText(g, Subtitle, Theme.FontSmall, new Point(16, 32), Theme.TextFaint, TextFormatFlags.NoPadding);
        }
    }

    /// 顶部信息条：应用名 + 右侧状态胶囊（权限、定时状态…）。
    internal sealed class HeaderBar : Control
    {
        public string Title = "";
        public string Subtitle = "";
        private readonly List<string> _chips = new List<string>();
        private readonly List<Color> _chipColors = new List<Color>();

        public HeaderBar()
        {
            Height = 64;
            BackColor = Theme.Bg;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer
                     | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
        }

        public void SetChips(params string[] chips)
        {
            _chips.Clear();
            _chipColors.Clear();
            foreach (string c in chips)
            {
                if (string.IsNullOrEmpty(c)) continue;
                int sep = c.IndexOf('|');
                if (sep > 0)
                {
                    _chips.Add(c.Substring(0, sep));
                    _chipColors.Add(ColorTranslator.FromHtml(c.Substring(sep + 1)));
                }
                else
                {
                    _chips.Add(c);
                    _chipColors.Add(Theme.TextDim);
                }
            }
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(BackColor);

            TextRenderer.DrawText(g, Title, Theme.FontTitle, new Point(20, 12), Theme.Text, TextFormatFlags.NoPadding);
            if (!string.IsNullOrEmpty(Subtitle))
                TextRenderer.DrawText(g, Subtitle, Theme.FontSmall, new Point(22, 38), Theme.TextFaint, TextFormatFlags.NoPadding);

            float x = Width - 20f;
            for (int i = _chips.Count - 1; i >= 0; i--)
            {
                float w = Soft.PillWidth(_chips[i], Theme.FontSmall);
                float h = Soft.PillHeight(_chips[i], Theme.FontSmall);
                x -= w;
                Soft.DrawPill(g, x, (Height - h) / 2f, _chips[i], Theme.FontSmall, _chipColors[i], 30);
                x -= 8f;
            }
        }
    }
}
