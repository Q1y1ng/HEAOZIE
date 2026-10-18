// 「游戏加速」页（Fluent 版）：实时状态 + 一键 Turbo + 分类/选项开关 + 日志。
//
// 逻辑与 v1 相同（Turbo 在后台线程跑、分类名单、白名单双向同步、预演模式），
// 只换了外观层：开关用 Win11 ToggleSwitch，按钮用 Fluent 样式，文字全部 GDI+ 自绘。

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Threading;
using System.Windows.Forms;
using Hz.Sys;

namespace Hz.Panels
{
    internal sealed class BoosterPanel : PanelBase
    {
        private readonly SystemMonitor _monitor = new SystemMonitor();
        private readonly System.Windows.Forms.Timer _statusTimer = new System.Windows.Forms.Timer { Interval = 2000 };
        private readonly Dictionary<string, FluentButton> _categoryButtons = new Dictionary<string, FluentButton>(StringComparer.Ordinal);
        private readonly Dictionary<string, FluentToggleSwitch> _optionToggles = new Dictionary<string, FluentToggleSwitch>(StringComparer.Ordinal);

        private StatusView _status;
        private FluentText _statusNote;
        private LogView _log;
        private ProcTable _table;
        private FluentButton _btnTurbo;
        private FluentText _previewNote;
        private int _tick;
        private bool _busy;
        private bool _live;
        private bool _lastDryRun;

        public override string Title { get { return "游戏加速"; } }

        public BoosterPanel(IHzHost host) : base(host)
        {
            TableLayoutPanel split = BuildSplitLayout(440f);
            split.Controls.Add(BuildColumn(BuildStatusCard(), BuildTurboCard()), 0, 0);
            split.Controls.Add(BuildRightColumn(), 1, 0);

            _statusTimer.Tick += delegate { RefreshStatus(); };
            _log.Append("就绪。勾选分类后点「一键 Turbo」；每一步结果都会写在这里。");
            _log.Append("当前分类：" + BoosterOps.DescribeCategories(Host.Cfg.BoosterCats), Theme.TextTertiary);
            UpdateOptionToggles();
        }

        // ── 左列 ────────────────────────────────────────────────

        private FluentCard BuildStatusCard()
        {
            var card = new FluentCard("系统状态", "2 秒刷新 · GPU 走 nvidia-smi · 温度走 WMI") { Height = 268 };
            var host = new Panel { Dock = DockStyle.Fill, Padding = new Padding(0), BackColor = Color.Transparent };
            _status = new StatusView { Dock = DockStyle.Fill };
            host.Controls.Add(_status);
            card.Controls.Add(host);

            _statusNote = new FluentText("", Theme.Caption, Theme.TextTertiary)
            {
                Dock = DockStyle.Bottom,
                Height = 22,
                Align = ContentAlignment.MiddleLeft
            };
            card.Controls.Add(_statusNote);
            return card;
        }

        private FluentCard BuildTurboCard()
        {
            var card = new FluentCard("一键 Turbo", "结束后台进程 → 整理内存 → 清理临时文件 → 高性能电源 → 刷新 DNS")
            { Height = 352 };

            _btnTurbo = new FluentButton("一键 Turbo", 200, 38) { Style = FluentButtonStyle.Accent, Glyph = Theme.Ico.Bolt };
            _btnTurbo.Location = new Point(16, 56);
            _btnTurbo.Click += delegate { RunTurbo(); };
            card.Controls.Add(_btnTurbo);

            var white = new FluentButton("白名单", 120, 38) { Style = FluentButtonStyle.Standard, Glyph = Theme.Ico.Lock };
            white.Location = new Point(224, 56);
            white.Click += delegate { EditWhitelist(); };
            card.Controls.Add(white);

            var options = new FlowLayoutPanel
            {
                Location = new Point(16, 104),
                Size = new Size(410, 164),
                BackColor = Color.Transparent,
                WrapContents = true,
                FlowDirection = FlowDirection.LeftToRight
            };
            AddToggle(options, "kill", "结束后台进程");
            AddToggle(options, "trim", "整理内存");
            AddToggle(options, "clean", "清理临时文件");
            AddToggle(options, "power", "高性能电源");
            AddToggle(options, "dns", "刷新 DNS");
            AddToggle(options, "strong", "强效内存整理");
            AddToggle(options, "prefetch", "清理 Prefetch");
            card.Controls.Add(options);

            string note = SysEnv.IsAdmin()
                ? "管理员权限：强效内存整理与 Prefetch 清理可用。"
                : "普通用户：强效内存整理需管理员（自动回退温和），Prefetch 清理会跳过。";
            card.Controls.Add(new FluentText(note, Theme.Caption, Theme.TextTertiary)
            {
                Location = new Point(16, 252),
                Size = new Size(410, 18),
                Align = ContentAlignment.MiddleLeft
            });

            var restore = new FluentButton("恢复电源计划", 150, 32) { Style = FluentButtonStyle.Standard, Glyph = Theme.Ico.Refresh };
            restore.Location = new Point(16, 276);
            restore.Click += delegate { RestorePowerPlan(); };
            card.Controls.Add(restore);

            if (!SysEnv.IsAdmin())
            {
                var elevate = new FluentButton("以管理员重启", 150, 32) { Style = FluentButtonStyle.Standard, Glyph = Theme.Ico.Lock };
                elevate.Location = new Point(176, 276);
                elevate.Click += delegate
                {
                    Host.Log("用户请求以管理员身份重启");
                    MainForm.RequestElevatedRestart(0);
                };
                card.Controls.Add(elevate);
            }
            return card;
        }

        private void AddToggle(FlowLayoutPanel host, string key, string text)
        {
            var t = new FluentToggleSwitch(text, 196) { Margin = new Padding(0, 0, 8, 6) };
            t.CheckedChanged += delegate { ToggleOption(key); };
            _optionToggles[key] = t;
            host.Controls.Add(t);
        }

        // ── 右列 ────────────────────────────────────────────────

        private Control BuildRightColumn()
        {
            var right = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                BackColor = Color.Transparent,
                ColumnCount = 1,
                RowCount = 2,
                Margin = new Padding(16, 0, 0, 0)
            };
            right.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            right.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
            right.RowStyles.Add(new RowStyle(SizeType.Absolute, 200f));
            right.Controls.Add(BuildProcessCard(), 0, 0);
            right.Controls.Add(BuildLogCard(), 0, 1);
            return right;
        }

        private FluentCard BuildProcessCard()
        {
            var card = new FluentCard("后台进程（按分类）",
                "勾选框 = 本次会被结束；取消勾选会把这个进程名加入白名单") { Dock = DockStyle.Fill, Margin = new Padding(0, 0, 0, 16) };

            // Fill 必须先加，否则会被后加的 Top/Bottom 压住
            var host = new Panel { Dock = DockStyle.Fill, Padding = new Padding(0), BackColor = Color.Transparent };
            _table = new ProcTable { Dock = DockStyle.Fill };
            _table.SelectionChanged += delegate { SyncWhitelistFromTable(); };
            host.Controls.Add(_table);
            card.Controls.Add(host);

            var chips = new FlowLayoutPanel
            {
                Dock = DockStyle.Top,
                Height = 80,
                BackColor = Color.Transparent,
                WrapContents = true,
                Padding = new Padding(0, 0, 0, 4)
            };
            foreach (string key in BoosterOps.CategoryOrder)
            {
                var b = new FluentButton(BoosterOps.CategoryLabels[key], 96, 32) { Style = FluentButtonStyle.Standard, Margin = new Padding(0, 0, 6, 6) };
                string captured = key;
                b.Click += delegate { ToggleCategory(captured); };
                _categoryButtons[key] = b;
                chips.Controls.Add(b);
            }
            card.Controls.Add(chips);

            FlowLayoutPanel row = AddButtonRow(card, 44, false);
            var refresh = new FluentButton("刷新", 84, 32) { Glyph = Theme.Ico.Refresh };
            refresh.Click += delegate { RefreshProcessPreview(); };
            var all = new FluentButton("全选", 78, 32) { Glyph = Theme.Ico.Check };
            all.Click += delegate { _table.CheckAll(); SyncWhitelistFromTable(); };
            var none = new FluentButton("全部排除", 100, 32) { Glyph = Theme.Ico.Close };
            none.Click += delegate { _table.UncheckAll(); SyncWhitelistFromTable(); };
            _previewNote = new FluentText("", Theme.Caption, Theme.TextTertiary) { Size = new Size(110, 32), Margin = new Padding(6, 0, 0, 0), Align = ContentAlignment.MiddleLeft };
            row.Controls.Add(refresh);
            row.Controls.Add(all);
            row.Controls.Add(none);
            row.Controls.Add(_previewNote);
            return card;
        }

        private FluentCard BuildLogCard()
        {
            var card = new FluentCard("日志", null) { Dock = DockStyle.Fill, Margin = new Padding(0) };
            var host = new Panel { Dock = DockStyle.Fill, Padding = new Padding(0), BackColor = Color.Transparent };
            _log = new LogView { Dock = DockStyle.Fill };
            host.Controls.Add(_log);
            card.Controls.Add(host);

            FlowLayoutPanel row = AddButtonRow(card, 44, false);
            var clear = new FluentButton("清空", 84, 32) { Glyph = Theme.Ico.Delete };
            clear.Click += delegate { _log.Clear(); };
            var copy = new FluentButton("复制日志", 108, 32) { Glyph = Theme.Ico.Copy };
            copy.Click += delegate { CopyLog(); };
            var open = new FluentButton("打开日志文件", 132, 32) { Glyph = Theme.Ico.Folder };
            open.Click += delegate { OpenLogFile(); };
            row.Controls.Add(clear);
            row.Controls.Add(copy);
            row.Controls.Add(open);
            return card;
        }

        // ── 状态刷新 ────────────────────────────────────────────

        private void RefreshStatus()
        {
            if (!_live) return;
            _tick++;
            bool withGpu = (_tick % 2) == 1;
            bool withTemp = (_tick % 5) == 1;
            SystemSnapshot snap = _monitor.Capture(withGpu, withTemp);
            _status.Update(snap);
            string note = "刷新于 " + snap.At.ToString("HH:mm:ss");
            if (!string.IsNullOrEmpty(Host.Cfg.BoosterLastRun)) note += " · 上次 Turbo " + Host.Cfg.BoosterLastRun;
            _statusNote.SetText(note);
        }

        // ── 分类与选项 ──────────────────────────────────────────

        private void ToggleCategory(string key)
        {
            bool value = !Host.Cfg.BoosterCats.Get(key);
            Host.Cfg.BoosterCats.Set(key, value);
            Host.Cfg.Save();
            UpdateCategoryButtons();
            RefreshProcessPreview();
            Host.SetStatus((value ? "已勾选" : "已取消") + "分类「" + BoosterOps.CategoryLabels[key] + "」");
        }

        private void UpdateCategoryButtons()
        {
            foreach (KeyValuePair<string, FluentButton> kv in _categoryButtons)
            {
                kv.Value.Selected = Host.Cfg.BoosterCats.Get(kv.Key);
                kv.Value.Invalidate();
            }
        }

        private void ToggleOption(string key)
        {
            bool value = !GetOption(key);
            SetOption(key, value);
            Host.Cfg.Save();
        }

        private bool GetOption(string key)
        {
            switch (key)
            {
                case "kill": return Host.Cfg.BoosterKill;
                case "trim": return Host.Cfg.BoosterTrim;
                case "clean": return Host.Cfg.BoosterCleanTemp;
                case "power": return Host.Cfg.BoosterHighPerf;
                case "dns": return Host.Cfg.BoosterFlushDns;
                case "strong": return Host.Cfg.BoosterStrongTrim;
                case "prefetch": return Host.Cfg.BoosterCleanPrefetch;
                default: return false;
            }
        }

        private void SetOption(string key, bool value)
        {
            switch (key)
            {
                case "kill": Host.Cfg.BoosterKill = value; break;
                case "trim": Host.Cfg.BoosterTrim = value; break;
                case "clean": Host.Cfg.BoosterCleanTemp = value; break;
                case "power": Host.Cfg.BoosterHighPerf = value; break;
                case "dns": Host.Cfg.BoosterFlushDns = value; break;
                case "strong": Host.Cfg.BoosterStrongTrim = value; break;
                case "prefetch": Host.Cfg.BoosterCleanPrefetch = value; break;
            }
        }

        private void UpdateOptionToggles()
        {
            foreach (KeyValuePair<string, FluentToggleSwitch> kv in _optionToggles)
            {
                kv.Value.Checked = GetOption(kv.Key);
            }
        }

        // ── 进程预览与白名单 ────────────────────────────────────

        private void RefreshProcessPreview()
        {
            if (_table == null) return;
            var wanted = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string key in BoosterOps.CategoryOrder)
            {
                if (!Host.Cfg.BoosterCats.Get(key)) continue;
                foreach (string name in BoosterOps.ProcessesOf(key)) wanted.Add(name);
            }

            var rows = new List<ProcEntry>();
            foreach (ProcEntry e in ProcList.Enumerate())
            {
                string bare = e.Name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
                    ? e.Name.Substring(0, e.Name.Length - 4) : e.Name;
                if (wanted.Contains(bare)) rows.Add(e);
            }
            _table.SetRows(rows);
            _table.CheckAll();
            foreach (ProcEntry e in rows)
            {
                if (Host.Cfg.BoosterWhitelist.Contains(e.Name)) _table.SetChecked(e.Pid, false);
            }
            UpdatePreviewNote();
        }

        private void UpdatePreviewNote()
        {
            if (_previewNote == null) return;
            int total = _table.RowCount;
            int will = _table.CheckedPids.Count;
            _previewNote.SetText(total == 0 ? "无匹配进程" : "将结束 " + will + "/" + total);
        }

        private void SyncWhitelistFromTable()
        {
            if (_table == null) return;
            var checkedNames = new HashSet<string>(_table.CheckedNames, StringComparer.OrdinalIgnoreCase);
            bool changed = false;
            var updated = new List<string>();
            foreach (string name in Host.Cfg.BoosterWhitelist)
            {
                if (checkedNames.Contains(name)) { changed = true; continue; }
                updated.Add(name);
            }
            foreach (string name in _table.UncheckedNames)
            {
                if (!updated.Contains(name)) { updated.Add(name); changed = true; }
            }
            if (changed)
            {
                Host.Cfg.BoosterWhitelist.Clear();
                Host.Cfg.BoosterWhitelist.AddRange(updated);
                Host.Cfg.Save();
            }
            UpdatePreviewNote();
        }

        private void EditWhitelist()
        {
            using (var dlg = new Form
            {
                Text = "白名单 · 每行一个进程名（如 chrome.exe）",
                Size = new Size(440, 480),
                StartPosition = FormStartPosition.CenterParent,
                BackColor = Theme.Layer,
                ForeColor = Theme.TextPrimary,
                Font = Theme.Body,
                MinimizeBox = false,
                MaximizeBox = false,
                FormBorderStyle = FormBorderStyle.SizableToolWindow,
                Icon = Theme.AppIcon(16)
            })
            {
                var box = new TextBox
                {
                    Dock = DockStyle.Fill,
                    Multiline = true,
                    ScrollBars = ScrollBars.Vertical,
                    BackColor = Theme.LayerAlt,
                    ForeColor = Theme.TextPrimary,
                    BorderStyle = BorderStyle.FixedSingle,
                    Font = Theme.Mono,
                    Text = string.Join("\r\n", Host.Cfg.BoosterWhitelist.ToArray())
                };
                var boxHost = new Panel { Dock = DockStyle.Fill, Padding = new Padding(14), BackColor = Theme.Layer };
                boxHost.Controls.Add(box);
                dlg.Controls.Add(boxHost);

                var buttons = new Panel { Dock = DockStyle.Bottom, Height = 56, BackColor = Theme.Layer, Padding = new Padding(14, 10, 14, 10) };
                var save = new FluentButton("保存", 96, 32) { Style = FluentButtonStyle.Accent };
                save.Location = new Point(190, 12);
                var cancel = new FluentButton("取消", 96, 32);
                cancel.Location = new Point(296, 12);
                cancel.Click += delegate { dlg.DialogResult = DialogResult.Cancel; dlg.Close(); };
                save.Click += delegate
                {
                    var list = new List<string>();
                    foreach (string raw in box.Text.Replace("\r", "").Split('\n'))
                    {
                        string name = raw.Trim();
                        if (name.Length == 0 || list.Contains(name)) continue;
                        list.Add(name);
                    }
                    Host.Cfg.BoosterWhitelist.Clear();
                    Host.Cfg.BoosterWhitelist.AddRange(list);
                    Host.Cfg.Save();
                    Host.Log("白名单已更新，共 " + list.Count + " 条");
                    dlg.DialogResult = DialogResult.OK;
                    dlg.Close();
                };
                buttons.Controls.Add(save);
                buttons.Controls.Add(cancel);
                dlg.Controls.Add(buttons);

                dlg.ShowDialog(FindForm());
                RefreshProcessPreview();
                Host.SetStatus("白名单：" + Host.Cfg.BoosterWhitelist.Count + " 条");
            }
        }

        // ── 一键 Turbo ─────────────────────────────────────────

        private BoosterOptions CollectOptions()
        {
            var opt = new BoosterOptions
            {
                KillProcesses = Host.Cfg.BoosterKill,
                TrimMemory = Host.Cfg.BoosterTrim,
                CleanTemp = Host.Cfg.BoosterCleanTemp,
                HighPerformance = Host.Cfg.BoosterHighPerf,
                FlushDns = Host.Cfg.BoosterFlushDns,
                StrongTrim = Host.Cfg.BoosterStrongTrim,
                CleanPrefetch = Host.Cfg.BoosterCleanPrefetch
            };
            opt.Categories.Browsers = Host.Cfg.BoosterCats.Browsers;
            opt.Categories.Chat = Host.Cfg.BoosterCats.Chat;
            opt.Categories.Download = Host.Cfg.BoosterCats.Download;
            opt.Categories.Music = Host.Cfg.BoosterCats.Music;
            opt.Categories.Office = Host.Cfg.BoosterCats.Office;
            opt.Categories.Cloud = Host.Cfg.BoosterCats.Cloud;
            opt.Categories.Dev = Host.Cfg.BoosterCats.Dev;
            opt.Categories.Game = Host.Cfg.BoosterCats.Game;
            return opt;
        }

        private void RunTurbo() { RunTurbo(false, null); }

        /// 冒烟测试入口：延迟 400ms（面板在构造期创建，那时窗口句柄还没建好）。
        internal void DemoStart()
        {
            var delay = new System.Windows.Forms.Timer { Interval = 400 };
            delay.Tick += delegate
            {
                delay.Stop();
                delay.Dispose();
                _log.Append("演示模式：只预演「将结束哪些进程」，内存/清理/电源/DNS 全部跳过", Theme.Warning);
                var opt = new BoosterOptions
                {
                    KillProcesses = true,
                    TrimMemory = true,
                    CleanTemp = true,
                    HighPerformance = true,
                    FlushDns = true
                };
                opt.Categories.Browsers = true;
                opt.Categories.Chat = true;
                RunTurbo(true, opt);
            };
            delay.Start();
        }

        private void RunTurbo(bool dryRun, BoosterOptions preset)
        {
            if (_busy) return;
            _busy = true;
            _btnTurbo.Enabled = false;
            _btnTurbo.Text = dryRun ? "预演中…" : "加速中…";
            _log.Append("──── " + DateTime.Now.ToString("HH:mm:ss") + (dryRun ? "（预演）" : "") + " ────", Theme.Accent);

            BoosterOptions opt = preset ?? CollectOptions();
            _lastDryRun = dryRun;
            var whitelist = new List<string>(Host.Cfg.BoosterWhitelist);

            var worker = new Thread(delegate()
            {
                TurboReport report = null;
                try
                {
                    report = BoosterOps.RunTurbo(opt, whitelist, delegate(string line) { Post(line); }, dryRun);
                }
                catch (Exception ex)
                {
                    Post("Turbo 异常：" + ex.GetType().Name + ": " + ex.Message, Theme.Danger);
                    Log.Error("Turbo 异常", ex);
                }
                TurboReport captured = report;
                try
                {
                    BeginInvoke(new Action(delegate { OnTurboDone(captured); }));
                }
                catch { }
            });
            worker.IsBackground = true;
            worker.Name = "hz-turbo";
            worker.Start();
        }

        private void Post(string line) { Post(line, Theme.TextSecondary); }

        private void Post(string line, Color color)
        {
            try
            {
                if (InvokeRequired) { BeginInvoke(new Action<string, Color>(Post), line, color); return; }
                _log.Append(line, color);
            }
            catch { }
        }

        private void OnTurboDone(TurboReport report)
        {
            _busy = false;
            _btnTurbo.Enabled = true;
            _btnTurbo.Text = "一键 Turbo";
            Host.Cfg.BoosterLastRun = DateTime.Now.ToString("HH:mm:ss");
            if (report != null && report.PowerChanged) Host.Cfg.BoosterPreviousPowerPlan = report.PreviousPowerPlan;
            Host.Cfg.Save();

            if (report == null)
            {
                Host.SetStatus("Turbo 未完成，见日志");
                return;
            }
            string summary = _lastDryRun ? "预演完成" : "Turbo 完成";
            if (report.Kill != null)
            {
                summary += _lastDryRun
                    ? " · 预演命中 " + report.Kill.Killed + " 个进程（未结束）"
                    : " · 结束 " + report.Kill.Killed + " 个进程";
            }
            if (report.Trim != null) summary += " · 内存 " + (report.Trim.FreedMb >= 0 ? "+" : "") + report.Trim.FreedMb.ToString("0.0") + " MB";
            if (report.Clean != null) summary += " · 临时文件 " + report.Clean.FreedMb.ToString("0.0") + " MB";
            if (report.PowerChanged) summary += " · 已切高性能";
            if (report.DnsFlushed) summary += " · DNS 已刷新";
            Host.SetStatus(summary);
            Host.Notify(AppInfo.Name, summary);
            RefreshStatus();
            RefreshProcessPreview();
        }

        private void RestorePowerPlan()
        {
            string error;
            if (BoosterOps.RestorePowerPlan(Host.Cfg.BoosterPreviousPowerPlan, out error))
            {
                Host.Log("已恢复电源计划：" + Host.Cfg.BoosterPreviousPowerPlan);
                Host.SetStatus("已恢复切换前的电源计划");
                _log.Append("已恢复电源计划：" + Host.Cfg.BoosterPreviousPowerPlan, Theme.Success);
                Host.Cfg.BoosterPreviousPowerPlan = "";
                Host.Cfg.Save();
            }
            else
            {
                Host.Log("恢复电源计划失败：" + error);
                Host.SetStatus("恢复电源计划失败：" + error);
                _log.Append("恢复电源计划失败：" + error, Theme.Danger);
            }
        }

        private void CopyLog()
        {
            try
            {
                Clipboard.SetText(_log.AllText());
                Host.SetStatus("日志已复制到剪贴板");
            }
            catch (Exception ex)
            {
                Host.SetStatus("复制失败：" + ex.Message);
            }
        }

        private void OpenLogFile()
        {
            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = AppConfig.LogPath,
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                Host.SetStatus("打开日志失败：" + ex.Message);
            }
        }

        // ── 生命周期 ────────────────────────────────────────────

        public override void OnEnter()
        {
            _live = true;
            UpdateCategoryButtons();
            UpdateOptionToggles();
            RefreshStatus();
            RefreshProcessPreview();
            _statusTimer.Start();
        }

        public override void OnLeave()
        {
            _live = false;
            _statusTimer.Stop();
        }

        public override void OnAppExit()
        {
            _live = false;
            _statusTimer.Stop();
        }
    }
}
