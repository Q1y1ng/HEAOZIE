// 日志面板：自绘的多行日志（深色、按级别着色、自动跟到末尾、自绘滚动条）。
//
// 不用 TextBox 的原因和进程表一样：原生滚动条是浅灰的，深色界面里很跳；
// 而且日志只需要「追加 + 滚动 + 复制全文」三件事，自绘反而更简单。

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Text;
using System.Windows.Forms;

namespace Hz.Panels
{
    internal sealed class LogView : Control
    {
        private sealed class Line
        {
            public string Text;
            public Color Color;
        }

        private const int LineHeight = 17;
        private const int MaxLines = 1000;
        private const int ScrollWidth = 10;

        private readonly List<Line> _lines = new List<Line>();
        private int _offset;
        private bool _follow = true;
        private bool _dragging;

        public LogView()
        {
            BackColor = Theme.Card;
            Font = Theme.FontMono;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer
                     | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
        }

        public void Clear()
        {
            _lines.Clear();
            _offset = 0;
            _follow = true;
            Invalidate();
        }

        public void Append(string text)
        {
            Append(text, Theme.TextDim);
        }

        public void Append(string text, Color color)
        {
            if (text == null) return;
            foreach (string raw in text.Replace("\r", "").Split('\n'))
            {
                _lines.Add(new Line { Text = raw, Color = color });
            }
            while (_lines.Count > MaxLines) _lines.RemoveAt(0);
            if (_follow) _offset = MaxOffset;
            Invalidate();
        }

        public string AllText()
        {
            var sb = new StringBuilder();
            foreach (Line line in _lines) sb.AppendLine(line.Text);
            return sb.ToString();
        }

        public int LineCount { get { return _lines.Count; } }

        private int VisibleLines
        {
            get { return Math.Max(1, Height / LineHeight); }
        }

        private int MaxOffset
        {
            get { return Math.Max(0, _lines.Count - VisibleLines); }
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            if (_follow) _offset = MaxOffset;
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            _offset = Math.Max(0, Math.Min(MaxOffset, _offset + (e.Delta > 0 ? -3 : 3)));
            _follow = _offset >= MaxOffset;
            Invalidate();
            base.OnMouseWheel(e);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            if (MaxOffset > 0 && e.X >= Width - ScrollWidth) _dragging = true;
            base.OnMouseDown(e);
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            _dragging = false;
            base.OnMouseUp(e);
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            if (!_dragging || MaxOffset <= 0) return;
            int trackHeight = Math.Max(10, Height - 4);
            int thumbHeight = Math.Max(20, (int)(trackHeight * (double)VisibleLines / _lines.Count));
            int travel = Math.Max(1, trackHeight - thumbHeight);
            int rel = e.Y - 2 - thumbHeight / 2;
            if (rel < 0) rel = 0;
            if (rel > travel) rel = travel;
            _offset = (int)Math.Round(MaxOffset * (double)rel / travel);
            _follow = _offset >= MaxOffset;
            Invalidate();
            base.OnMouseMove(e);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.Clear(BackColor);

            int right = Width - (MaxOffset > 0 ? ScrollWidth : 0);
            int visible = VisibleLines;
            for (int i = 0; i < visible; i++)
            {
                int index = _offset + i;
                if (index >= _lines.Count) break;
                Line line = _lines[index];
                TextRenderer.DrawText(g, line.Text, Theme.FontMono, new Point(0, i * LineHeight),
                    line.Color, TextFormatFlags.NoPadding | TextFormatFlags.EndEllipsis);
            }

            if (_lines.Count == 0)
            {
                TextRenderer.DrawText(g, "（暂无日志：点「一键 Turbo」后这里会显示每一步结果）", Theme.FontUi,
                    new Point(0, 4), Theme.TextFaint, TextFormatFlags.NoPadding);
            }

            if (MaxOffset > 0)
            {
                int trackHeight = Math.Max(10, Height - 4);
                int thumbHeight = Math.Max(20, (int)(trackHeight * (double)VisibleLines / _lines.Count));
                int travel = Math.Max(1, trackHeight - thumbHeight);
                int thumbTop = 2 + (MaxOffset == 0 ? 0 : (int)Math.Round(travel * (double)_offset / MaxOffset));
                Soft.Fill(g, new RectangleF(Width - 7f, 2f, 4f, trackHeight), 2f, Theme.Alpha(Theme.TextFaint, 30));
                Soft.Fill(g, new RectangleF(Width - 7f, thumbTop, 4f, thumbHeight), 2f,
                    _dragging ? Theme.Accent : Theme.Alpha(Theme.TextDim, 150));
            }
        }
    }
}
