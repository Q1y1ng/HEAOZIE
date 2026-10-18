#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
HEAOZIE 定时休眠 — 设定时间让电脑自动 睡眠/关机/重启
"""

import tkinter as tk
from tkinter import ttk
import threading
import time as time_module
import ctypes
import os
import sys
import subprocess

# ── 常量 ────────────────────────────────────────────
APP_NAME = "HEAOZIE 定时休眠"
BG = "#0d1117"
CARD_BG = "#161b22"
ACCENT = "#e94560"
GREEN = "#00c853"
TEXT_FG = "#e6edf3"
DIM_FG = "#8b949e"
FONT_DIGIT = ("Consolas", 56, "bold")
FONT_LARGE = ("Microsoft YaHei", 14, "bold")
FONT_NORMAL = ("Microsoft YaHei", 10)
FONT_SMALL = ("Microsoft YaHei", 9)

ACTION_SLEEP = "sleep"
ACTION_SHUTDOWN = "shutdown"
ACTION_RESTART = "restart"

# ── 执行动作 ────────────────────────────────────────
def _do_action(action):
    """执行选定的电源操作"""
    try:
        if action == ACTION_SLEEP:
            ctypes.windll.powrprof.SetSuspendState(0, 1, 0)
        elif action == ACTION_SHUTDOWN:
            subprocess.run(["shutdown", "/s", "/t", "0"], check=True)
        elif action == ACTION_RESTART:
            subprocess.run(["shutdown", "/r", "/t", "0"], check=True)
        return True
    except Exception as e:
        print(f"操作失败: {e}")
        return False

# ── 主窗口 ──────────────────────────────────────────
class SleepTimerApp:
    def __init__(self):
        self.window = tk.Tk()
        self.window.title(APP_NAME)
        self.window.configure(bg=BG)
        self.window.resizable(False, False)

        # 窗口居中
        ww, wh = 440, 500
        sw = self.window.winfo_screenwidth()
        sh = self.window.winfo_screenheight()
        x = (sw - ww) // 2
        y = (sh - wh) // 2
        self.window.geometry(f"{ww}x{wh}+{x}+{y}")

        # 状态变量
        self.remaining = 0
        self.running = False
        self.paused = False
        self.timer_thread = None
        self._stop_event = threading.Event()
        self.action_var = tk.StringVar(value=ACTION_SLEEP)

        # ── 构建界面 ──
        self._build_ui()
        self._update_clock()

        self.window.protocol("WM_DELETE_WINDOW", self._on_close)

    # ── 构建界面 ────────────────────────────────────
    def _build_ui(self):
        w = self.window

        # == Logo ==
        logo_frame = tk.Frame(w, bg=BG)
        logo_frame.pack(fill="x", pady=(12, 0))

        # 使用和 gaming booster 一致的 HEAOZIE 字符 logo
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

        # 副标题
        tk.Label(w, text="⏰ 定时电源管理", font=FONT_LARGE,
                 fg=TEXT_FG, bg=BG).pack(pady=(2, 8))

        # == 时间设定区域 ==
        set_frame = tk.Frame(w, bg=CARD_BG, bd=1, relief="solid",
                             highlightbackground="#2d333b", highlightthickness=1)
        set_frame.pack(fill="x", padx=20, pady=4)

        tk.Label(set_frame, text="设定倒计时", font=FONT_NORMAL,
                 fg=DIM_FG, bg=CARD_BG).pack(pady=(6, 4))

        # 时:分:秒 输入行
        spin_frame = tk.Frame(set_frame, bg=CARD_BG)
        spin_frame.pack(pady=(0, 4))

        tk.Label(spin_frame, text="时", font=FONT_SMALL, fg=DIM_FG, bg=CARD_BG)\
            .grid(row=0, column=0, padx=(0, 2))
        self.h_spin = tk.Spinbox(spin_frame, from_=0, to=23, width=3,
                                  font=("Consolas", 16), justify="center",
                                  bg=BG, fg=TEXT_FG, relief="flat", bd=0,
                                  highlightthickness=1, highlightbackground="#2d333b",
                                  buttonbackground=BG, insertbackground=TEXT_FG)
        self.h_spin.grid(row=0, column=1, padx=2)
        self.h_spin.delete(0, "end"); self.h_spin.insert(0, "0")

        tk.Label(spin_frame, text=":", font=("Consolas", 16, "bold"),
                 fg=DIM_FG, bg=CARD_BG).grid(row=0, column=2, padx=2)

        self.m_spin = tk.Spinbox(spin_frame, from_=0, to=59, width=3,
                                  font=("Consolas", 16), justify="center",
                                  bg=BG, fg=TEXT_FG, relief="flat", bd=0,
                                  highlightthickness=1, highlightbackground="#2d333b",
                                  buttonbackground=BG, insertbackground=TEXT_FG)
        self.m_spin.grid(row=0, column=3, padx=2)
        self.m_spin.delete(0, "end"); self.m_spin.insert(0, "30")

        tk.Label(spin_frame, text=":", font=("Consolas", 16, "bold"),
                 fg=DIM_FG, bg=CARD_BG).grid(row=0, column=4, padx=2)

        self.s_spin = tk.Spinbox(spin_frame, from_=0, to=59, width=3,
                                  font=("Consolas", 16), justify="center",
                                  bg=BG, fg=TEXT_FG, relief="flat", bd=0,
                                  highlightthickness=1, highlightbackground="#2d333b",
                                  buttonbackground=BG, insertbackground=TEXT_FG)
        self.s_spin.grid(row=0, column=5, padx=2)
        self.s_spin.delete(0, "end"); self.s_spin.insert(0, "0")

        # 快速预设按钮
        preset_frame = tk.Frame(set_frame, bg=CARD_BG)
        preset_frame.pack(pady=(0, 6))

        for label, secs in [("10 分钟", 600), ("30 分钟", 1800),
                            ("1 小时", 3600), ("2 小时", 7200)]:
            btn = tk.Button(preset_frame, text=label, font=FONT_SMALL,
                            bg="#21262d", fg=TEXT_FG, activebackground="#30363d",
                            activeforeground=TEXT_FG, relief="flat", bd=0,
                            padx=10, pady=2, cursor="hand2",
                            command=lambda s=secs: self._set_preset(s))
            btn.pack(side="left", padx=3)

        # == 动作选择（睡眠 / 关机 / 重启） ==
        action_frame = tk.Frame(w, bg=CARD_BG, bd=1, relief="solid",
                                highlightbackground="#2d333b", highlightthickness=1)
        action_frame.pack(fill="x", padx=20, pady=4)

        tk.Label(action_frame, text="到达时间后执行", font=FONT_NORMAL,
                 fg=DIM_FG, bg=CARD_BG).pack(pady=(6, 4))

        radio_frame = tk.Frame(action_frame, bg=CARD_BG)
        radio_frame.pack(pady=(0, 6))

        for val, txt, icon in [
            (ACTION_SLEEP, "睡眠", "💤"),
            (ACTION_SHUTDOWN, "关机", "⏻"),
            (ACTION_RESTART, "重启", "🔄"),
        ]:
            rb = tk.Radiobutton(radio_frame, text=f"{icon} {txt}", variable=self.action_var,
                                 value=val, font=FONT_NORMAL, fg=TEXT_FG, bg=CARD_BG,
                                 selectcolor=CARD_BG, activebackground=CARD_BG,
                                 activeforeground=TEXT_FG, relief="flat",
                                 highlightthickness=0, bd=0)
            rb.pack(side="left", padx=8)

        # == 倒计时显示 ==
        self.countdown_label = tk.Label(w, text="00:00:00", font=FONT_DIGIT,
                                        fg=ACCENT, bg=BG)
        self.countdown_label.pack(pady=(8, 2))

        self.status_label = tk.Label(w, text="🟢 等待启动", font=FONT_NORMAL,
                                     fg=DIM_FG, bg=BG)
        self.status_label.pack(pady=(0, 6))

        # == 控制按钮 ==
        ctrl_frame = tk.Frame(w, bg=BG)
        ctrl_frame.pack(pady=2)

        def _mk_btn(frame, text, fg, cmd):
            return tk.Button(frame, text=text, font=("Microsoft YaHei", 11, "bold"),
                             bg="#21262d", fg=fg, activebackground="#30363d",
                             activeforeground=fg, relief="flat", bd=0,
                             padx=18, pady=4, cursor="hand2", command=cmd)

        self.btn_start = _mk_btn(ctrl_frame, "▶  开始", GREEN, self._start_timer)
        self.btn_start.pack(side="left", padx=4)

        self.btn_pause = _mk_btn(ctrl_frame, "⏸  暂停", "#ffc107", self._pause_timer)
        self.btn_pause.pack(side="left", padx=4)
        self.btn_pause.config(state="disabled")

        self.btn_reset = _mk_btn(ctrl_frame, "⏹  重置", DIM_FG, self._reset_timer)
        self.btn_reset.pack(side="left", padx=4)
        self.btn_reset.config(state="disabled")

        # == 底部状态 ==
        tk.Frame(w, height=1, bg="#2d333b").pack(fill="x", padx=20, pady=(6, 4))

        self.footer_label = tk.Label(w, text="💤 到时间后自动执行选中的操作 (可取消)",
                                     font=FONT_SMALL, fg=DIM_FG, bg=BG)
        self.footer_label.pack(pady=(2, 6))

        # 取消按钮（隐藏）
        self.btn_cancel_sleep = tk.Button(w, text="✕  点击取消 (5s 内)",
                                          font=("Microsoft YaHei", 12, "bold"),
                                          bg=ACCENT, fg="white",
                                          activebackground="#ff1744",
                                          activeforeground="white",
                                          relief="flat", bd=0,
                                          padx=16, pady=6, cursor="hand2",
                                          command=self._cancel_pending)

        # 警告标签（隐藏）
        self.warn_label = tk.Label(w, text="即将执行...", font=("Consolas", 20, "bold"),
                                    fg=ACCENT, bg=BG)

    # ── 预设时间 ────────────────────────────────────
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

    def _action_label(self):
        return {
            ACTION_SLEEP: "睡眠",
            ACTION_SHUTDOWN: "关机",
            ACTION_RESTART: "重启",
        }.get(self.action_var.get(), "睡眠")

    def _action_icon(self):
        return {
            ACTION_SLEEP: "💤",
            ACTION_SHUTDOWN: "⏻",
            ACTION_RESTART: "🔄",
        }.get(self.action_var.get(), "💤")

    def _start_timer(self):
        if self.running:
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
        self._disable_spins()
        self.running = True
        self.paused = False
        self._stop_event.clear()

        h = self.remaining // 3600
        m = (self.remaining % 3600) // 60
        s = self.remaining % 60
        time_str = f"{h:02d}:{m:02d}:{s:02d}"
        lbl = self._action_label()
        self.status_label.config(text=f"⏳ {time_str} 后 {lbl}", fg=GREEN)
        self.footer_label.config(text=f"{self._action_icon()} 倒计时运行中...")

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
        self._enable_spins()
        self.countdown_label.config(text="00:00:00", fg=ACCENT)
        self.status_label.config(text="🟢 等待启动", fg=DIM_FG)
        self.footer_label.config(text=f"{self._action_icon()} 到时间后自动{self._action_label()} (可取消)")
        self.warn_label.pack_forget()
        self.btn_cancel_sleep.pack_forget()
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

    def _disable_spins(self):
        for s in (self.h_spin, self.m_spin, self.s_spin):
            s.config(state="disabled")

    def _enable_spins(self):
        for s in (self.h_spin, self.m_spin, self.s_spin):
            s.config(state="normal")

    # ── 倒计时线程 ──────────────────────────────────
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
            self._do_final_sequence()

    def _update_display(self, time_str, remaining):
        self.countdown_label.config(text=time_str)
        lbl = self._action_label()
        if remaining <= 10 and remaining > 0:
            self.countdown_label.config(fg=ACCENT)
            self.status_label.config(text=f"⚠️  即将{lbl} ({remaining}s)", fg=ACCENT)
        elif remaining > 0:
            self.countdown_label.config(fg=GREEN)
            self.status_label.config(text=f"⏳ 剩余 {time_str}", fg=GREEN)

    # ── 最终执行流程 ────────────────────────────────
    def _do_final_sequence(self):
        self.running = False
        self.paused = False
        lbl = self._action_label()
        ico = self._action_icon()
        self.countdown_label.config(text="00:00:00", fg=ACCENT)
        self.status_label.config(text=f"{ico} 准备{lbl}...", fg=ACCENT)

        self.footer_label.config(text=f"⏰ 点击下方按钮取消{lbl}", fg=ACCENT)
        self.warn_label.config(text=f"⚠️ 即将{lbl} ⚠️")
        self.warn_label.pack(pady=4)
        self.btn_cancel_sleep.pack(pady=4)

        self._stop_event.clear()
        self._pending_cancel = True

        def _execute():
            for i in range(5, 0, -1):
                self.window.after(0, self._update_warn, i)
                time_module.sleep(1)
                if not self._pending_cancel:
                    return
            self.window.after(0, self._execute_action)

        self._pending_cancel = True
        t = threading.Thread(target=_execute, daemon=True)
        t.start()

    def _update_warn(self, i):
        lbl = self._action_label()
        self.warn_label.config(text=f"⚠️ {i} 秒后{lbl} ⚠️")
        self.btn_cancel_sleep.config(text=f"✕  点击取消 ({i}s)")

    def _cancel_pending(self):
        self._pending_cancel = False
        self.warn_label.pack_forget()
        self.btn_cancel_sleep.pack_forget()
        self.countdown_label.config(text="00:00:00", fg=ACCENT)
        self.status_label.config(text="✅ 已取消", fg=GREEN)
        self.footer_label.config(text="点击「开始」重新设定倒计时")
        self._enable_spins()
        self._update_button_states()

    def _execute_action(self):
        act = self.action_var.get()
        lbl = self._action_label()
        ico = self._action_icon()
        self.warn_label.config(text=f"{ico} 正在{lbl}...")
        self.window.update()
        time_module.sleep(1)
        _do_action(act)

        # 如果操作失败（如被阻止），回到就绪
        self.status_label.config(text=f"⚠️ {lbl}失败或已被阻止", fg=ACCENT)
        self.warn_label.pack_forget()
        self.btn_cancel_sleep.pack_forget()
        self._enable_spins()
        self._update_button_states()

    # ── 时钟刷新 ────────────────────────────────────
    def _update_clock(self):
        self.window.after(250, self._update_clock)

    # ── 窗口关闭 ────────────────────────────────────
    def _on_close(self):
        if self.running or self.paused:
            self._stop_event.set()
        self.window.destroy()

    def run(self):
        self.window.mainloop()


if __name__ == "__main__":
    app = SleepTimerApp()
    app.run()
