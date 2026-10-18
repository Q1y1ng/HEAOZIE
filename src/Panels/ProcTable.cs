// 进程表（Fluent 版）：Win11 列表观感 —— 32px 行高、16px 圆角勾选框、悬停/选中次级底、
// 自绘 4px 滚动条。文字走 GDI+（Mica 下 alpha 才正确）。

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using Hz.Sys;

namespace Hz.Panels
{
    internal sealed class ProcTable : Control
    {
        private const int RowHeight = 34;
        private const int HeaderHeight = 34;
        private const int CheckWidth = 40;
        private const int PidWidth = 84;
        private const int MemWidth = 104;
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
            SetStyle(ControlStyles.SupportsTransparentBackColor, true);
            BackColor = Color.Transparent;
            Font = Theme.Body;
            TabStop = true;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer
                     | ControlStyles.UserPaint | ControlStyles.ResizeRedraw | ControlStyles.Selectable
                     | ControlStyles.SupportsTransparentBackColor, true);

            _bar = new VScrollBar { Width = 14, SmallChange = 1, TabStop = false, Visible = false };
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

        public List<string> UncheckedNames
        {
            get
            {
                var list = new List<string>();
                foreach (ProcEntry e in _rows)
                {
                    if (!_checked.Contains(e.Pid)) list.Add(e.Name);
                }
                return list;
            }
        }

        public void SetRows(List<ProcEntry> rows)
        {
            _rows.Clear();
            if (rows != null) _rows.AddRange(rows);

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

        public bool IsChecked(int pid) { return _checked.Contains(pid); }

        public void SetChecked(int pid, bool on)
        {
            if (on) _checked.Add(pid);
            else _checked.Remove(pid);
            Invalidate();
            RaiseChanged();
        }

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

        private int VisibleRows { get { return Math.Max(1, (Height - HeaderHeight) / RowHeight); } }
        private int MaxOffset { get { return Math.Max(0, _rows.Count - VisibleRows); } }
        private bool NeedsScroll { get { return _rows.Count > VisibleRows; } }

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

        protected override void OnMouseLeave(EventArgs e)
        {
            if (_hover != -1) { _hover = -1; Invalidate(); }
            base.OnMouseLeave(e);
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            _offset = Math.Max(0, Math.Min(MaxOffset, _offset + (e.Delta > 0 ? -3 : 3)));
            UpdateScroll();
            Invalidate();
            base.OnMouseWheel(e);
        }

        private void ScrollToPoint(int y)
        {
            if (!NeedsScroll) return;
            int trackTop = HeaderHeight + 2;
            int trackHeight = Math.Max(10, Height - trackTop - 2);
            int thumbHeight = Math.Max(28, (int)(trackHeight * (double)VisibleRows / _rows.Count));
            int travel = Math.Max(1, trackHeight - thumbHeight);
            int rel = y - trackTop - thumbHeight / 2;
            if (rel < 0) rel = 0;
            if (rel > travel) rel = travel;
            _offset = (int)Math.Round(MaxOffset * (double)rel / travel);
            UpdateScroll();
            Invalidate();
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
            g.SmoothingMode = SmoothingMode.AntiAlias;

            int right = Width - (NeedsScroll ? ScrollWidth : 0);
            int nameWidth = Math.Max(80, right - CheckWidth - PidWidth - MemWidth);

            Fluent.Text(g, "进程名", Theme.Caption, new RectangleF(CheckWidth, 0, nameWidth, HeaderHeight), Theme.TextTertiary, ContentAlignment.MiddleLeft);
            Fluent.Text(g, "PID", Theme.Caption, new RectangleF(CheckWidth + nameWidth, 0, PidWidth, HeaderHeight), Theme.TextTertiary, ContentAlignment.MiddleLeft);
            Fluent.Text(g, "内存", Theme.Caption, new RectangleF(CheckWidth + nameWidth + PidWidth, 0, MemWidth, HeaderHeight), Theme.TextTertiary, ContentAlignment.MiddleLeft);
            using (var pen = new Pen(Theme.Divider))
                g.DrawLine(pen, 0, HeaderHeight - 1, right, HeaderHeight - 1);

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
                    Fluent.Fill(g, new RectangleF(0, y + 1, right, RowHeight - 2), Fluent.ControlRadius, Theme.SubtleFillHover);
                }
                else if (index == _hover)
                {
                    Fluent.Fill(g, new RectangleF(0, y + 1, right, RowHeight - 2), Fluent.ControlRadius, Theme.SubtleFill);
                }

                // Win11 勾选框：16px 圆角方块，选中 = 强调色底 + 勾
                var box = new RectangleF(14f, y + (RowHeight - 16) / 2f, 16f, 16f);
                if (isChecked)
                {
                    Fluent.Fill(g, box, 4f, Theme.Accent);
                    Fluent.Icon(g, Theme.Ico.Check, new RectangleF(box.X + 1f, box.Y + 1f, 14f, 14f), Theme.AccentText, Theme.IconSmallFont);
                }
                else
                {
                    Fluent.Stroke(g, box, 4f, Fluent.ControlStroke, 1.2f);
                }

                if (row.Pid == _highlightPid)
                {
                    Fluent.Stroke(g, new RectangleF(0.5f, y + 1.5f, right - 1f, RowHeight - 3f), Fluent.ControlRadius, Theme.Accent, 1.4f);
                }

                Fluent.Text(g, Fluent.Shorten(row.Name, 34), Theme.Body,
                    new RectangleF(CheckWidth, y, nameWidth - 8, RowHeight), isChecked ? Theme.TextPrimary : Theme.TextSecondary, ContentAlignment.MiddleLeft);
                Fluent.Text(g, row.Pid.ToString(), Theme.MonoSmall,
                    new RectangleF(CheckWidth + nameWidth, y, PidWidth, RowHeight), Theme.TextTertiary, ContentAlignment.MiddleLeft);
                Fluent.Text(g, Fluent.FormatMb(row.MemoryMb), Theme.MonoSmall,
                    new RectangleF(CheckWidth + nameWidth + PidWidth, y, MemWidth, RowHeight), Theme.TextTertiary, ContentAlignment.MiddleLeft);
            }

            if (_rows.Count == 0)
            {
                Fluent.Text(g, "（列表为空，点「刷新」重新枚举）", Theme.Body,
                    new RectangleF(CheckWidth, HeaderHeight, Width - CheckWidth * 2, RowHeight), Theme.TextTertiary, ContentAlignment.MiddleLeft);
            }
            else if (NeedsScroll)
            {
                int trackTop = HeaderHeight + 2;
                int trackHeight = Math.Max(10, Height - trackTop - 2);
                int thumbHeight = Math.Max(28, (int)(trackHeight * (double)VisibleRows / _rows.Count));
                int travel = Math.Max(1, trackHeight - thumbHeight);
                int thumbTop = trackTop + (MaxOffset == 0 ? 0 : (int)Math.Round(travel * (double)_offset / MaxOffset));
                Fluent.Fill(g, new RectangleF(Width - 7f, trackTop, 4f, trackHeight), 2f, Theme.Alpha(Theme.TextTertiary, 26));
                Fluent.Fill(g, new RectangleF(Width - 7f, thumbTop, 4f, thumbHeight), 2f,
                    _draggingScroll ? Theme.Accent : Theme.Alpha(Theme.TextTertiary, 150));
            }
        }
    }
}
