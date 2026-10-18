// 进程表：深色自绘表格（勾选框 / 进程名 / PID / 内存）。
//
// 为什么不用 ListView：原生 ListView 在深色主题下只能改背景与文字，表头、
// 勾选框、选中高亮仍是系统亮色观感 —— 一屏「半亮半暗」的杂色表格。
// 自绘的代价是要自己管滚动与命中测试，但那部分是确定的（见下面 HitTest 系列）。

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;
using Hz.Sys;

namespace Hz.Panels
{
    internal sealed class ProcTable : Control
    {
        private const int RowHeight = 26;
        private const int HeaderHeight = 30;
        private const int CheckWidth = 34;
        private const int PidWidth = 76;
        private const int MemWidth = 96;
        private const int ScrollWidth = 10;

        private readonly List<ProcEntry> _rows = new List<ProcEntry>();
        private readonly HashSet<int> _checked = new HashSet<int>();
        private readonly VScrollBar _bar;

        private int _hover = -1;
        private int _selected = -1;
        private int _highlightPid;
        private int _offset;
        private bool _draggingScroll;

        public event Action SelectionChanged;

        public ProcTable()
        {
            BackColor = Theme.Card;
            Font = Theme.FontUi;
            TabStop = true;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer
                     | ControlStyles.UserPaint | ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);

            // 原生 VScrollBar 在深色主题下是浅灰色的一条，很扎眼；这里只把它当"滚动状态容器"
            // （Maximum/LargeChange 的语义正好合用），实际显示与拖动由自绘指示条负责。
            _bar = new VScrollBar
            {
                Width = 14,
                SmallChange = 1,
                TabStop = false,
                Visible = false
            };
            _bar.ValueChanged += delegate { _offset = _bar.Value; Invalidate(); };
            Controls.Add(_bar);
        }

        public int RowCount { get { return _rows.Count; } }

        public List<int> CheckedPids
        {
            get
            {
                var list = new List<int>();
                foreach (ProcEntry e in _rows)
                {
                    if (_checked.Contains(e.Pid)) list.Add(e.Pid);
                }
                return list;
            }
        }

        public List<string> CheckedNames
        {
            get
            {
                var list = new List<string>();
                foreach (ProcEntry e in _rows)
                {
                    if (_checked.Contains(e.Pid)) list.Add(e.Name);
                }
                return list;
            }
        }

        public void SetRows(List<ProcEntry> rows)
        {
            _rows.Clear();
            if (rows != null) _rows.AddRange(rows);

            // 已经退出的进程要顺手从勾选里去掉，否则会拿着过期 PID 去结束
            var alive = new HashSet<int>();
            foreach (ProcEntry e in _rows) alive.Add(e.Pid);
            var stale = new List<int>();
            foreach (int pid in _checked)
            {
                if (!alive.Contains(pid)) stale.Add(pid);
            }
            foreach (int pid in stale) _checked.Remove(pid);

            if (_selected >= _rows.Count) _selected = _rows.Count - 1;
            UpdateScroll();
            Invalidate();
            RaiseChanged();
        }

        public void CheckAll()
        {
            foreach (ProcEntry e in _rows) _checked.Add(e.Pid);
            Invalidate();
            RaiseChanged();
        }

        /// 勾选前 count 行（冒烟测试用：自动挑几个目标）。
        public void CheckTop(int count)
        {
            for (int i = 0; i < _rows.Count && i < count; i++) _checked.Add(_rows[i].Pid);
            Invalidate();
            RaiseChanged();
        }

        public void UncheckAll()
        {
            _checked.Clear();
            Invalidate();
            RaiseChanged();
        }

        public bool IsChecked(int pid)
        {
            return _checked.Contains(pid);
        }

        public void SetChecked(int pid, bool on)
        {
            if (on) _checked.Add(pid);
            else _checked.Remove(pid);
            Invalidate();
            RaiseChanged();
        }

        /// 点选目标命中：滚到该行、高亮、并选中。
        public bool HighlightPid(int pid)
        {
            for (int i = 0; i < _rows.Count; i++)
            {
                if (_rows[i].Pid != pid) continue;
                _highlightPid = pid;
                _selected = i;
                EnsureVisible(i);
                Invalidate();
                return true;
            }
            _highlightPid = 0;
            return false;
        }

        private void RaiseChanged()
        {
            if (SelectionChanged != null) SelectionChanged();
        }

        private int VisibleRows
        {
            get { return Math.Max(1, (Height - HeaderHeight) / RowHeight); }
        }

        private int MaxOffset
        {
            get { return Math.Max(0, _rows.Count - VisibleRows); }
        }

        private bool NeedsScroll
        {
            get { return _rows.Count > VisibleRows; }
        }

        private void UpdateScroll()
        {
            _bar.Maximum = Math.Max(0, _rows.Count - 1);
            _bar.LargeChange = VisibleRows;
            _bar.SmallChange = 1;
            if (_offset > MaxOffset) _offset = MaxOffset;
            if (_offset < 0) _offset = 0;
            if (_bar.Value != _offset) _bar.Value = _offset;
        }

        private void EnsureVisible(int index)
        {
            if (index < _offset) _offset = index;
            else if (index >= _offset + VisibleRows) _offset = index - VisibleRows + 1;
            UpdateScroll();
        }

        private int RowAt(Point p)
        {
            if (p.Y < HeaderHeight) return -1;
            int index = (p.Y - HeaderHeight) / RowHeight + _offset;
            if (index < 0 || index >= _rows.Count) return -1;
            return index;
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            UpdateScroll();
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            Focus();
            if (NeedsScroll && e.X >= Width - ScrollWidth)
            {
                _draggingScroll = true;
                ScrollToPoint(e.Y);
                base.OnMouseDown(e);
                return;
            }
            int row = RowAt(e.Location);
            if (row >= 0)
            {
                _selected = row;
                if (e.X < CheckWidth)
                {
                    int pid = _rows[row].Pid;
                    if (_checked.Contains(pid)) _checked.Remove(pid);
                    else _checked.Add(pid);
                    RaiseChanged();
                }
                Invalidate();
            }
            base.OnMouseDown(e);
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            _draggingScroll = false;
            base.OnMouseUp(e);
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            if (_draggingScroll)
            {
                ScrollToPoint(e.Y);
                base.OnMouseMove(e);
                return;
            }
            int row = RowAt(e.Location);
            if (row != _hover) { _hover = row; Invalidate(); }
            base.OnMouseMove(e);
        }

        /// 把滚动条上的 y 换算成偏移（拖动与点击轨道共用）。
        private void ScrollToPoint(int y)
        {
            if (!NeedsScroll) return;
            int trackTop = HeaderHeight + 2;
            int trackHeight = Math.Max(10, Height - trackTop - 2);
            int thumbHeight = Math.Max(24, (int)(trackHeight * (double)VisibleRows / _rows.Count));
            int travel = Math.Max(1, trackHeight - thumbHeight);
            int rel = y - trackTop - thumbHeight / 2;
            if (rel < 0) rel = 0;
            if (rel > travel) rel = travel;
            _offset = (int)Math.Round(MaxOffset * (double)rel / travel);
            UpdateScroll();
            Invalidate();
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            if (_hover != -1) { _hover = -1; Invalidate(); }
            base.OnMouseLeave(e);
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            int delta = e.Delta > 0 ? -3 : 3;
            _offset = Math.Max(0, Math.Min(MaxOffset, _offset + delta));
            UpdateScroll();
            Invalidate();
            base.OnMouseWheel(e);
        }

        protected override bool IsInputKey(Keys keyData)
        {
            if (keyData == Keys.Up || keyData == Keys.Down || keyData == Keys.Space) return true;
            return base.IsInputKey(keyData);
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (_rows.Count > 0)
            {
                if (e.KeyCode == Keys.Down)
                {
                    _selected = Math.Min(_rows.Count - 1, _selected + 1);
                    EnsureVisible(_selected);
                    Invalidate();
                    e.Handled = true;
                }
                else if (e.KeyCode == Keys.Up)
                {
                    _selected = Math.Max(0, _selected - 1);
                    EnsureVisible(_selected);
                    Invalidate();
                    e.Handled = true;
                }
                else if (e.KeyCode == Keys.Space && _selected >= 0 && _selected < _rows.Count)
                {
                    int pid = _rows[_selected].Pid;
                    if (_checked.Contains(pid)) _checked.Remove(pid);
                    else _checked.Add(pid);
                    RaiseChanged();
                    Invalidate();
                    e.Handled = true;
                }
            }
            base.OnKeyDown(e);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            g.Clear(BackColor);

            int right = Width - (NeedsScroll ? ScrollWidth : 0);
            int nameWidth = Math.Max(80, right - CheckWidth - PidWidth - MemWidth);

            // 表头
            using (var b = new SolidBrush(Theme.Surface))
                g.FillRectangle(b, 0, 0, Width, HeaderHeight);
            using (var pen = new Pen(Theme.CardBorder))
                g.DrawLine(pen, 0, HeaderHeight - 1, Width, HeaderHeight - 1);

            TextRenderer.DrawText(g, "进程名", Theme.FontSmall, new Point(CheckWidth + 6, 8), Theme.TextDim, TextFormatFlags.NoPadding);
            TextRenderer.DrawText(g, "PID", Theme.FontSmall, new Point(CheckWidth + nameWidth + 6, 8), Theme.TextDim, TextFormatFlags.NoPadding);
            TextRenderer.DrawText(g, "内存", Theme.FontSmall, new Point(CheckWidth + nameWidth + PidWidth + 6, 8), Theme.TextDim, TextFormatFlags.NoPadding);

            int visible = VisibleRows;
            for (int i = 0; i < visible; i++)
            {
                int index = _offset + i;
                if (index >= _rows.Count) break;
                ProcEntry row = _rows[index];
                int y = HeaderHeight + i * RowHeight;

                bool isChecked = _checked.Contains(row.Pid);
                if (index == _selected)
                {
                    using (var b = new SolidBrush(Theme.Alpha(Theme.Info, 40)))
                        g.FillRectangle(b, 0, y, right, RowHeight);
                }
                else if (index == _hover)
                {
                    using (var b = new SolidBrush(Theme.CardHover))
                        g.FillRectangle(b, 0, y, right, RowHeight);
                }

                // 勾选框
                var boxRect = new RectangleF(9f, y + (RowHeight - 15) / 2f, 15f, 15f);
                if (isChecked)
                {
                    Soft.Fill(g, boxRect, 4f, Theme.Accent);
                    Soft.DrawGlyph(g, GI.Check, new RectangleF(boxRect.X + 1.5f, boxRect.Y + 1.5f, 12f, 12f), Color.White);
                }
                else
                {
                    Soft.Fill(g, boxRect, 4f, Theme.Alpha(Theme.TextFaint, 40));
                    Soft.Stroke(g, boxRect, 4f, Theme.CardBorder);
                }

                // 点选目标命中的那一行加个强调边框
                if (row.Pid == _highlightPid)
                {
                    using (var pen = new Pen(Theme.Alpha(Theme.Accent, 160)))
                        g.DrawRectangle(pen, 1, y + 1, right - 3, RowHeight - 3);
                }

                Color nameColor = isChecked ? Theme.Text : Theme.TextDim;
                TextRenderer.DrawText(g, Fit(row.Name, Theme.FontUi, nameWidth - 12), Theme.FontUi,
                    new Point(CheckWidth + 6, y + (RowHeight - 16) / 2), nameColor, TextFormatFlags.NoPadding);
                TextRenderer.DrawText(g, row.Pid.ToString(), Theme.FontMono,
                    new Point(CheckWidth + nameWidth + 6, y + (RowHeight - 16) / 2), Theme.TextFaint, TextFormatFlags.NoPadding);
                TextRenderer.DrawText(g, row.MemoryMb.ToString("0.0") + " MB", Theme.FontMono,
                    new Point(CheckWidth + nameWidth + PidWidth + 6, y + (RowHeight - 16) / 2), Theme.TextFaint, TextFormatFlags.NoPadding);
            }

            if (_rows.Count == 0)
            {
                TextRenderer.DrawText(g, "（列表为空，点「刷新」重新枚举）", Theme.FontUi,
                    new Point(CheckWidth, HeaderHeight + 12), Theme.TextFaint, TextFormatFlags.NoPadding);
            }
            else if (NeedsScroll)
            {
                DrawScrollIndicator(g);
            }
        }

        /// 自绘滚动指示条：4px 圆角滑块 + 悬停/拖动时提亮。
        private void DrawScrollIndicator(Graphics g)
        {
            int trackTop = HeaderHeight + 2;
            int trackHeight = Math.Max(10, Height - trackTop - 2);
            int thumbHeight = Math.Max(24, (int)(trackHeight * (double)VisibleRows / _rows.Count));
            int travel = Math.Max(1, trackHeight - thumbHeight);
            int thumbTop = trackTop + (MaxOffset == 0 ? 0 : (int)Math.Round(travel * (double)_offset / MaxOffset));
            var track = new RectangleF(Width - 7f, trackTop, 4f, trackHeight);
            var thumb = new RectangleF(Width - 7f, thumbTop, 4f, thumbHeight);
            Soft.Fill(g, track, 2f, Theme.Alpha(Theme.TextFaint, 30));
            Soft.Fill(g, thumb, 2f, _draggingScroll ? Theme.Accent : Theme.Alpha(Theme.TextDim, 150));
        }

        /// 进程名过长时截断加省略号（不做 tooltip，够用且不引入额外控件）。
        private static string Fit(string text, Font font, int width)
        {
            if (string.IsNullOrEmpty(text) || width <= 8) return text;
            if (TextRenderer.MeasureText(text, font).Width <= width) return text;
            string cut = text;
            while (cut.Length > 1 && TextRenderer.MeasureText(cut + "…", font).Width > width)
            {
                cut = cut.Substring(0, cut.Length - 1);
            }
            return cut + "…";
        }
    }
}
