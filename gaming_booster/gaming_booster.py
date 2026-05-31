"""
游戏性能加速器 v2.1 — 主程序 GUI
HEAOZIE Brand
"""
import os
import sys
import json
import threading
import tkinter as tk
from tkinter import ttk, scrolledtext, messagebox
from pathlib import Path
import time

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from booster_core import (
    load_config,
    save_config,
    get_system_stats,
    get_gpu_stats,
    get_all_drives,
    get_cpu_temperature,
    get_categorized_processes,
    run_all_optimizations,
    restore_balanced_power_plan,
    is_admin,
    run_as_admin,
    CATEGORY_ICONS,
    CATEGORY_LABELS,
)


# ─── 主题配色 ──────────────────────────────────────────────────

THEME = {
    "bg": "#0d1117",
    "card": "#161b22",
    "card_alt": "#1c2333",
    "border": "#30363d",
    "accent": "#e94560",
    "accent_hover": "#ff6b81",
    "green": "#238636",
    "green_hover": "#2ea043",
    "blue": "#1f6feb",
    "text": "#e6edf3",
    "text_dim": "#8b949e",
    "text_bright": "#ffffff",
    "success": "#3fb950",
    "warning": "#d29922",
    "error": "#f85149",
    "info": "#58a6ff",
    "logo_bg": "#1a0a0a",
    "logo_text": "#ff6b6b",
}

FONT = ("Segoe UI", 10)
FONT_SM = ("Segoe UI", 9)
FONT_BOLD = ("Segoe UI", 10, "bold")
FONT_TITLE = ("Segoe UI", 20, "bold")
FONT_LOGO = ("Consolas", 9, "bold")
FONT_CARD = ("Segoe UI", 16, "bold")


# ─── HEAOZIE Logo 数据 ──────────────────────────────────────
# 每个字母 5列宽 x 5行高，字母之间 1列空格

HEAOZIE_LOGO = [
    "█   █ █████  ███   ███  █████ █████ █████",
    "█   █ █     █   █ █   █    █    █   █    ",
    "█████ █████ █████ █   █   █     █   █████",
    "█   █ █     █   █ █   █  █      █   █    ",
    "█   █ █████ █   █  ███  █████ █████ █████",
]


# ─── 主应用 ──────────────────────────────────────────────────

class GamingBoosterApp:
    def __init__(self, root: tk.Tk):
        self.root = root
        self.root.title("HEAOZIE | 游戏性能加速器")
        self.root.geometry("800x850")
        self.root.configure(bg=THEME["bg"])
        self.root.resizable(False, False)

        self.config = load_config()
        self.running = False
        self.monitor_active = True
        self.process_vars = {}      # pid -> BooleanVar
        self.process_data = []      # 当前进程列表
        self._status_subs = {}      # key -> sub-label (temperature)

        self._build_ui()
        self._start_monitor()

    # ─── UI 构建 ──────────────────────────────────────────────

    def _build_ui(self):
        self.root.grid_rowconfigure(5, weight=1)
        self.root.grid_columnconfigure(0, weight=1)

        self._build_banner()
        self._build_status_panel()
        self._build_turbo_button()
        self._build_options()
        self._build_middle_section()
        self._build_log()
        self._build_footer()

    # ─── HEAOZIE 品牌横幅 ────────────────────────────────────

    def _build_banner(self):
        frame = tk.Frame(self.root, bg=THEME["logo_bg"],
                         highlightbackground="#3a1a1a", highlightthickness=1)
        frame.grid(row=0, column=0, sticky="ew", padx=10, pady=(8, 4))

        # 左侧 Logo
        logo_frame = tk.Frame(frame, bg=THEME["logo_bg"])
        logo_frame.pack(side=tk.LEFT, padx=(15, 5), pady=4)

        for line in HEAOZIE_LOGO:
            tk.Label(logo_frame, text=line, font=FONT_LOGO,
                     bg=THEME["logo_bg"], fg=THEME["logo_text"]).pack(anchor=tk.W)

        # 右侧信息
        right_frame = tk.Frame(frame, bg=THEME["logo_bg"])
        right_frame.pack(side=tk.RIGHT, padx=(5, 15), pady=4)

        tk.Label(right_frame, text="™", font=("Segoe UI", 16, "bold"),
                 bg=THEME["logo_bg"], fg=THEME["accent"]).pack(anchor=tk.E)

        self.admin_badge = tk.Label(
            right_frame,
            text="⚡ ADMIN" if is_admin() else "⚡ USER",
            font=("Segoe UI", 8, "bold"),
            bg=THEME["green"] if is_admin() else THEME["card_alt"],
            fg=THEME["text_bright"],
            padx=6, pady=2
        )
        self.admin_badge.pack(anchor=tk.E, pady=(2, 0))

        # 副标题
        sub_frame = tk.Frame(self.root, bg=THEME["bg"])
        sub_frame.grid(row=1, column=0, sticky="ew", padx=15, pady=(2, 6))

        tk.Label(sub_frame, text="🚀 GAME BOOSTER", font=FONT_TITLE,
                 bg=THEME["bg"], fg=THEME["accent"]).pack(side=tk.LEFT)

        tk.Label(sub_frame, text="v2.1  |  HEAOZIE 出品",
                 font=("Segoe UI", 9), bg=THEME["bg"],
                 fg=THEME["text_dim"]).pack(side=tk.LEFT, padx=(8, 0), pady=(8, 0))

    # ─── 状态面板 ────────────────────────────────────────────

    def _build_status_panel(self):
        panel = tk.Frame(self.root, bg=THEME["card"],
                         highlightbackground=THEME["border"], highlightthickness=1)
        panel.grid(row=2, column=0, sticky="ew", padx=10, pady=(0, 6))

        self._status_cards = {}

        # ── 第1行：性能类（CPU / GPU / 显存 / 内存）温度已合并进 CPU、GPU 卡片 ──
        row1 = tk.Frame(panel, bg=THEME["card"])
        row1.pack(fill=tk.X, padx=6, pady=(3, 1))

        perf_cards = [
            ("cpu", "🖥️ CPU", "0%"),
            ("gpu", "🎮 GPU", "N/A"),
            ("gpu_mem", "🎮 显存", "N/A"),
            ("memory", "🧠 内存", "0/0 GB"),
        ]
        for key, icon, default in perf_cards:
            card = tk.Frame(row1, bg=THEME["card_alt"],
                            highlightbackground=THEME["border"],
                            highlightthickness=1, padx=8, pady=4)
            card.pack(side=tk.LEFT, fill=tk.X, expand=True, padx=2)

            tk.Label(card, text=icon, font=("Segoe UI", 10),
                     bg=THEME["card_alt"], fg=THEME["text_dim"]).pack()
            val = tk.Label(card, text=default, font=FONT_CARD,
                           bg=THEME["card_alt"], fg=THEME["text_bright"])
            val.pack()
            self._status_cards[key] = val

            # 温度子标签（仅 CPU 和 GPU 卡片需要）
            if key in ("cpu", "gpu"):
                sub = tk.Label(card, text="", font=FONT_SM,
                               bg=THEME["card_alt"], fg=THEME["text_dim"])
                sub.pack()
                self._status_subs[key] = sub

        # ── 第2行：存储类（各盘符） ──
        self._drive_frame = tk.Frame(panel, bg=THEME["card"])
        self._drive_frame.pack(fill=tk.X, padx=6, pady=(1, 3))
        # 初始显示，后续由 _update_display 动态更新
        self._build_drive_labels(["C", "D", "E", "H", "U"])

    def _build_drive_labels(self, letters: list):
        """动态构建盘符卡片"""
        for w in self._drive_frame.winfo_children():
            w.destroy()
        for letter in letters:
            key = f"drive_{letter}"
            card = tk.Frame(self._drive_frame, bg=THEME["card_alt"],
                            highlightbackground=THEME["border"],
                            highlightthickness=1, padx=8, pady=4)
            card.pack(side=tk.LEFT, fill=tk.X, expand=True, padx=2)

            lbl = tk.Label(card, text=f"💾 {letter}:", font=("Segoe UI", 9),
                           bg=THEME["card_alt"], fg=THEME["text_dim"])
            lbl.pack()
            val = tk.Label(card, text="-- GB", font=("Segoe UI", 13, "bold"),
                           bg=THEME["card_alt"], fg=THEME["text_bright"])
            val.pack()
            self._status_cards[key] = val

    # ─── 一键加速 ─────────────────────────────────────────────

    def _build_turbo_button(self):
        frame = tk.Frame(self.root, bg=THEME["bg"])
        frame.grid(row=3, column=0, sticky="ew", padx=10, pady=(0, 6))

        self.turbo_btn = tk.Button(
            frame, text="⚡ 一 键 加 速  ⚡",
            font=("Segoe UI", 18, "bold"),
            bg=THEME["accent"], fg=THEME["text_bright"],
            activebackground=THEME["accent_hover"],
            activeforeground=THEME["text_bright"],
            relief=tk.FLAT, bd=0, cursor="hand2", padx=40, pady=12,
            command=self._on_turbo_boost,
        )
        self.turbo_btn.pack(fill=tk.X)

    # ─── 优化选项 ─────────────────────────────────────────────

    def _build_options(self):
        frame = tk.LabelFrame(
            self.root, text="⚙️ 优化选项",
            font=FONT_BOLD, bg=THEME["card"], fg=THEME["text"],
            highlightbackground=THEME["border"], highlightthickness=1,
            padx=12, pady=8, relief=tk.FLAT, labelanchor="nw"
        )
        frame.grid(row=4, column=0, sticky="ew", padx=10, pady=(0, 4))

        self.var_clean_memory = tk.BooleanVar(value=True)
        self.var_kill_processes = tk.BooleanVar(value=True)
        self.var_clean_temp = tk.BooleanVar(value=True)
        self.var_power_plan = tk.BooleanVar(value=True)
        self.var_flush_dns = tk.BooleanVar(value=True)

        opts = [
            ("var_clean_memory", "🧹 清理内存"),
            ("var_kill_processes", "🛑 关闭进程"),
            ("var_clean_temp", "🗑️ 清理临时文件"),
            ("var_power_plan", "⚡ 高性能电源"),
            ("var_flush_dns", "🌐 清除DNS缓存"),
        ]
        for i, (attr, text) in enumerate(opts):
            cb = tk.Checkbutton(
                frame, text=text, variable=getattr(self, attr),
                font=FONT, bg=THEME["card"], fg=THEME["text"],
                selectcolor=THEME["card"], activebackground=THEME["card"],
                activeforeground=THEME["text"]
            )
            cb.grid(row=0, column=i, sticky=tk.W, padx=5, pady=2)

    # ─── 中间区域（分类按钮条 + 大进程列表）──────────────────

    def _build_middle_section(self):
        container = tk.Frame(self.root, bg=THEME["bg"])
        container.grid(row=5, column=0, sticky="nsew", padx=10, pady=(0, 4))
        container.grid_rowconfigure(1, weight=1)
        container.grid_columnconfigure(0, weight=1)

        # ── 顶部：分类按钮条 ──
        cat_bar = tk.Frame(container, bg=THEME["card"],
                           highlightbackground=THEME["border"],
                           highlightthickness=1)
        cat_bar.grid(row=0, column=0, sticky="ew", pady=(0, 3))

        inner = tk.Frame(cat_bar, bg=THEME["card"])
        inner.pack(fill=tk.X, padx=8, pady=4)

        self.cat_btns = {}
        categories = [
            ("browsers", "🌐 浏览器"), ("chat", "💬 聊天"),
            ("download_tools", "📥 下载"), ("music", "🎵 音乐"),
            ("office", "📄 办公"), ("dev_tools", "💻 开发"),
            ("cloud_sync", "☁️ 云同步"),
            ("game_processes", "🎮 游戏"),   # 默认不杀
        ]
        for key, label in categories:
            var = tk.BooleanVar(value=(key != "game_processes"))
            self.cat_btns[key] = var
            tk.Checkbutton(inner, text=label, variable=var,
                           font=("Segoe UI", 9), bg=THEME["card"],
                           fg=THEME["text"], selectcolor=THEME["card"],
                           activebackground=THEME["card"],
                           activeforeground=THEME["text"]
                           ).pack(side=tk.LEFT, padx=3)

        # 右侧操作按钮
        for text, cmd in [("🗑️ 关闭勾选类别", self._on_kill_by_category),
                          ("✅ 全选", self._select_all),
                          ("⬜ 反选", self._invert_selection),
                          ("🔄 刷新", self._refresh_processes)]:
            tk.Button(inner, text=text, font=("Segoe UI", 9),
                      bg=THEME["card_alt"], fg=THEME["text"],
                      activebackground=THEME["border"],
                      relief=tk.FLAT, bd=0, cursor="hand2",
                      padx=8, pady=2, command=cmd
                      ).pack(side=tk.RIGHT, padx=2)

        # ── 进程列表（大尺寸） ──
        list_frame = tk.Frame(container, bg=THEME["card"],
                              highlightbackground=THEME["border"],
                              highlightthickness=1)
        list_frame.grid(row=1, column=0, sticky="nsew")
        list_frame.grid_rowconfigure(1, weight=1)
        list_frame.grid_columnconfigure(0, weight=1)

        # 计数标签（嵌入列表框架顶部）
        self.proc_count_label = tk.Label(list_frame,
                                         text="进程: 加载中...",
                                         font=FONT_SM, bg=THEME["card"],
                                         fg=THEME["text_dim"])
        self.proc_count_label.grid(row=0, column=0, sticky=tk.W,
                                    padx=8, pady=(4, 0))
        # 不额外加 toolbar，按钮已经在上方分类条

        # Treeview 容器
        tv_container = tk.Frame(list_frame, bg=THEME["bg"],
                                highlightbackground=THEME["border"],
                                highlightthickness=1)
        tv_container.grid(row=1, column=0, sticky="nsew", padx=6, pady=(2, 6))
        tv_container.grid_rowconfigure(0, weight=1)
        tv_container.grid_columnconfigure(0, weight=1)

        style = ttk.Style()
        style.theme_use("default")
        style.configure("Treeview",
                        background=THEME["bg"],
                        foreground=THEME["text"],
                        fieldbackground=THEME["bg"],
                        borderwidth=0, rowheight=24, font=FONT_SM)
        style.configure("Treeview.Heading",
                        background=THEME["card"],
                        foreground=THEME["text"],
                        borderwidth=0, font=FONT_BOLD)
        style.map("Treeview",
                  background=[("selected", THEME["blue"])],
                  foreground=[("selected", THEME["text_bright"])])

        self.tree = ttk.Treeview(
            tv_container,
            columns=("check", "icon", "name", "category", "memory"),
            show="headings", height=12, selectmode="none",
        )
        self.tree.grid(row=0, column=0, sticky="nsew")

        for col, w, anc in [("check", 30, tk.CENTER),
                            ("icon", 30, tk.CENTER),
                            ("name", 240, tk.W),
                            ("category", 90, tk.W),
                            ("memory", 80, tk.E)]:
            self.tree.heading(col, text=col.capitalize() if col != "icon" else "  ")
            self.tree.column(col, width=w, anchor=anc)

        scrollbar = ttk.Scrollbar(tv_container, orient=tk.VERTICAL,
                                  command=self.tree.yview)
        scrollbar.grid(row=0, column=1, sticky="ns")
        self.tree.configure(yscrollcommand=scrollbar.set)

        self.tree.bind("<ButtonRelease-1>", self._on_tree_click)
        self._refresh_processes()

    # ─── 日志区域 ─────────────────────────────────────────────

    def _build_log(self):
        frame = tk.LabelFrame(
            self.root, text="📋 日志",
            font=FONT_BOLD, bg=THEME["card"], fg=THEME["text"],
            highlightbackground=THEME["border"], highlightthickness=1,
            padx=6, pady=4, relief=tk.FLAT, labelanchor="nw"
        )
        frame.grid(row=6, column=0, sticky="ew", padx=10, pady=(0, 4))

        self.log_area = scrolledtext.ScrolledText(
            frame, font=("Consolas", 9),
            bg="#0a0d14", fg=THEME["text_dim"],
            insertbackground=THEME["text"],
            height=5, bd=0, relief=tk.FLAT, state=tk.DISABLED
        )
        self.log_area.pack(fill=tk.X, expand=False)
        for tag, color in [("info", THEME["text_dim"]),
                           ("success", THEME["success"]),
                           ("warning", THEME["warning"]),
                           ("error", THEME["error"]),
                           ("step", THEME["info"])]:
            self.log_area.tag_config(tag, foreground=color)

    # ─── 底部按钮 ─────────────────────────────────────────────

    def _build_footer(self):
        frame = tk.Frame(self.root, bg=THEME["bg"])
        frame.grid(row=7, column=0, sticky="ew", padx=10, pady=(0, 8))

        self.run_btn = tk.Button(
            frame, text="🚀 执行勾选优化", font=FONT_BOLD,
            bg=THEME["green"], fg=THEME["text_bright"],
            activebackground=THEME["green_hover"],
            relief=tk.FLAT, bd=0, cursor="hand2", padx=16, pady=6,
            command=self._on_run_selected
        )
        self.run_btn.pack(side=tk.LEFT)

        btn_data = [
            ("🔄 恢复电源", self._on_restore_power, THEME["text_dim"]),
            ("📋 白名单", self._on_manage_whitelist, THEME["info"]),
        ]
        if not is_admin():
            btn_data.append(("🔑 管理员运行", self._on_run_as_admin, THEME["warning"]))

        for text, cmd, fg in btn_data:
            tk.Button(frame, text=text, font=FONT,
                      bg=THEME["card_alt"], fg=fg,
                      activebackground=THEME["border"],
                      relief=tk.FLAT, bd=0, cursor="hand2",
                      padx=12, pady=6, command=cmd
                      ).pack(side=tk.RIGHT, padx=4)

        tk.Label(frame, text="© HEAOZIE", font=("Segoe UI", 8),
                 bg=THEME["bg"], fg=THEME["text_dim"]
                 ).pack(side=tk.RIGHT, padx=(0, 4), pady=(4, 0))

    # ─── 按类别关闭进程 ──────────────────────────────────────

    def _on_kill_by_category(self):
        """根据勾选的类别，全选/取消该类别的进程"""
        if not self.process_data:
            return

        # 获取勾选的类别
        active_cats = {k for k, v in self.cat_btns.items() if v.get()}

        changed_any = False
        for proc in self.process_data:
            pid = proc["pid"]
            cat = proc["category"]
            if pid in self.process_vars:
                var = self.process_vars[pid]
                if cat in active_cats:
                    var.set(True)
                else:
                    var.set(False)
                changed_any = True

        if changed_any:
            self._update_check_display()
            cnt = sum(1 for p in self.process_data if p["pid"] in self.process_vars and self.process_vars[p["pid"]].get())
            self.log(f"📂 按类别筛选完成：已勾选 {cnt} 个进程", "info")

    # ─── 进程列表操作 ─────────────────────────────────────────

    def _refresh_processes(self):
        self.process_data = get_categorized_processes(self.config)
        self.process_vars.clear()
        self.tree.delete(*self.tree.get_children())

        for proc in self.process_data:
            pid = proc["pid"]
            var = tk.BooleanVar(value=True)
            self.process_vars[pid] = var
            mem = f"{proc['memory_mb']:.1f} MB" if proc['memory_mb'] < 1000 else f"{proc['memory_mb']/1024:.1f} GB"
            self.tree.insert("", tk.END, iid=str(pid),
                             values=("☑", proc["icon"], proc["name"],
                                     proc["category_label"], mem))

        self.proc_count_label.config(text=f"进程: {len(self.process_data)} 项")

    def _select_all(self):
        for var in self.process_vars.values():
            var.set(True)
        self._update_check_display()

    def _invert_selection(self):
        for var in self.process_vars.values():
            var.set(not var.get())
        self._update_check_display()

    def _update_check_display(self):
        for pid, var in self.process_vars.items():
            if not self.tree.exists(str(pid)):
                continue
            checked = "☑" if var.get() else "☐"
            vals = list(self.tree.item(str(pid), "values"))
            vals[0] = checked
            self.tree.item(str(pid), values=vals)

    def _on_tree_click(self, event):
        region = self.tree.identify("region", event.x, event.y)
        if region in ("cell", "tree"):
            item = self.tree.identify_row(event.y)
            if item:
                pid = int(item)
                if pid in self.process_vars:
                    var = self.process_vars[pid]
                    var.set(not var.get())
                    checked = "☑" if var.get() else "☐"
                    vals = list(self.tree.item(item, "values"))
                    vals[0] = checked
                    self.tree.item(item, values=vals)

    def _get_selected_pids(self) -> list:
        return [pid for pid, var in self.process_vars.items() if var.get()]

    # ─── 系统监控 ─────────────────────────────────────────────

    def _start_monitor(self):
        def update():
            first = True
            while self.monitor_active:
                try:
                    stats = get_system_stats()
                    gpu = get_gpu_stats()
                    drives = get_all_drives()
                    cpu_temp = get_cpu_temperature()
                    self.root.after(0, lambda s=stats, g=gpu, d=drives, t=cpu_temp, f=first:
                                    self._update_display(s, g, d, t, f))
                    first = False
                except:
                    pass
                time.sleep(2 if not first else 1)
        threading.Thread(target=update, daemon=True).start()

    def _update_display(self, stats: dict, gpu: dict, drives: dict, cpu_temp: float | None = None, rebuild_drives: bool = False):
        try:
            # ── CPU（使用率 + 温度） ──
            cpu = stats["cpu_percent"]
            self._status_cards["cpu"].config(
                text=f"{cpu:.0f}%",
                fg=THEME["success"] if cpu < 50 else THEME["warning"] if cpu < 80 else THEME["error"])
            # CPU 温度子标签
            if cpu_temp is not None:
                self._status_subs.get("cpu", tk.Label()).config(
                    text=f"🌡️ {cpu_temp:.1f}°C",
                    fg=THEME["warning"] if cpu_temp > 75 else THEME["text_dim"])
            else:
                self._status_subs.get("cpu", tk.Label()).config(text="")

            # ── 内存 ──
            mem = stats["memory_percent"]
            self._status_cards["memory"].config(
                text=f"{stats['memory_used_gb']:.1f}/{stats['memory_total_gb']:.0f} GB",
                fg=THEME["success"] if mem < 60 else THEME["warning"] if mem < 80 else THEME["error"])

            # ── GPU（使用率 + 温度已合并） ──
            if gpu.get("available"):
                gp = gpu['gpu_percent']
                self._status_cards["gpu"].config(
                    text=f"{gp}%",
                    fg=THEME["success"] if gp < 50 else THEME["warning"] if gp < 80 else THEME["error"])
                self._status_cards["gpu_mem"].config(
                    text=f"{gpu['memory_used_gb']}/{gpu['memory_total_gb']} GB")
                gt = gpu['temperature']
                self._status_subs.get("gpu", tk.Label()).config(
                    text=f"🌡️ {gt}°C",
                    fg=THEME["warning"] if gt > 75 else THEME["text_dim"])
            else:
                for k in ("gpu", "gpu_mem"):
                    self._status_cards[k].config(text="N/A")
                self._status_subs.get("gpu", tk.Label()).config(text="")

            # 盘符——首次或盘符数量变化时重建
            if rebuild_drives or not hasattr(self, '_last_drives') or self._last_drives != set(drives.keys()):
                self._last_drives = set(drives.keys())
                self._build_drive_labels(sorted(drives.keys()))

            for letter, info in drives.items():
                key = f"drive_{letter}"
                if key in self._status_cards:
                    pct = info["percent"]
                    self._status_cards[key].config(
                        text=f"{info['free_gb']} GB",
                        fg=THEME["success"] if pct > 25 else
                           THEME["warning"] if pct > 10 else THEME["error"])
        except:
            pass

    # ─── 日志 ─────────────────────────────────────────────────

    def log(self, msg: str, tag: str = "info"):
        def _write():
            self.log_area.config(state=tk.NORMAL)
            self.log_area.insert(tk.END, msg + "\n", tag)
            self.log_area.see(tk.END)
            self.log_area.config(state=tk.DISABLED)
        self.root.after(0, _write)

    def log_clear(self):
        self.log_area.config(state=tk.NORMAL)
        self.log_area.delete("1.0", tk.END)
        self.log_area.config(state=tk.DISABLED)

    # ─── 执行勾选优化 ─────────────────────────────────────────

    def _on_run_selected(self):
        if self.running:
            return
        self.log_clear()

        target_pids = self._get_selected_pids()
        options = {
            "clean_memory": self.var_clean_memory.get(),
            "kill_processes": self.var_kill_processes.get(),
            "target_pids": target_pids if self.var_kill_processes.get() else None,
            "clean_temp": self.var_clean_temp.get(),
            "power_plan": self.var_power_plan.get(),
            "flush_dns": self.var_flush_dns.get(),
        }

        if not any(v for k, v in options.items() if k not in ("target_pids",)):
            messagebox.showinfo("提示", "请至少勾选一项优化")
            return

        self._start_optimization(options)

    # ─── 一键加速 ─────────────────────────────────────────────

    def _on_turbo_boost(self):
        if self.running:
            return
        self.log_clear()
        self.log("⚡ 一键加速启动（全项优化）...", "step")

        self._select_all()
        target_pids = self._get_selected_pids()

        for attr in ("var_clean_memory", "var_kill_processes", "var_clean_temp",
                     "var_power_plan", "var_flush_dns"):
            getattr(self, attr).set(True)

        options = {
            "clean_memory": True,
            "kill_processes": True,
            "target_pids": target_pids,
            "clean_temp": True,
            "power_plan": True,
            "flush_dns": True,
        }
        self._start_optimization(options, turbo=True)

    # ─── 统一优化启动 ─────────────────────────────────────────

    def _start_optimization(self, options: dict, turbo: bool = False):
        self.running = True
        self.turbo_btn.config(text="⚡ 优化中...", state=tk.DISABLED)
        self.run_btn.config(text="⏳ 优化中...", state=tk.DISABLED)
        self.log("🚀 开始执行优化...", "step")

        def progress(msg):
            self.log(msg)

        def run():
            try:
                results = run_all_optimizations(
                    self.config, options, progress, None
                )
                self.root.after(0, lambda: self._on_done(results))
            except Exception as e:
                self.root.after(0, lambda: self.log(f"❌ 出错: {e}", "error"))
            finally:
                self.running = False
                self.root.after(0, lambda: self.turbo_btn.config(
                    text="⚡ 一 键 加 速  ⚡", state=tk.NORMAL))
                self.root.after(0, lambda: self.run_btn.config(
                    text="🚀 执行勾选优化", state=tk.NORMAL))

        threading.Thread(target=run, daemon=True).start()

    # ─── 完成回调 ─────────────────────────────────────────────

    def _on_done(self, results: dict):
        total = results.get("total_freed_mb", 0)
        self.log("")
        self.log(f"🎉 优化完成！共释放约 {total:.0f} MB 内存", "success")

        for key, label in [("processes", "🛑 结束进程"),
                           ("memory", "🧹 内存清理"),
                           ("temp", "🗑️ 临时文件")]:
            if results.get(key):
                self.log(f"  {label} +{results[key]['freed_mb']:.0f} MB")

        if results.get("power") and results["power"]["success"]:
            self.log("  ⚡ 已切换高性能电源", "success")
        if results.get("dns") and results["dns"]["success"]:
            self.log("  🌐 DNS 已刷新", "success")

        msg = f"✅ 优化完成！\n\n释放内存约 {total:.0f} MB"
        if results.get("processes"):
            msg += f"\n🛑 结束 {results['processes']['killed']} 个进程"
        if results.get("power") and results["power"]["success"]:
            msg += "\n⚡ 已切换高性能电源"
        messagebox.showinfo("HEAOZIE 加速器", msg)

    # ─── 其他回调 ─────────────────────────────────────────────

    def _on_restore_power(self):
        def run():
            self.log("🔄 恢复平衡电源...", "step")
            res = restore_balanced_power_plan(self.log)
            if res["success"]:
                messagebox.showinfo("完成", "已恢复为平衡电源计划")
            else:
                messagebox.showwarning("提示", res["message"])
        threading.Thread(target=run, daemon=True).start()

    def _on_run_as_admin(self):
        if messagebox.askyesno("确认", "以管理员权限重新启动？\n（会弹出 UAC 确认）"):
            run_as_admin()

    def _on_manage_whitelist(self):
        """打开白名单管理对话框"""
        win = tk.Toplevel(self.root)
        win.title("用户白名单管理")
        win.geometry("500x420")
        win.configure(bg=THEME["bg"])
        win.resizable(False, False)
        win.transient(self.root)
        win.grab_set()

        title = tk.Label(win, text="📋 用户自定义白名单", font=FONT_BOLD,
                         bg=THEME["bg"], fg=THEME["text"])
        title.pack(pady=(14, 4))

        tip = tk.Label(win, text="白名单中的进程名不会被显示和关闭（每行一个，如 notepad.exe）",
                       font=("Segoe UI", 9), bg=THEME["bg"], fg=THEME["text_dim"])
        tip.pack(pady=(0, 8))

        # 主内容区
        main = tk.Frame(win, bg=THEME["bg"])
        main.pack(fill=tk.BOTH, expand=True, padx=14)

        # 列表 + 控制按钮（水平布局）
        list_frame = tk.Frame(main, bg=THEME["card"],
                              highlightbackground=THEME["border"],
                              highlightthickness=1)
        list_frame.pack(side=tk.LEFT, fill=tk.BOTH, expand=True)

        tk.Label(list_frame, text="  当前白名单", font=("Segoe UI", 9, "bold"),
                 bg=THEME["card"], fg=THEME["text_dim"]).pack(anchor=tk.W, padx=6, pady=(4, 0))

        lb = tk.Listbox(list_frame, font=("Consolas", 10),
                        bg=THEME["bg"], fg=THEME["text"],
                        selectbackground=THEME["blue"],
                        selectforeground=THEME["text_bright"],
                        bd=0, highlightthickness=0)
        lb.pack(fill=tk.BOTH, expand=True, padx=4, pady=4)

        # 控制按钮区
        ctrl = tk.Frame(main, bg=THEME["bg"])
        ctrl.pack(side=tk.RIGHT, fill=tk.Y, padx=(8, 0))

        def refresh_list():
            lb.delete(0, tk.END)
            whitelist = self.config.get("user_whitelist", [])
            for item in whitelist:
                lb.insert(tk.END, item)

        def add_entry():
            entry = add_entry_box.get().strip()
            if not entry:
                return
            whitelist = self.config.get("user_whitelist", [])
            if entry.lower() not in [w.lower() for w in whitelist]:
                whitelist.append(entry)
                self.config["user_whitelist"] = whitelist
                add_entry_box.delete(0, tk.END)
                refresh_list()
            else:
                messagebox.showinfo("提示", f"'{entry}' 已在白名单中", parent=win)

        def remove_selected():
            sel = lb.curselection()
            if not sel:
                return
            idx = sel[0]
            whitelist = self.config.get("user_whitelist", [])
            if idx < len(whitelist):
                removed = whitelist.pop(idx)
                self.config["user_whitelist"] = whitelist
                refresh_list()

        def save_and_close():
            save_config(self.config)
            self.log(f"📋 白名单已更新（{len(self.config.get('user_whitelist', []))} 条）", "info")
            self._refresh_processes()
            win.destroy()

        # 添加框 + 按钮
        add_frame = tk.Frame(ctrl, bg=THEME["bg"])
        add_frame.pack(pady=(0, 6))

        add_entry_box = tk.Entry(add_frame, font=("Consolas", 10),
                                  bg=THEME["card"], fg=THEME["text"],
                                  insertbackground=THEME["text"],
                                  bd=0, width=16, relief=tk.FLAT)
        add_entry_box.pack(pady=(0, 4), ipadx=4, ipady=4)
        add_entry_box.insert(0, "进程名.exe")
        add_entry_box.bind("<FocusIn>", lambda e: add_entry_box.delete(0, tk.END) if add_entry_box.get() == "进程名.exe" else None)

        tk.Button(add_frame, text="➕ 添加", font=("Segoe UI", 9),
                  bg=THEME["green"], fg=THEME["text_bright"],
                  activebackground=THEME["green_hover"],
                  relief=tk.FLAT, bd=0, cursor="hand2",
                  padx=10, pady=4, command=add_entry
                  ).pack(pady=(2, 4))

        tk.Button(add_frame, text="➖ 移除选中", font=("Segoe UI", 9),
                  bg=THEME["accent"], fg=THEME["text_bright"],
                  activebackground=THEME["accent_hover"],
                  relief=tk.FLAT, bd=0, cursor="hand2",
                  padx=10, pady=4, command=remove_selected
                  ).pack(pady=(0, 6))

        # 底栏
        bot = tk.Frame(win, bg=THEME["bg"])
        bot.pack(fill=tk.X, padx=14, pady=(8, 12))

        tk.Button(bot, text="💾 保存并刷新", font=FONT_BOLD,
                  bg=THEME["green"], fg=THEME["text_bright"],
                  activebackground=THEME["green_hover"],
                  relief=tk.FLAT, bd=0, cursor="hand2",
                  padx=20, pady=6, command=save_and_close
                  ).pack(side=tk.LEFT)

        tk.Button(bot, text="取消", font=FONT,
                  bg=THEME["card_alt"], fg=THEME["text_dim"],
                  activebackground=THEME["border"],
                  relief=tk.FLAT, bd=0, cursor="hand2",
                  padx=16, pady=6, command=win.destroy
                  ).pack(side=tk.RIGHT)

        refresh_list()

    def on_close(self):
        self.monitor_active = False
        self.root.destroy()


# ─── 入口 ────────────────────────────────────────────────────

def main():
    root = tk.Tk()
    app = GamingBoosterApp(root)
    root.protocol("WM_DELETE_WINDOW", app.on_close)
    root.mainloop()


if __name__ == "__main__":
    main()
