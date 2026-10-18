#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
HEAOZIE 定时关闭应用 — 设定时间自动关闭指定程序
"""

import tkinter as tk
from tkinter import ttk
import threading
import time as time_module
import psutil
import os
import sys
import ctypes
import ctypes.wintypes

# ── 常量 ────────────────────────────────────────────
APP_NAME = "HEAOZIE 定时关闭应用"
BG = "#0d1117"
CARD_BG = "#161b22"
ACCENT = "#e94560"
GREEN = "#00c853"
TEXT_FG = "#e6edf3"
DIM_FG = "#8b949e"
FONT_DIGIT = ("Consolas", 48, "bold")
FONT_LARGE = ("Microsoft YaHei", 14, "bold")
FONT_NORMAL = ("Microsoft YaHei", 10)
FONT_SMALL = ("Microsoft YaHei", 9)


class AppKiller:
    def __init__(self):
        self.window = tk.Tk()
        self.window.title(APP_NAME)
        self.window.configure(bg=BG)
        self.window.resizable(True, True)
        self.window.minsize(480, 900)

        ww, wh = 520, 960
        sw = self.window.winfo_screenwidth()
        sh = self.window.winfo_screenheight()
        self.window.geometry(f"{ww}x{wh}+{(sw-ww)//2}+{(sh-wh)//2}")

        # 状态变量
        self.remaining = 0
        self.running = False
        self.paused = False
        self.timer_thread = None
        self._stop_event = threading.Event()
        self._process_vars = {}       # pid -> BooleanVar
        self._process_list = []       # [{'pid':.., 'name':.., 'label':..}, ...]
        self._picker_active = False   # 点击选择模式
        self._minimize_on_pick = True  # 点击选择时自动最小化

        # Windows API
        self._user32 = ctypes.windll.user32

        self._build_ui()
        self._update_clock()
        self._refresh_processes()

        self.window.protocol("WM_DELETE_WINDOW", self._on_close)

    # ── 构建界面 ────────────────────────────────────
    def _build_ui(self):
        w = self.window

        # == Logo ==
        logo_frame = tk.Frame(w, bg=BG)
        logo_frame.pack(fill="x", pady=(10, 0))

        logo_lines = [
            "█   █ █████  ███   ███  █████ █████ █████",
            "█   █ █     █   █ █   █    █    █   █    ",
            "█████ █████ █████ █   █   █     █   █████",
            "█   █ █     █   █ █   █  █      █   █    ",
            "█   █ █████ █   █  ███  █████ █████ █████",
        ]
        for line in logo_lines:
            tk.Label(logo_frame, text=line, font=("Consolas", 9, "bold"),
                     fg=ACCENT, bg=BG).pack()

        tk.Label(w, text="⏰ 定时关闭应用", font=FONT_LARGE,
                 fg=TEXT_FG, bg=BG).pack(pady=(2, 6))

        # == 时间设定 ==
        set_frame = tk.Frame(w, bg=CARD_BG, bd=1, relief="solid",
                             highlightbackground="#2d333b", highlightthickness=1)
        set_frame.pack(fill="x", padx=16, pady=3)

        tk.Label(set_frame, text="设定倒计时", font=FONT_NORMAL,
                 fg=DIM_FG, bg=CARD_BG).pack(pady=(4, 2))

        spin_frame = tk.Frame(set_frame, bg=CARD_BG)
        spin_frame.pack(pady=(0, 2))

        tk.Label(spin_frame, text="时", font=FONT_SMALL, fg=DIM_FG, bg=CARD_BG)\
            .grid(row=0, column=0, padx=(0, 2))
        self.h_spin = tk.Spinbox(spin_frame, from_=0, to=23, width=3,
                                  font=("Consolas", 16), justify="center",
                                  bg=BG, fg=TEXT_FG, relief="flat", bd=0,
                                  highlightthickness=1, highlightbackground="#2d333b",
                                  buttonbackground=BG)
        self.h_spin.grid(row=0, column=1, padx=2)
        self.h_spin.delete(0, "end"); self.h_spin.insert(0, "0")

        tk.Label(spin_frame, text=":", font=("Consolas", 16, "bold"),
                 fg=DIM_FG, bg=CARD_BG).grid(row=0, column=2, padx=2)

        self.m_spin = tk.Spinbox(spin_frame, from_=0, to=59, width=3,
                                  font=("Consolas", 16), justify="center",
                                  bg=BG, fg=TEXT_FG, relief="flat", bd=0,
                                  highlightthickness=1, highlightbackground="#2d333b",
                                  buttonbackground=BG)
        self.m_spin.grid(row=0, column=3, padx=2)
        self.m_spin.delete(0, "end"); self.m_spin.insert(0, "5")

        tk.Label(spin_frame, text=":", font=("Consolas", 16, "bold"),
                 fg=DIM_FG, bg=CARD_BG).grid(row=0, column=4, padx=2)

        self.s_spin = tk.Spinbox(spin_frame, from_=0, to=59, width=3,
                                  font=("Consolas", 16), justify="center",
                                  bg=BG, fg=TEXT_FG, relief="flat", bd=0,
                                  highlightthickness=1, highlightbackground="#2d333b",
                                  buttonbackground=BG)
        self.s_spin.grid(row=0, column=5, padx=2)
        self.s_spin.delete(0, "end"); self.s_spin.insert(0, "0")

        # 预设按钮
        preset_frame = tk.Frame(set_frame, bg=CARD_BG)
        preset_frame.pack(pady=(0, 4))

        for label, secs in [("5 分钟", 300), ("15 分钟", 900),
                            ("30 分钟", 1800), ("1 小时", 3600)]:
            btn = tk.Button(preset_frame, text=label, font=FONT_SMALL,
                            bg="#21262d", fg=TEXT_FG, activebackground="#30363d",
                            activeforeground=TEXT_FG, relief="flat", bd=0,
                            padx=8, pady=2, cursor="hand2",
                            command=lambda s=secs: self._set_preset(s))
            btn.pack(side="left", padx=2)

        # == 进程列表 ==
        proc_frame = tk.Frame(w, bg=CARD_BG, bd=1, relief="solid",
                              highlightbackground="#2d333b", highlightthickness=1)
        proc_frame.pack(fill="both", expand=True, padx=16, pady=3)

        # 标题行
        title_row = tk.Frame(proc_frame, bg=CARD_BG)
        title_row.pack(fill="x", padx=6, pady=(4, 2))

        tk.Label(title_row, text="📋 选择要关闭的进程", font=FONT_NORMAL,
                 fg=DIM_FG, bg=CARD_BG).pack(side="left")

        self.btn_pick = tk.Button(title_row, text="👆 点击选择", font=FONT_SMALL,
                                   bg="#21262d", fg="#58a6ff", relief="flat", bd=0,
                                   padx=6, pady=1, cursor="hand2",
                                   command=self._start_picker)
        self.btn_pick.pack(side="right", padx=(2, 0))

        self.btn_min_toggle = tk.Button(title_row, text="🗕 自动最小化", font=FONT_SMALL,
                                         bg="#238636", fg=TEXT_FG, relief="flat", bd=0,
                                         padx=6, pady=1, cursor="hand2",
                                         command=self._toggle_minimize)
        self.btn_min_toggle.pack(side="right", padx=2)

        self.btn_refresh = tk.Button(title_row, text="🔄 刷新", font=FONT_SMALL,
                                      bg="#21262d", fg=TEXT_FG, relief="flat", bd=0,
                                      padx=6, pady=1, cursor="hand2",
                                      command=self._refresh_processes)
        self.btn_refresh.pack(side="right", padx=2)

        btn_sel_all = tk.Button(title_row, text="全选", font=FONT_SMALL,
                                 bg="#21262d", fg=TEXT_FG, relief="flat", bd=0,
                                 padx=6, pady=1, cursor="hand2",
                                 command=self._select_all)
        btn_sel_all.pack(side="right", padx=2)

        btn_sel_none = tk.Button(title_row, text="取消", font=FONT_SMALL,
                                  bg="#21262d", fg=TEXT_FG, relief="flat", bd=0,
                                  padx=6, pady=1, cursor="hand2",
                                  command=self._select_none)
        btn_sel_none.pack(side="right", padx=2)

        # Treeview 进程列表
        tree_container = tk.Frame(proc_frame, bg=BG)
        tree_container.pack(fill="both", expand=True, padx=6, pady=(0, 4))

        self.tree = ttk.Treeview(tree_container,
                                  columns=("check", "name", "pid", "memory"),
                                  show="tree",
                                  height=14,
                                  selectmode="none")
        self.tree.column("#0", width=0, stretch=False)
        self.tree.column("check", width=40, anchor="center")
        self.tree.column("name", width=240, anchor="w")
        self.tree.column("pid", width=70, anchor="center")
        self.tree.column("memory", width=90, anchor="e")

        self.tree.heading("check", text="✓")
        self.tree.heading("name", text="进程名")
        self.tree.heading("pid", text="PID")
        self.tree.heading("memory", text="内存")

        # Treeview 样式
        style = ttk.Style()
        style.theme_use("clam")
        style.configure("Treeview",
                        background=BG,
                        foreground=TEXT_FG,
                        fieldbackground=BG,
                        rowheight=26,
                        font=("Microsoft YaHei", 9))
        style.configure("Treeview.Heading",
                        background=CARD_BG,
                        foreground=TEXT_FG,
                        relief="flat",
                        font=("Microsoft YaHei", 9, "bold"))
        style.map("Treeview", background=[("selected", BG)])
        style.map("Treeview.Heading", background=[("active", CARD_BG)])

        vsb = ttk.Scrollbar(tree_container, orient="vertical", command=self.tree.yview)
        self.tree.configure(yscrollcommand=vsb.set)
        vsb.pack(side="right", fill="y")
        self.tree.pack(fill="both", expand=True)

        self.tree.bind("<Button-1>", self._on_tree_click)

        # == 倒计时 + 控制 ==
        bottom_frame = tk.Frame(w, bg=BG)
        bottom_frame.pack(fill="x", padx=16, pady=(4, 6))

        self.countdown_label = tk.Label(bottom_frame, text="00:00:00",
                                         font=FONT_DIGIT, fg=ACCENT, bg=BG)
        self.countdown_label.pack(pady=(2, 0))

        self.status_label = tk.Label(bottom_frame, text="🟢 勾选要关闭的进程，然后点击开始",
                                      font=FONT_NORMAL, fg=DIM_FG, bg=BG)
        self.status_label.pack(pady=(0, 4))

        # 控制按钮
        ctrl_frame = tk.Frame(bottom_frame, bg=BG)
        ctrl_frame.pack(pady=2)

        def _mk_btn(fg, cmd):
            return tk.Button(ctrl_frame, font=("Microsoft YaHei", 11, "bold"),
                             bg="#21262d", fg=fg, activebackground="#30363d",
                             activeforeground=fg, relief="flat", bd=0,
                             padx=16, pady=3, cursor="hand2", command=cmd)

        self.btn_start = _mk_btn(GREEN, self._start_timer)
        self.btn_start.config(text="▶  开始")
        self.btn_start.pack(side="left", padx=4)

        self.btn_pause = _mk_btn("#ffc107", self._pause_timer)
        self.btn_pause.config(text="⏸  暂停", state="disabled")
        self.btn_pause.pack(side="left", padx=4)

        self.btn_reset = _mk_btn(DIM_FG, self._reset_timer)
        self.btn_reset.config(text="⏹  重置", state="disabled")
        self.btn_reset.pack(side="left", padx=4)

        tk.Frame(bottom_frame, height=1, bg="#2d333b").pack(fill="x", pady=(4, 2))

        self.footer_label = tk.Label(bottom_frame,
                                      text="💡 选中要关闭的应用，到时间后将自动结束进程",
                                      font=FONT_SMALL, fg=DIM_FG, bg=BG)
        self.footer_label.pack(pady=(0, 4))

    # ── 进程列表管理 ────────────────────────────────
    def _refresh_processes(self):
        """扫描系统进程，更新 Treeview"""
        # 保存之前的勾选状态
        checked_pids = set()
        for pid, var in self._process_vars.items():
            if var.get():
                checked_pids.add(pid)

        # 清空
        for item in self.tree.get_children():
            self.tree.delete(item)
        self._process_vars.clear()
        self._process_list = []

        # 获取自身 PID 和名称
        my_pid = os.getpid()
        my_name = psutil.Process(my_pid).name().lower() if psutil.pid_exists(my_pid) else ""

        # 系统进程黑名单（不让用户勾选）
        never_show = {
            "system", "system idle process", "registry", "smss.exe", "csrss.exe",
            "wininit.exe", "winlogon.exe", "services.exe", "lsass.exe", "lsaiso.exe",
            "svchost.exe", "spoolsv.exe", "conhost.exe", "dwm.exe", "fontdrvhost.exe",
            "sihost.exe", "runtimebroker.exe", "taskhostw.exe", "ctfmon.exe",
            "securityhealthservice.exe", "sppsvc.exe", "wlms.exe", "audiodg.exe",
            "msmpeng.exe", "mssense.exe", "nissrv.exe", "searchindexer.exe",
            "explorer.exe", "taskmgr.exe",
        }

        seen_names = {}
        for proc in psutil.process_iter(["pid", "name", "memory_info"]):
            try:
                pinfo = proc.info
                pid = pinfo["pid"]
                name = pinfo["name"]
                if not name:
                    continue
                name_lower = name.lower()

                # 跳过系统进程、自身、无 PID 进程
                if pid == my_pid or name_lower == my_name or name_lower in never_show:
                    continue
                if name_lower in ("python.exe", "python3.exe", "python3.12.exe", "pythonw.exe"):
                    continue

                mem_mb = round(pinfo["memory_info"].rss / 1024 / 1024, 1) if pinfo["memory_info"] else 0
                label = f"{name} ({pid})"

                # 去重：同名字取内存最大的
                if name in seen_names:
                    seen_names[name]["count"] += 1
                    seen_names[name]["entries"].append((pid, mem_mb))
                else:
                    seen_names[name] = {"count": 1, "entries": [(pid, mem_mb)]}

            except (psutil.NoSuchProcess, psutil.AccessDenied, Exception):
                continue

        # 整理显示：单实例直接显示，多实例合并显示
        self._process_vars = {}
        for name, info in sorted(seen_names.items(), key=lambda x: x[0].lower()):
            entries = info["entries"]
            if len(entries) == 1:
                pid, mem_mb = entries[0]
                var = tk.BooleanVar(value=(pid in checked_pids))
                self._process_vars[pid] = var
                self._process_list.append({"pid": pid, "name": name, "label": name})
                mem_str = f"{mem_mb:.1f} MB" if mem_mb > 0 else "-"
                self.tree.insert("", "end", iid=str(pid),
                                  values=("☐", name, pid, mem_str))
                # 如果之前勾选过
                if pid in checked_pids:
                    self.tree.item(str(pid), values=("☑", name, pid, mem_str))
            else:
                # 多实例：合并成一条，显示总内存
                total_mem = sum(e[1] for e in entries)
                pids_str = ",".join(str(e[0]) for e in entries)
                label_display = f"{name} ({len(entries)}个实例)"
                var = tk.BooleanVar(value=False)
                # 用第一个 PID 作为 key
                for pid, _ in entries:
                    self._process_vars[pid] = var
                self._process_list.append({"pid": entries, "name": name, "label": label_display})
                total_mem_str = f"{total_mem:.1f} MB" if total_mem > 0 else "-"
                self.tree.insert("", "end", iid=name + "_multi",
                                  values=("☐", label_display, pids_str, total_mem_str))

        self.status_label.config(text=f"🟢 共 {len(self.tree.get_children())} 个进程可关闭")

    def _select_all(self):
        for item in self.tree.get_children():
            vals = self.tree.item(item, "values")
            self.tree.item(item, values=("☑", vals[1], vals[2], vals[3]))
            self._set_item_check(item, True)

    def _select_none(self):
        for item in self.tree.get_children():
            vals = self.tree.item(item, "values")
            self.tree.item(item, values=("☐", vals[1], vals[2], vals[3]))
            self._set_item_check(item, False)

    def _set_item_check(self, item, checked):
        """根据 tree item 设置对应的 BooleanVar"""
        vals = self.tree.item(item, "values")
        pid_str = vals[2]
        if "," in pid_str:
            # 多实例，第一个 PID 对应的 var
            pids = [int(p.strip()) for p in pid_str.split(",")]
            for pid in pids:
                if pid in self._process_vars:
                    self._process_vars[pid].set(checked)
        else:
            try:
                pid = int(pid_str)
                if pid in self._process_vars:
                    self._process_vars[pid].set(checked)
            except ValueError:
                pass

    def _on_tree_click(self, event):
        """点击 Treeview 勾选框切换"""
        region = self.tree.identify_region(event.x, event.y)
        if region == "cell":
            column = self.tree.identify_column(event.x)
            if column == "#1":  # check 列
                item = self.tree.identify_row(event.y)
                if item:
                    vals = self.tree.item(item, "values")
                    if vals[0] == "☐":
                        self.tree.item(item, values=("☑", vals[1], vals[2], vals[3]))
                        self._set_item_check(item, True)
                    else:
                        self.tree.item(item, values=("☐", vals[1], vals[2], vals[3]))
                        self._set_item_check(item, False)

    # ── 最小化开关 ──────────────────────────────────
    def _toggle_minimize(self):
        """切换「点击选择时自动最小化」开关"""
        self._minimize_on_pick = not self._minimize_on_pick
        if self._minimize_on_pick:
            self.btn_min_toggle.config(text="🗕 自动最小化", bg="#238636")
        else:
            self.btn_min_toggle.config(text="🗗 不最小化", bg="#21262d")
        self.footer_label.config(
            text=f"{'🗕' if self._minimize_on_pick else '🗗'} 点击选择时{'自动最小化' if self._minimize_on_pick else '保持窗口'}")

    # ── 点击选择进程（Windows API） ─────────────────
    def _start_picker(self):
        """进入「点击选择」模式：用户点击目标窗口后自动勾选"""
        if self.running:
            return
        self._picker_active = True

        self.status_label.config(text="👆 点击任意应用窗口 (ESC 取消)", fg="#58a6ff")
        self.footer_label.config(text="点击目标窗口选中进程，或按 ESC 取消")
        if self._minimize_on_pick:
            self.window.iconify()  # 最小化

        def _poll_click():
            time_module.sleep(0.3)
            while self._picker_active:
                # 鼠标左键按下
                if self._user32.GetAsyncKeyState(0x01) & 0x8000:
                    time_module.sleep(0.08)
                    # 获取点击位置
                    pt = ctypes.wintypes.POINT()
                    self._user32.GetCursorPos(ctypes.byref(pt))
                    # 获取该位置的窗口句柄
                    hwnd = self._user32.WindowFromPoint(pt.x, pt.y)
                    # 获取 PID
                    pid = ctypes.wintypes.DWORD()
                    self._user32.GetWindowThreadProcessId(hwnd, ctypes.byref(pid))
                    self.window.after(0, self._highlight_pid, pid.value)
                    return
                # ESC 取消
                if self._user32.GetAsyncKeyState(0x1B) & 0x8000:
                    self.window.after(0, self._cancel_picker)
                    return
                time_module.sleep(0.05)

        t = threading.Thread(target=_poll_click, daemon=True)
        t.start()

    def _cancel_picker(self):
        self._picker_active = False
        self.window.deiconify()
        self.window.lift()
        self.status_label.config(text="🟢 已取消选择", fg=DIM_FG)
        self.footer_label.config(text="💡 选中要关闭的应用，到时间后将自动结束进程")

    def _highlight_pid(self, target_pid):
        """根据 PID 在 Treeview 中勾选对应进程"""
        self._picker_active = False
        self.window.deiconify()
        self.window.lift()
        self.window.focus_force()

        if target_pid <= 0:
            self.status_label.config(text="⚠️ 无效窗口", fg=ACCENT)
            return

        # 跳过自身
        if target_pid == os.getpid():
            self.status_label.config(text="⚠️ 不能选择本程序自身", fg=ACCENT)
            return

        found = False
        for item in self.tree.get_children():
            vals = self.tree.item(item, "values")
            pid_str = vals[2]

            if "," in pid_str:
                pids = [int(p.strip()) for p in pid_str.split(",")]
                if target_pid in pids:
                    self.tree.item(item, values=("☑", vals[1], vals[2], vals[3]))
                    self._set_item_check(item, True)
                    self.tree.selection_set(item)
                    self.tree.see(item)
                    found = True
                    break
            else:
                try:
                    if int(pid_str) == target_pid:
                        self.tree.item(item, values=("☑", vals[1], vals[2], vals[3]))
                        self._set_item_check(item, True)
                        self.tree.selection_set(item)
                        self.tree.see(item)
                        found = True
                        break
                except ValueError:
                    continue

        if found:
            try:
                pname = psutil.Process(target_pid).name()
            except Exception:
                pname = str(target_pid)
            self.status_label.config(text=f"✅ 已选中: {pname}", fg=GREEN)
            self.footer_label.config(text=f"PID {target_pid} 已勾选")
        else:
            # 可能是被过滤的系统进程，尝试从 psutil 获取名字
            try:
                pname = psutil.Process(target_pid).name()
                msg = f"⚠️ {pname} 在过滤列表中，无法勾选"
            except Exception:
                msg = f"⚠️ 进程 (PID {target_pid}) 不在列表中（可能已退出或已被过滤）"
            self.status_label.config(text=msg, fg=ACCENT)
            self.footer_label.config(text="系统关键进程或自身已被自动过滤")

    def _get_selected_pids(self):
        """获取所有勾选进程的 PID 列表"""
        pids = []
        for pid, var in self._process_vars.items():
            if var.get():
                if isinstance(pid, int):
                    pids.append(pid)
                elif isinstance(pid, list):
                    for p in pid:
                        pids.append(p)
        return pids

    # ── 预设 ────────────────────────────────────────
    def _set_preset(self, secs):
        h = secs // 3600
        m = (secs % 3600) // 60
        s = secs % 60
        self.h_spin.delete(0, "end"); self.h_spin.insert(0, str(h))
        self.m_spin.delete(0, "end"); self.m_spin.insert(0, str(m))
        self.s_spin.delete(0, "end"); self.s_spin.insert(0, str(s))

    # ── 控制方法 ────────────────────────────────────
    def _get_input_seconds(self):
        try:
            h = int(self.h_spin.get() or 0)
            m = int(self.m_spin.get() or 0)
            s = int(self.s_spin.get() or 0)
            total = h * 3600 + m * 60 + s
            return max(total, 1)
        except ValueError:
            return 300

    def _start_timer(self):
        if self.running:
            return

        selected = self._get_selected_pids()
        if not selected and not self.paused:
            self.status_label.config(text="⚠️ 请先勾选要关闭的进程", fg=ACCENT)
            return

        if self.paused:
            self.paused = False
            self.running = True
            self._stop_event.clear()
            self._update_button_states()
            self.timer_thread = threading.Thread(target=self._countdown, daemon=True)
            self.timer_thread.start()
            return

        self.remaining = self._get_input_seconds()
        self._disable_inputs()
        self.running = True
        self.paused = False
        self._stop_event.clear()

        h = self.remaining // 3600
        m = (self.remaining % 3600) // 60
        s = self.remaining % 60
        time_str = f"{h:02d}:{m:02d}:{s:02d}"
        self.status_label.config(text=f"⏳ 剩余 {time_str}，到时关闭 {len(selected)} 个进程",
                                  fg=GREEN)
        self.footer_label.config(text=f"🔫 将关闭 {len(selected)} 个进程")

        self._update_button_states()
        self.timer_thread = threading.Thread(target=self._countdown, daemon=True)
        self.timer_thread.start()

    def _pause_timer(self):
        if not self.running or self.paused:
            return
        self.paused = True
        self.running = False
        self._stop_event.set()
        self.status_label.config(text="⏸  已暂停", fg="#ffc107")
        self.footer_label.config(text="点击「开始」继续倒计时")
        self._update_button_states()

    def _reset_timer(self):
        self.running = False
        self.paused = False
        self._stop_event.set()
        self.remaining = 0
        self._enable_inputs()
        self.countdown_label.config(text="00:00:00", fg=ACCENT)
        self.status_label.config(text="🟢 已重置", fg=DIM_FG)
        self.footer_label.config(text="💡 选中要关闭的应用，到时间后将自动结束进程")
        self._update_button_states()

    def _update_button_states(self):
        if self.running and not self.paused:
            self.btn_start.config(state="disabled", text="▶  开始")
            self.btn_pause.config(state="normal")
            self.btn_reset.config(state="normal")
        elif self.paused:
            self.btn_start.config(state="normal", text="▶  继续")
            self.btn_pause.config(state="disabled")
            self.btn_reset.config(state="normal")
        else:
            self.btn_start.config(state="normal", text="▶  开始")
            self.btn_pause.config(state="disabled")
            self.btn_reset.config(state="disabled")

    def _disable_inputs(self):
        for s in (self.h_spin, self.m_spin, self.s_spin):
            s.config(state="disabled")
        self.btn_refresh.config(state="disabled")
        self.btn_pick.config(state="disabled")

    def _enable_inputs(self):
        for s in (self.h_spin, self.m_spin, self.s_spin):
            s.config(state="normal")
        self.btn_refresh.config(state="normal")
        self.btn_pick.config(state="normal")

    # ── 倒计时 ──────────────────────────────────────
    def _countdown(self):
        while self.remaining > 0 and not self._stop_event.is_set():
            h = self.remaining // 3600
            m = (self.remaining % 3600) // 60
            s = self.remaining % 60
            time_str = f"{h:02d}:{m:02d}:{s:02d}"
            self.window.after(0, self._update_display, time_str, self.remaining)
            time_module.sleep(1)
            if not self._stop_event.is_set():
                self.remaining -= 1

        if not self._stop_event.is_set() and self.remaining <= 0:
            self._execute_kill()

    def _update_display(self, time_str, remaining):
        self.countdown_label.config(text=time_str)
        n = len(self._get_selected_pids())
        if remaining <= 10 and remaining > 0:
            self.countdown_label.config(fg=ACCENT)
            self.status_label.config(text=f"⚠️  即将关闭进程 ({remaining}s)", fg=ACCENT)
        elif remaining > 0:
            self.countdown_label.config(fg=GREEN)
            self.status_label.config(text=f"⏳ 剩余 {time_str}，待关闭 {n} 个进程", fg=GREEN)

    # ── 执行关闭 ────────────────────────────────────
    def _execute_kill(self):
        self.running = False
        self.paused = False
        self.countdown_label.config(text="00:00:00", fg=ACCENT)

        pids = self._get_selected_pids()
        if not pids:
            self.status_label.config(text="⚠️ 没有勾选进程", fg=ACCENT)
            self._enable_inputs()
            self._update_button_states()
            return

        self.status_label.config(text=f"🔫 正在关闭 {len(pids)} 个进程...", fg=ACCENT)
        self.window.update()

        killed = 0
        failed = 0
        for pid in pids:
            try:
                proc = psutil.Process(pid)
                proc.terminate()
                # 等待进程结束
                try:
                    proc.wait(timeout=3)
                except psutil.TimeoutExpired:
                    # 强制结束
                    proc.kill()
                    try:
                        proc.wait(timeout=2)
                    except psutil.TimeoutExpired:
                        pass
                killed += 1
            except (psutil.NoSuchProcess, psutil.AccessDenied, Exception):
                failed += 1

        self._enable_inputs()
        self._update_button_states()

        msg = f"✅ 成功关闭 {killed} 个进程"
        if failed:
            msg += f"，{failed} 个失败（无权限或已退出）"

        self.status_label.config(text=msg, fg=GREEN if killed else ACCENT)
        self.footer_label.config(text="点击「重置」再次使用")
        self.countdown_label.config(fg=GREEN)

        # 刷新进程列表
        self.window.after(1500, self._refresh_processes)

    # ── 时钟 ────────────────────────────────────────
    def _update_clock(self):
        self.window.after(250, self._update_clock)

    def _on_close(self):
        if self.running or self.paused:
            self._stop_event.set()
        self.window.destroy()

    def run(self):
        self.window.mainloop()


if __name__ == "__main__":
    app = AppKiller()
    app.run()
