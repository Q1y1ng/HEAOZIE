// 「定时关闭应用」页（Fluent 版）：勾选进程 → 设定倒计时 → 到点结束它们。
// 逻辑与 v1 相同（安全名单、点选目标、二次确认、撤销窗口、白名单式持久化），只换外观层。

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Text;
using System.Windows.Forms;
using Hz.Sys;

namespace Hz.Panels
{
    internal sealed class KillerPanel : PanelBase
    {
        private readonly Countdown _count = new Countdown();
        private readonly Countdown _grace = new Countdown();
        private readonly System.Windows.Forms.Timer _tick = new System.Windows.Forms.Timer { Interval = 250 };
        private readonly System.Windows.Forms.Timer _pickTimer = new System.Windows.Forms.Timer { Interval = 100 };
        private readonly System.Windows.Forms.Timer _refreshAfterKill = new System.Windows.Forms.Timer { Interval = 1500 };

        private TimeSetupCard _setup;
        private ProcTable _table;
        private FluentText _countNote;
        private FluentText _digit;
        private FluentText _status;
        private FluentButton _btnStart;
        private FluentButton _btnPause;
        private FluentButton _btnReset;
        private FluentButton _btnCancel;
        private FluentButton _btnPick;
        private FluentButton _btnMinimize;

        private bool _inGrace;
        private bool _picking;
        private bool _sawButtonRelease;
        private bool _autoMinimize;
        private bool _loaded;

        internal bool DemoMode;

        public override string Title { get { return "定时关闭应用"; } }

        public KillerPanel(IHzHost host) : base(host)
        {
            _autoMinimize = host.Cfg.KillerAutoMinimize;

            TableLayoutPanel split = BuildSplitLayout(440f);

            _setup = new TimeSetupCard("设定倒计时", "到达目标时刻后结束勾选的进程（时 : 分 : 秒）", host.Cfg.KillerPresets, 86, true)
            { Height = 184 };
            _setup.SetSeconds(1800);
            _setup.Changed += delegate { UpdateHint(); };

            FluentCard countdown = BuildCountdownCard();
            split.Controls.Add(BuildColumn(_setup, countdown), 0, 0);
            split.Controls.Add(BuildTableCard(), 1, 0);

            _tick.Tick += OnTick;
            _pickTimer.Tick += OnPickTick;
            _refreshAfterKill.Tick += delegate { _refreshAfterKill.Stop(); RefreshProcesses(); };

            ResetUi("等待启动");
        }

        internal void DemoStart(int seconds)
        {
            DemoMode = true;
            RefreshProcesses();
            _table.CheckTop(3);
            _setup.SetSeconds(seconds);
            StartCountdown(true);
            Host.Log("演示模式：倒计时 " + seconds + " 秒，到点后只记录不结束进程");
        }

        // ── 界面 ────────────────────────────────────────────────

        private FluentCard BuildTableCard()
        {
            var card = new FluentCard("选择要关闭的进程",
                "勾选左侧方框；也可以点「点击选择」后在屏幕上点目标窗口") { Dock = DockStyle.Fill };

            var host = new Panel { Dock = DockStyle.Fill, Padding = new Padding(0), BackColor = Color.Transparent };
            _table = new ProcTable { Dock = DockStyle.Fill };
            _table.SelectionChanged += delegate { UpdateCountNote(); };
            host.Controls.Add(_table);
            card.Controls.Add(host);

            FlowLayoutPanel row = AddButtonRow(card, 84, true);
            var refresh = new FluentButton("刷新", 84, 32) { Glyph = Theme.Ico.Refresh };
            refresh.Click += delegate { RefreshProcesses(); };
            var all = new FluentButton("全选", 78, 32) { Glyph = Theme.Ico.Check };
            all.Click += delegate { _table.CheckAll(); };
            var none = new FluentButton("取消全选", 100, 32) { Glyph = Theme.Ico.Close };
            none.Click += delegate { _table.UncheckAll(); };
            _btnPick = new FluentButton("点击选择", 112, 32) { Glyph = Theme.Ico.Search };
            _btnPick.Click += delegate { TogglePicker(); };
            _btnMinimize = new FluentButton(MinimizeLabel(), 178, 32) { Glyph = Theme.Ico.ChevronDown };
            _btnMinimize.Click += delegate { ToggleAutoMinimize(); };
            _countNote = new FluentText("", Theme.Caption, Theme.TextTertiary) { Size = new Size(150, 32), Margin = new Padding(10, 0, 0, 0), Align = ContentAlignment.MiddleLeft };

            row.Controls.Add(refresh);
            row.Controls.Add(all);
            row.Controls.Add(none);
            row.Controls.Add(_btnPick);
            row.Controls.Add(_btnMinimize);
            row.Controls.Add(_countNote);
            return card;
        }

        private FluentCard BuildCountdownCard()
        {
            var card = new FluentCard("倒计时", null) { Height = 240 };

            _digit = new FluentText("00:00:00", Theme.Display, Theme.TextPrimary)
            {
                Location = new Point(16, 54),
                Size = new Size(400, 64),
                Align = ContentAlignment.MiddleLeft
            };
            card.Controls.Add(_digit);

            _status = new FluentText("等待启动", Theme.Body, Theme.TextSecondary)
            {
                Location = new Point(18, 126),
                Size = new Size(400, 22),
                Align = ContentAlignment.MiddleLeft
            };
            card.Controls.Add(_status);

            _btnStart = new FluentButton("开始", 108, 38) { Style = FluentButtonStyle.Accent, Glyph = Theme.Ico.Play, Location = new Point(16, 164) };
            _btnStart.Click += delegate { StartCountdown(false); };
            _btnPause = new FluentButton("暂停", 108, 38) { Glyph = Theme.Ico.Pause, Location = new Point(132, 164) };
            _btnPause.Click += delegate { TogglePause(); };
            _btnReset = new FluentButton("重置", 96, 38) { Glyph = Theme.Ico.Stop, Location = new Point(248, 164) };
            _btnReset.Click += delegate { ResetUi("已重置"); Host.SetStatus("倒计时已重置"); };
            _btnCancel = new FluentButton("立即取消", 126, 38) { Style = FluentButtonStyle.Danger, Glyph = Theme.Ico.Close, Location = new Point(352, 164), Visible = false };
            _btnCancel.Click += delegate { CancelGrace(); };

            card.Controls.Add(_btnStart);
            card.Controls.Add(_btnPause);
            card.Controls.Add(_btnReset);
            card.Controls.Add(_btnCancel);
            return card;
        }

        private string MinimizeLabel()
        {
            return _autoMinimize ? "点选时自动最小化：开" : "点选时自动最小化：关";
        }

        private void ToggleAutoMinimize()
        {
            _autoMinimize = !_autoMinimize;
            Host.Cfg.KillerAutoMinimize = _autoMinimize;
            Host.Cfg.Save();
            _btnMinimize.Text = MinimizeLabel();
            Host.SetStatus("点选目标时" + (_autoMinimize ? "自动最小化本窗口" : "保持本窗口"));
        }

        private void UpdateCountNote()
        {
            if (_countNote == null) return;
            int n = _table == null ? 0 : _table.CheckedPids.Count;
            _countNote.SetText(n == 0 ? "已勾选 0 个进程" : "已勾选 " + n + " 个进程");
        }

        private void UpdateHint()
        {
            if (_count.IsArmed)
            {
                _setup.SetHintOverride("目标时刻：" + _count.Deadline.ToString("yyyy-MM-dd HH:mm:ss") +
                                       (_count.IsPaused ? "（已暂停，剩余 " + Countdown.Format(_count.Remaining) + "）" : ""));
                return;
            }
            _setup.SetHintOverride(null);
        }

        // ── 进程列表 ────────────────────────────────────────────

        private void RefreshProcesses()
        {
            var rows = ProcList.Enumerate();
            _table.SetRows(rows);

            if (Host.Cfg.KillerTargets.Count > 0)
            {
                var pending = new List<string>(Host.Cfg.KillerTargets);
                foreach (ProcEntry e in rows)
                {
                    int idx = IndexOfName(pending, e.Name);
                    if (idx < 0) continue;
                    _table.SetChecked(e.Pid, true);
                    pending.RemoveAt(idx);
                    if (pending.Count == 0) break;
                }
            }
            _loaded = true;
            UpdateCountNote();
            Host.SetStatus("已刷新进程列表：" + rows.Count + " 个可关闭进程");
        }

        private static int IndexOfName(List<string> names, string name)
        {
            for (int i = 0; i < names.Count; i++)
            {
                if (string.Equals(names[i], name, StringComparison.OrdinalIgnoreCase)) return i;
            }
            return -1;
        }

        // ── 点选目标 ────────────────────────────────────────────

        private void TogglePicker()
        {
            if (_picking) { CancelPicker("已取消点选"); return; }
            if (_count.IsArmed || _inGrace)
            {
                Host.SetStatus("倒计时运行中不能点选目标");
                return;
            }
            _picking = true;
            _sawButtonRelease = false;
            _pickTimer.Start();
            _btnPick.Text = "点选进行中…";
            Host.SetStatus("请在屏幕上点击目标窗口（按 ESC 取消）");
            if (_autoMinimize)
            {
                Form f = FindForm();
                if (f != null) f.WindowState = FormWindowState.Minimized;
            }
        }

        private void OnPickTick(object sender, EventArgs e)
        {
            if (!_picking)
            {
                _pickTimer.Stop();
                return;
            }
            if (Native.KeyDown(Native.VK_ESCAPE))
            {
                CancelPicker("已取消点选");
                return;
            }
            if (!Native.KeyDown(Native.VK_LBUTTON))
            {
                _sawButtonRelease = true;
                return;
            }
            if (!_sawButtonRelease) return;
            FinishPicker(Native.ProcessIdUnderCursor());
        }

        private void CancelPicker(string reason)
        {
            _pickTimer.Stop();
            _picking = false;
            RestoreAfterPick();
            _btnPick.Text = "点击选择";
            Host.SetStatus(reason);
        }

        private void RestoreAfterPick()
        {
            Form f = FindForm();
            if (f == null) return;
            if (f.WindowState == FormWindowState.Minimized) f.WindowState = FormWindowState.Normal;
            f.Activate();
        }

        private void FinishPicker(int pid)
        {
            _pickTimer.Stop();
            _picking = false;
            RestoreAfterPick();
            _btnPick.Text = "点击选择";

            if (pid <= 0) { Host.SetStatus("没有取到有效窗口，请重试"); return; }
            if (ProcList.IsSelf(pid)) { Host.SetStatus("不能选择本程序自身"); return; }
            string name = ProcList.NameOf(pid);
            if (string.IsNullOrEmpty(name)) { Host.SetStatus("该窗口所属进程已退出"); return; }
            if (ProcList.IsProtected(name))
            {
                Host.SetStatus("「" + name + "」是系统关键进程，已跳过（关掉会影响桌面或服务）");
                Host.Log("点选被拒：" + name + " 在安全名单内");
                return;
            }
            if (!_table.HighlightPid(pid))
            {
                Host.SetStatus("「" + name + "」不在当前列表里，点「刷新」后重试");
                return;
            }
            _table.SetChecked(pid, true);
            Host.SetStatus("已勾选：" + name + "（PID " + pid + "）");
        }

        // ── 倒计时 ──────────────────────────────────────────────

        private void StartCountdown(bool skipConfirm)
        {
            if (_inGrace) return;
            if (_count.IsArmed && _count.IsPaused)
            {
                _count.Resume();
                _btnPause.Text = "暂停";
                UpdateHint();
                UpdateRunStatus();
                Host.SetStatus("已继续倒计时");
                return;
            }

            List<int> pids = _table.CheckedPids;
            if (pids.Count == 0) { Host.SetStatus("请先勾选要关闭的进程"); return; }
            TimeSpan total = _setup.Total;
            if (total <= TimeSpan.Zero) { Host.SetStatus("请先设定时间（时 / 分 / 秒）"); return; }

            if (!skipConfirm)
            {
                string names = string.Join("、", _table.CheckedNames.ToArray());
                if (names.Length > 320) names = names.Substring(0, 320) + "…";
                DialogResult answer = MessageBox.Show(this,
                    "将在 " + Countdown.Format(total) + " 后结束以下 " + pids.Count + " 个进程：\r\n\r\n" +
                    names + "\r\n\r\n确认开始倒计时？（到点前 " + Host.Cfg.CancelWindowSec + " 秒还可以取消）",
                    AppInfo.DisplayName, MessageBoxButtons.OKCancel, MessageBoxIcon.Warning);
                if (answer != DialogResult.OK) { Host.SetStatus("已取消"); return; }
            }

            Host.Cfg.KillerTargets.Clear();
            Host.Cfg.KillerTargets.AddRange(_table.CheckedNames);
            Host.Cfg.Save();

            _count.Start(total);
            _tick.Start();
            _setup.Lock(true);
            _btnStart.Enabled = false;
            _btnPause.Enabled = true;
            _btnPause.Text = "暂停";
            _btnReset.Enabled = true;
            _btnPick.Enabled = false;
            Host.Log("定时关闭应用开始：目标 " + pids.Count + " 个进程（" + string.Join(", ", _table.CheckedNames.ToArray()) +
                     "），倒计时 " + Countdown.Format(total) + "，目标 " + _count.Deadline.ToString("yyyy-MM-dd HH:mm:ss"));
            Host.SetTrayColor(Theme.Warning);
            UpdateHint();
            UpdateRunStatus();
            UpdateTray();
            Host.SetStatus("倒计时运行中，到点将结束 " + pids.Count + " 个进程");
        }

        private void UpdateRunStatus()
        {
            if (_status == null || _inGrace || !_count.IsArmed) return;
            if (_count.IsPaused)
            {
                _status.SetText("已暂停 · 剩余 " + Countdown.Format(_count.Remaining) + "（点「继续」恢复）");
                _status.SetColor(Theme.Warning);
                return;
            }
            _status.SetText("倒计时运行中 · 目标 " + _count.Deadline.ToString("HH:mm:ss") +
                            " · 到点结束 " + _table.CheckedPids.Count + " 个进程");
            _status.SetColor(Theme.TextSecondary);
        }

        private void TogglePause()
        {
            if (!_count.IsArmed) return;
            if (_count.IsPaused)
            {
                _count.Resume();
                _btnPause.Text = "暂停";
                Host.SetStatus("已继续倒计时");
            }
            else
            {
                _count.Pause();
                _btnPause.Text = "继续";
                Host.SetStatus("已暂停（剩余 " + Countdown.Format(_count.Remaining) + "）");
            }
            UpdateHint();
            UpdateRunStatus();
            UpdateTray();
        }

        private void OnTick(object sender, EventArgs e)
        {
            if (_inGrace)
            {
                int secs = (int)Math.Ceiling(_grace.Remaining.TotalSeconds);
                _digit.SetText(secs.ToString("00") + " 秒");
                _digit.SetColor(Theme.Danger);
                _status.SetText("⚠ 即将结束 " + _table.CheckedPids.Count + " 个进程，点「立即取消」可撤销");
                _status.SetColor(Theme.Danger);
                Host.SetTrayStatus(AppInfo.Name + " · " + secs + " 秒后关闭 " + _table.CheckedPids.Count + " 个进程");
                if (_grace.Poll()) ExecuteNow();
                return;
            }

            if (!_count.IsArmed)
            {
                _tick.Stop();
                return;
            }

            _digit.SetText(Countdown.Format(_count.Remaining));
            UpdateTray();
            if (_count.Poll()) EnterGrace();
        }

        private void UpdateTray()
        {
            if (_count.IsArmed && !_count.IsPaused)
                Host.SetTrayStatus(AppInfo.Name + " · 定时关闭应用 剩 " + Countdown.Format(_count.Remaining));
            else if (_count.IsArmed)
                Host.SetTrayStatus(AppInfo.Name + " · 已暂停（剩 " + Countdown.Format(_count.Remaining) + "）");
            else
                Host.SetTrayStatus(AppInfo.DisplayName);
        }

        private void EnterGrace()
        {
            _inGrace = true;
            int window = Math.Max(1, Host.Cfg.CancelWindowSec);
            _grace.Start(TimeSpan.FromSeconds(window));
            _btnCancel.Visible = true;
            _btnStart.Enabled = false;
            _btnPause.Enabled = false;
            _btnReset.Enabled = true;
            Host.SetTrayColor(Theme.Danger);
            Host.Log("定时到点，进入 " + window + " 秒撤销窗口，准备结束 " + _table.CheckedPids.Count + " 个进程");
            Host.Notify(AppInfo.Name, "将在 " + window + " 秒后结束 " + _table.CheckedPids.Count + " 个进程，可取消");
            Host.SetStatus("⚠ " + window + " 秒后结束 " + _table.CheckedPids.Count + " 个进程，可取消");
        }

        private void CancelGrace()
        {
            if (!_inGrace) return;
            _grace.Reset();
            _inGrace = false;
            _btnCancel.Visible = false;
            Host.Log("用户在撤销窗口内取消了本次关闭");
            Host.SetTrayColor(Theme.Success);
            ResetUi("已取消（撤销窗口内）");
            Host.SetStatus("已取消本次关闭");
        }

        private void ExecuteNow()
        {
            _inGrace = false;
            _tick.Stop();
            _btnCancel.Visible = false;

            List<int> pids = _table.CheckedPids;
            List<string> names = _table.CheckedNames;

            if (DemoMode)
            {
                Host.Log("演示模式：本应结束 " + pids.Count + " 个进程（" + string.Join(", ", names.ToArray()) + "），已跳过");
                Host.SetStatus("演示模式：流程走通，未结束任何进程");
                ResetUi("演示模式：流程走通，未结束任何进程");
                return;
            }

            if (pids.Count == 0)
            {
                Host.Log("到点执行时目标列表已空（进程可能已退出）");
                ResetUi("目标进程已全部退出，无需处理");
                return;
            }

            int ok = 0;
            int fail = 0;
            var failures = new StringBuilder();
            for (int i = 0; i < pids.Count; i++)
            {
                string error;
                string label = i < names.Count ? names[i] : "?";
                if (ProcList.Kill(pids[i], out error))
                {
                    ok++;
                    Host.Log("已结束进程：" + label + "（PID " + pids[i] + "）");
                }
                else
                {
                    fail++;
                    failures.AppendLine("  · " + label + "（PID " + pids[i] + "）：" + error);
                    Host.Log("结束失败：" + label + "（PID " + pids[i] + "）：" + error);
                }
            }

            string summary = "成功结束 " + ok + " 个进程" + (fail > 0 ? "，" + fail + " 个失败（见日志）" : "");
            Host.Log("定时关闭应用执行完毕：" + summary + (fail > 0 ? "\n" + failures : ""));
            Host.Notify(AppInfo.Name, summary);
            Host.SetStatus(summary);
            ResetUi(summary);
            _refreshAfterKill.Start();
        }

        private void ResetUi(string reason)
        {
            _count.Reset();
            _grace.Reset();
            _inGrace = false;
            _tick.Stop();
            if (_btnCancel != null) _btnCancel.Visible = false;
            if (_digit != null)
            {
                _digit.SetText("00:00:00");
                _digit.SetColor(Theme.TextPrimary);
            }
            if (_btnStart != null)
            {
                _btnStart.Enabled = true;
                _btnStart.Text = "开始";
            }
            if (_btnPause != null)
            {
                _btnPause.Enabled = false;
                _btnPause.Text = "暂停";
            }
            if (_btnReset != null) _btnReset.Enabled = false;
            if (_btnPick != null) _btnPick.Enabled = true;
            if (_setup != null) _setup.Lock(false);
            if (_status != null)
            {
                _status.SetText(reason);
                _status.SetColor(Theme.TextSecondary);
            }
            UpdateHint();
            UpdateCountNote();
            Host.SetTrayColor(Theme.Success);
            Host.SetTrayStatus(AppInfo.DisplayName);
        }

        // ── 生命周期 ────────────────────────────────────────────

        public override void OnEnter()
        {
            if (!_loaded) RefreshProcesses();
            UpdateHint();
            UpdateCountNote();
        }

        public override void OnLeave()
        {
            if (_picking) CancelPicker("已离开本页，点选取消");
        }

        public override void OnAppExit()
        {
            _tick.Stop();
            _pickTimer.Stop();
            _refreshAfterKill.Stop();
        }
    }
}
