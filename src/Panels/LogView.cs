// 日志视图（Fluent 版）：等宽小字、按级别着色、自动跟到末尾、自绘 4px 滚动条。

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
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

        private const int LineHeight = 19;
        private const int MaxLines = 1000;
        private const int ScrollWidth = 10;

        private readonly List<Line> _lines = new List<Line>();
        private int _offset;
        private bool _follow = true;
        private bool _dragging;

        public LogView()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer
                     | ControlStyles.UserPaint | ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor, true);
            SetStyle(ControlStyles.SupportsTransparentBackColor, true);
            BackColor = Color.Transparent;
        }

        public void Clear()
        {
            _lines.Clear();
            _offset = 0;
            _follow = true;
            Invalidate();
        }

        public void Append(string text) { Append(text, Theme.TextSecondary); }

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

        private int VisibleLines { get { return Math.Max(1, Height / LineHeight); } }
        private int MaxOffset { get { return Math.Max(0, _lines.Count - VisibleLines); } }

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
            int thumbHeight = Math.Max(24, (int)(trackHeight * (double)VisibleLines / _lines.Count));
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
            g.SmoothingMode = SmoothingMode.AntiAlias;

            int visible = VisibleLines;
            for (int i = 0; i < visible; i++)
            {
                int index = _offset + i;
                if (index >= _lines.Count) break;
                Line line = _lines[index];
                Fluent.Text(g, line.Text, Theme.MonoSmall,
                    new RectangleF(0, i * LineHeight, Width - (MaxOffset > 0 ? ScrollWidth : 0), LineHeight),
                    line.Color, ContentAlignment.MiddleLeft);
            }

            if (_lines.Count == 0)
            {
                Fluent.Text(g, "（暂无日志：点「一键 Turbo」后这里会显示每一步结果）", Theme.Body,
                    new RectangleF(0, 0, Width, LineHeight * 2), Theme.TextTertiary, ContentAlignment.MiddleLeft);
            }

            if (MaxOffset > 0)
            {
                int trackHeight = Math.Max(10, Height - 4);
                int thumbHeight = Math.Max(24, (int)(trackHeight * (double)VisibleLines / _lines.Count));
                int travel = Math.Max(1, trackHeight - thumbHeight);
                int thumbTop = 2 + (MaxOffset == 0 ? 0 : (int)Math.Round(travel * (double)_offset / MaxOffset));
                Fluent.Fill(g, new RectangleF(Width - 7f, 2f, 4f, trackHeight), 2f, Theme.Alpha(Theme.TextTertiary, 26));
                Fluent.Fill(g, new RectangleF(Width - 7f, thumbTop, 4f, thumbHeight), 2f,
                    _dragging ? Theme.Accent : Theme.Alpha(Theme.TextTertiary, 150));
            }
        }
    }
}
