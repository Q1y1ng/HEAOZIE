"""
游戏性能加速器 — 核心优化逻辑
"""
import os
import sys
import json
import psutil
import time
import subprocess
import ctypes
import shutil
from pathlib import Path


# ─── 管理员权限检测 ──────────────────────────────────────────────

def is_admin() -> bool:
    try:
        return ctypes.windll.shell32.IsUserAnAdmin() != 0
    except:
        return False


def run_as_admin(script_args: str = "") -> None:
    """以管理员权限重新启动当前脚本（会弹出 UAC 确认框）"""
    ctypes.windll.shell32.ShellExecuteW(
        None, "runas", sys.executable, f'"{sys.argv[0]}" {script_args}', None, 1
    )


# ─── GPU 状态 ──────────────────────────────────────────────────

def get_gpu_stats() -> dict:
    """通过 nvidia-smi 获取 GPU 信息"""
    result = {
        "available": False,
        "name": "",
        "gpu_percent": 0,
        "memory_used_gb": 0,
        "memory_total_gb": 0,
        "temperature": 0,
    }
    try:
        r = subprocess.run(
            'nvidia-smi --query-gpu=utilization.gpu,memory.used,memory.total,temperature.gpu,name --format=csv,noheader',
            shell=True, capture_output=True, text=True, timeout=5
        )
        if r.returncode == 0 and r.stdout.strip():
            parts = [p.strip() for p in r.stdout.strip().split(", ")]
            if len(parts) >= 5:
                result["available"] = True
                result["gpu_percent"] = int(parts[0].replace(" %", ""))
                result["memory_used_gb"] = round(int(parts[1].replace(" MiB", "")) / 1024, 1)
                result["memory_total_gb"] = round(int(parts[2].replace(" MiB", "")) / 1024, 1)
                result["temperature"] = int(parts[3].replace(" °C", ""))
                result["name"] = parts[4]
    except:
        pass
    return result


def get_cpu_temperature() -> float | None:
    """通过 WMI 获取 CPU 温度（°C），失败返回 None"""
    try:
        r = subprocess.run(
            'wmic /namespace:\\\\root\\wmi PATH MSAcpi_ThermalZoneTemperature get CurrentTemperature /value 2>nul',
            shell=True, capture_output=True, text=True, timeout=5
        )
        if r.returncode == 0:
            for line in r.stdout.strip().splitlines():
                line = line.strip()
                if line.startswith("CurrentTemperature="):
                    val = line.split("=", 1)[1].strip()
                    celsius = (float(val) / 10) - 273.15
                    if 0 < celsius < 120:  # 合理性校验
                        return round(celsius, 1)
    except:
        pass
    return None


# ─── 系统状态 ──────────────────────────────────────────────────

def get_system_stats() -> dict:
    """获取当前系统状态"""
    mem = psutil.virtual_memory()
    cpu_percent = psutil.cpu_percent(interval=0.3)
    process_count = len(psutil.pids())
    disk = shutil.disk_usage("C:/")

    return {
        "cpu_percent": cpu_percent,
        "memory_percent": mem.percent,
        "memory_used_gb": mem.used / (1024**3),
        "memory_total_gb": mem.total / (1024**3),
        "memory_available_gb": mem.available / (1024**3),
        "process_count": process_count,
        "disk_free_gb": disk.free / (1024**3),
    }


def get_all_drives() -> dict:
    """获取所有可用盘符的剩余空间"""
    drives = {}
    for letter in "CDEFGHIJKLMNOPQRSTUVWXYZ":
        path = f"{letter}:/"
        if os.path.exists(path):
            try:
                t, u, f = shutil.disk_usage(path)
                drives[letter] = {
                    "total_gb": round(t / (2**30), 1),
                    "used_gb": round(u / (2**30), 1),
                    "free_gb": round(f / (2**30), 1),
                    "percent": round(f * 100 / t),
                }
            except:
                pass
    return drives


# ─── 配置加载 ──────────────────────────────────────────────────

DEFAULT_CONFIG = {
    "kill_processes": {
        "browsers": True,
        "chat": True,
        "download_tools": True,
        "dev_tools": False,
        "music": True,
        "office": True,
        "cloud_sync": True,
        "game_processes": False,      # 游戏平台 — 默认不杀
    },
    "browser_processes": [
        "chrome.exe", "msedge.exe", "firefox.exe", "opera.exe",
        "brave.exe", "vivaldi.exe", "360chrome.exe", "qqbrowser.exe",
        "sogouexplorer.exe", "liebao.exe", "centbrowser.exe",
    ],
    "chat_processes": [
        "wechat.exe", "wx.exe", "qq.exe", "tim.exe", "dingtalk.exe",
        "lark.exe", "feishu.exe", "slack.exe", "discord.exe",
        "telegram.exe", "whatsapp.exe",
    ],
    "download_tools": [
        "xunlei.exe", "thunder.exe", "qbittorrent.exe", "utorrent.exe",
        "aria2c.exe", "idman.exe", "internetdownloadmanager.exe",
        "motrix.exe", "eagleget.exe",
    ],
    "dev_tools": [
        "code.exe", "cursor.exe", "idea64.exe", "pycharm64.exe",
        "clion64.exe", "webstorm64.exe", "goland64.exe",
        "sublime_text.exe", "notepad++.exe", "winscp.exe",
        "putty.exe", "docker desktop.exe",
    ],
    "music_processes": [
        "cloudmusic.exe", "qqmusic.exe", "kugou.exe", "kuwo.exe",
        "spotify.exe", "foobar2000.exe", "neteasemusic.exe",
    ],
    "office_processes": [
        "winword.exe", "excel.exe", "powerpnt.exe", "outlook.exe",
        "wps.exe", "wpsoffice.exe", "et.exe", "wpp.exe",
    ],
    "cloud_sync_processes": [
        "onedrive.exe", "baidunetdisk.exe", "baidupan.exe",
        "115pan.exe", "123pan.exe", "yundetectservice.exe",
        "dropbox.exe",
    ],
    "game_processes": [
        "steam.exe", "steamwebhelper.exe", "epicgameslauncher.exe",
        "battle.net.exe", "galaxyclient.exe", "ubisoftconnect.exe",
        "eadesktop.exe", "ea.exe", "origin.exe", "epicclientlauncher.exe",
        "xboxapp.exe", "gamingservices.exe", "gamingservicesnet.exe",
        "r5apex.exe", "discord.exe",
    ],
    "never_kill": [
        # ─── Windows 系统核心进程 ───
        "system", "system idle process", "system32",
        "svchost.exe", "csrss.exe", "wininit.exe",
        "services.exe", "lsass.exe", "lsaiso.exe",
        "winlogon.exe", "smss.exe", "winlogon.exe",
        # ─── 桌面与UI ───
        "explorer.exe", "taskmgr.exe", "conhost.exe",
        "dwm.exe", "fontdrvhost.exe", "sihost.exe",
        "runtimebroker.exe", "taskhostw.exe",
        "ctfmon.exe", "securityhealthservice.exe",
        "sppsvc.exe", "wlms.exe",
        # ─── 音频与输入 ───
        "audiodg.exe", "audiosrv.exe",
        # ─── 打印机与存储 ───
        "spoolsv.exe", "searchindexer.exe",
        # ─── 杀毒软件 ───
        "msmpeng.exe", "microsoft defender.exe",
        "defender.exe", "antimalware.exe",
        "msascuil.exe", "msascui.exe", "mpcmdrun.exe",
        "nissrv.exe", "mssense.exe",
        "windowsdefender.exe",
        # ─── NVIDIA 驱动完整套件 ───
        "nvidia container.exe", "nvdisplay.container.exe",
        "nvcontainer.exe", "nvtoolsrv.exe",
        "nvxdsync.exe", "nvnlist.exe",
        "nvtelemetrycontainer.exe", "nvidia share.exe",
        "nvidia web helper.exe", "nvbackend.exe",
        "nvgpucomp64.exe", "nvidia driver helper.exe",
        "nscq.exe", "nvspcap64.exe",
        # ─── 壁纸软件 ───
        "wallpaper32.exe", "wallpaper64.exe",
        "wallpaper engine.exe",
        # ─── 游戏平台辅助（不要自动杀） ───
        "steam.exe", "steamwebhelper.exe", "steamclient64.exe",
        "steamservice.exe", "steamerrorreporter64.exe",
        "galaxyclient.exe", "eadesktop.exe",
        "epicgameslauncher.exe", "epicclientlauncher.exe",
        # ─── 系统更新 ───
        "microsoftedgeupdate.exe", "microsoftedgeupdatelocal.exe",
        "wuauclt.exe", "wuauserv.exe", "trustedinstaller.exe",
        "tiworker.exe",
    ],
    "user_whitelist": [
        # 用户自定义不杀进程（通过 UI 管理）
    ],
}


def load_config() -> dict:
    config_path = Path(__file__).parent / "config.json"
    if config_path.exists():
        try:
            with open(config_path, "r", encoding="utf-8") as f:
                user_cfg = json.load(f)
            merged = DEFAULT_CONFIG.copy()
            merged.update(user_cfg)
            # 确保嵌套字典深度合并 — 保留 DEFAULT 新增的键（如 game_processes）
            for key in DEFAULT_CONFIG:
                if isinstance(DEFAULT_CONFIG[key], dict):
                    if key in user_cfg and isinstance(user_cfg[key], dict):
                        base = DEFAULT_CONFIG[key].copy()
                        base.update(user_cfg[key])
                        merged[key] = base
                    elif key not in user_cfg:
                        merged[key] = DEFAULT_CONFIG[key].copy()
            return merged
        except:
            return DEFAULT_CONFIG.copy()
    return DEFAULT_CONFIG.copy()


def save_config(config: dict) -> None:
    config_path = Path(__file__).parent / "config.json"
    with open(config_path, "w", encoding="utf-8") as f:
        json.dump(config, f, ensure_ascii=False, indent=2)


# ─── 进程分类 ──────────────────────────────────────────────────

CATEGORY_MAP = {
    "browsers": "browser_processes",
    "chat": "chat_processes",
    "download_tools": "download_tools",
    "dev_tools": "dev_tools",
    "music": "music_processes",
    "office": "office_processes",
    "cloud_sync": "cloud_sync_processes",
    "game_processes": "game_processes",
}

CATEGORY_ICONS = {
    "browsers": "🌐",
    "chat": "💬",
    "download_tools": "📥",
    "dev_tools": "💻",
    "music": "🎵",
    "office": "📄",
    "cloud_sync": "☁️",
    "game_processes": "🎮",
}

CATEGORY_LABELS = {
    "browsers": "浏览器",
    "chat": "聊天软件",
    "download_tools": "下载工具",
    "dev_tools": "开发工具",
    "music": "音乐播放器",
    "office": "办公软件",
    "cloud_sync": "云同步",
    "game_processes": "游戏平台",
}


def get_categorized_processes(config: dict = None) -> list:
    """扫描进程，按类别分组，返回 [{pid, name, category, category_label, icon, memory_mb}]"""
    if config is None:
        config = load_config()

    # 合并内置白名单 + 用户自定义白名单
    skip_list = [nk.lower() for nk in config.get("never_kill", [])]
    skip_list += [uw.lower() for uw in config.get("user_whitelist", [])]
    # 跳过自身（不要把自己杀了）
    my_pid = os.getpid()
    try:
        my_name = psutil.Process(my_pid).name().lower()
        if my_name not in skip_list:
            skip_list.append(my_name)
    except:
        pass
    # Build reverse lookup: exename -> category
    exe_to_cat = {}
    for cat, cfg_key in CATEGORY_MAP.items():
        for exe in config.get(cfg_key, []):
            exe_to_cat[exe.lower()] = cat

    found = {}
    for proc in psutil.process_iter(["pid", "name", "memory_info"]):
        try:
            pname = proc.info["name"] or ""
            pname_lower = pname.lower()
            if not pname_lower:
                continue
            pid = proc.info["pid"]
            # 跳过自身 PID（即使进程名不同也跳过）
            if pid == my_pid:
                continue
            # 跳过白名单（内置 + 用户自定义 + 自身进程名）
            if any(nk in pname_lower for nk in skip_list):
                continue
            mem_mb = round((proc.info["memory_info"].rss or 0) / (1024**2), 1)
            category = exe_to_cat.get(pname_lower, "unknown")
            icon = CATEGORY_ICONS.get(category, "📦")
            label = CATEGORY_LABELS.get(category, "其他")
            # 去重：同个 exe 只保留内存最大的那个
            key = pname_lower
            if key not in found or mem_mb > found[key]["memory_mb"]:
                found[key] = {
                    "pid": pid,
                    "name": pname,
                    "category": category,
                    "category_label": label,
                    "icon": icon,
                    "memory_mb": mem_mb,
                }
        except:
            pass

    procs = sorted(found.values(), key=lambda x: -x["memory_mb"])
    return procs


# ─── 内存清理 ──────────────────────────────────────────────────

def clean_memory(progress_callback=None) -> dict:
    """清理内存：清空工作集 + 清理 Standby Memory"""
    result = {"freed_mb": 0, "steps": []}

    def log(msg):
        result["steps"].append(msg)
        if progress_callback:
            progress_callback(msg)

    # 1. 遍历进程清空工作集
    log("正在清空各进程工作集...")
    count = 0
    for proc in psutil.process_iter(["pid", "name"]):
        try:
            process = psutil.Process(proc.info["pid"])
            # EmptyWorkingSet via kernel32
            kernel32 = ctypes.windll.kernel32
            handle = kernel32.OpenProcess(0x1F0FFF, False, proc.info["pid"])
            if handle:
                kernel32.EmptyWorkingSet(handle)
                kernel32.CloseHandle(handle)
                count += 1
        except:
            pass
    log(f"已清空 {count} 个进程的工作集")

    # 2. 清理 Standby Memory（需要管理员权限）
    if is_admin():
        log("正在清理 Standby Memory（管理员模式）...")
        try:
            subprocess.run(
                "powershell -Command \"[System.Diagnostics.Eventing.Reader.EventLogSession]::GlobalSession.ClearLog('') ; Write-Host 'Done'\"",
                shell=True, capture_output=True, timeout=10
            )
            # 另一种方式：调用 EmptyWorkingSet on system
            subprocess.run(
                "powershell -Command \"[System.Runtime.InteropServices.Marshal]::ReleaseComObject([System.Runtime.InteropServices.Marshal]::GetActiveObject('') )\"",
                shell=True, capture_output=True, timeout=5
            )
            log("Standby Memory 清理完成")
        except Exception as e:
            log(f"Standby 清理未完成: {e}")
    else:
        log("Standby Memory 需要管理员权限才能清理，已跳过")

    # 3. 测量释放效果
    before = psutil.virtual_memory().available
    time.sleep(1)
    after = psutil.virtual_memory().available
    freed = (after - before) / (1024**2)
    result["freed_mb"] = round(freed, 1)
    log(f"共释放内存: {result['freed_mb']:.1f} MB")
    return result


# ─── 进程管理 ──────────────────────────────────────────────────

def kill_background_processes(config: dict = None, categories: dict = None,
                               target_pids: list = None, progress_callback=None) -> dict:
    """结束后台非必要进程
    Args:
        config: 配置字典
        categories: 按类别过滤 {category: bool}
        target_pids: 指定的 PID 列表（优先于 categories）
        progress_callback: 进度回调
    """
    if config is None:
        config = load_config()
    if categories is None:
        categories = config.get("kill_processes", {})

    # 构建要杀的进程列表
    targets = []
    # 合并内置白名单 + 用户自定义白名单
    skip_list = [nk.lower() for nk in config.get("never_kill", [])]
    skip_list += [uw.lower() for uw in config.get("user_whitelist", [])]
    # 跳过自身（不要把自己杀了）
    my_pid = os.getpid()
    try:
        my_name = psutil.Process(my_pid).name().lower()
        if my_name not in skip_list:
            skip_list.append(my_name)
    except:
        pass

    if target_pids:
        # 按 PID 杀——只检查白名单
        pass
    else:
        # 按类别杀
        for cat, enabled in categories.items():
            if enabled and cat in CATEGORY_MAP:
                targets.extend(config.get(CATEGORY_MAP[cat], []))

    killed = []
    skipped = 0
    total_freed_mb = 0

    def log(msg):
        if progress_callback:
            progress_callback(msg)

    log("🚀 开始结束后台进程...")

    for proc in psutil.process_iter(["pid", "name", "memory_info"]):
        try:
            pname = proc.info["name"].lower() if proc.info["name"] else ""
            pid = proc.info["pid"]
            if not pname:
                continue
            # 跳过自身 PID（不要把自己杀了）
            if pid == my_pid:
                continue
            # 跳过白名单进程（内置 + 用户自定义 + 自身进程名）
            if any(sk in pname for sk in skip_list):
                continue

            # 判断是否在目标列表
            should_kill = False
            if target_pids:
                should_kill = pid in target_pids
            else:
                should_kill = pname in targets

            if should_kill:
                mem_mb = (proc.info["memory_info"].rss or 0) / (1024**2)
                try:
                    proc.terminate()
                    # 等待3秒，如果没结束则强制杀
                    proc.wait(timeout=3)
                    killed.append({"name": proc.info["name"], "pid": proc.info["pid"], "freed_mb": round(mem_mb, 1)})
                    total_freed_mb += mem_mb
                except (psutil.NoSuchProcess, psutil.AccessDenied, psutil.TimeoutExpired):
                    try:
                        proc.kill()
                        killed.append({"name": proc.info["name"], "pid": proc.info["pid"], "freed_mb": round(mem_mb, 1)})
                        total_freed_mb += mem_mb
                    except:
                        skipped += 1
        except:
            skipped += 1

    log(f"✅ 已结束 {len(killed)} 个进程，释放约 {total_freed_mb:.0f} MB 内存")
    if skipped:
        log(f"⏭️ 跳过 {skipped} 个（权限不足）")

    return {
        "killed": len(killed),
        "skipped": skipped,
        "freed_mb": round(total_freed_mb, 1),
        "details": killed[:20],  # 只记录前20个
    }


# ─── 临时文件清理 ──────────────────────────────────────────────

def clean_temp_files(progress_callback=None) -> dict:
    """清理临时文件"""
    result = {"freed_mb": 0, "paths_cleaned": []}

    def log(msg):
        if progress_callback:
            progress_callback(msg)

    def clean_dir(path, label):
        cleaned = 0
        total = 0
        if not os.path.exists(path):
            return 0
        for item in os.listdir(path):
            item_path = os.path.join(path, item)
            try:
                if os.path.isfile(item_path):
                    total += os.path.getsize(item_path)
                    os.remove(item_path)
                    cleaned += 1
                elif os.path.isdir(item_path):
                    try:
                        sz = 0
                        for r, d, fs in os.walk(item_path):
                            for f in fs:
                                try:
                                    sz += os.path.getsize(os.path.join(r, f))
                                except:
                                    pass
                        total += sz
                        shutil.rmtree(item_path)
                        cleaned += 1
                    except:
                        pass
            except:
                pass
        if total > 0:
            result["paths_cleaned"].append({"path": label, "freed_mb": round(total / (1024**2), 1)})
        return total

    log("正在清理临时文件...")

    # 用户 Temp
    user_temp = os.path.expandvars(r"%TEMP%")
    s1 = clean_dir(user_temp, "用户临时文件 (%TEMP%)")
    log(f"  📁 用户 Temp: {s1 / (1024**2):.1f} MB 已清理")

    # Windows Temp（需要管理员权限可以清理更多）
    win_temp = r"C:\Windows\Temp"
    s2 = clean_dir(win_temp, "Windows 临时文件")
    if s2 > 0:
        log(f"  📁 Windows Temp: {s2 / (1024**2):.1f} MB 已清理")

    # Prefetch（需要管理员权限清理该目录下的文件）
    if is_admin():
        prefetch = r"C:\Windows\Prefetch"
        s3 = clean_dir(prefetch, "Prefetch")
        if s3 > 0:
            log(f"  📁 Prefetch: {s3 / (1024**2):.1f} MB 已清理")

    total_freed = sum(p["freed_mb"] for p in result["paths_cleaned"])
    result["freed_mb"] = round(total_freed, 1)
    log(f"✅ 临时文件清理完成，共释放 {total_freed:.1f} MB")
    return result


# ─── 电源计划 ──────────────────────────────────────────────────

def set_high_performance_power_plan(progress_callback=None) -> dict:
    """切换到高性能电源计划"""
    result = {"success": False, "message": ""}

    def log(msg):
        if progress_callback:
            progress_callback(msg)

    log("正在切换到高性能电源计划...")
    try:
        # 获取所有电源计划
        result_get = subprocess.run(
            "powercfg /list",
            shell=True, capture_output=True, text=True, timeout=10
        )
        # 高性能计划的 GUID 通常是 8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c
        # 先尝试直接切换
        r = subprocess.run(
            "powercfg /setactive 8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c",
            shell=True, capture_output=True, text=True, timeout=10
        )
        if r.returncode == 0:
            result["success"] = True
            result["message"] = "已切换到高性能电源计划"
            log("✅ 已切换到高性能电源计划")
        else:
            # 尝试查找高性能计划
            lines = result_get.stdout.splitlines()
            for line in lines:
                if "高性能" in line or "High Performance" in line:
                    guid = line.split()[0] if line.split() else ""
                    if guid.startswith("{"):
                        subprocess.run(
                            f"powercfg /setactive {guid}",
                            shell=True, capture_output=True, timeout=10
                        )
                        result["success"] = True
                        result["message"] = "已切换到高性能电源计划"
                        log("✅ 已切换到高性能电源计划")
                        break
            else:
                result["message"] = "未找到高性能电源计划"
                log("⚠️ 未找到高性能电源计划")
    except Exception as e:
        result["message"] = str(e)
        log(f"⚠️ 切换失败: {e}")

    return result


def restore_balanced_power_plan(progress_callback=None) -> dict:
    """恢复为平衡电源计划"""
    result = {"success": False, "message": ""}

    def log(msg):
        if progress_callback:
            progress_callback(msg)

    log("正在恢复平衡电源计划...")
    try:
        r = subprocess.run(
            "powercfg /setactive 381b4222-f694-41f0-9685-ff5bb260df2e",
            shell=True, capture_output=True, text=True, timeout=10
        )
        if r.returncode == 0:
            result["success"] = True
            result["message"] = "已切换回平衡电源计划"
            log("✅ 已切换回平衡电源计划")
        else:
            result["message"] = "切换失败"
            log("⚠️ 切换失败")
    except Exception as e:
        result["message"] = str(e)
        log(f"⚠️ 恢复失败: {e}")

    return result


# ─── DNS 缓存 ──────────────────────────────────────────────────

def flush_dns(progress_callback=None) -> dict:
    """清空 DNS 缓存"""
    result = {"success": False}

    def log(msg):
        if progress_callback:
            progress_callback(msg)

    log("正在清空 DNS 缓存...")
    try:
        r = subprocess.run(
            "ipconfig /flushdns",
            shell=True, capture_output=True, text=True, timeout=10
        )
        if r.returncode == 0:
            result["success"] = True
            log("✅ DNS 缓存已清空")
        else:
            log("⚠️ DNS 清空失败")
    except Exception as e:
        log(f"⚠️ DNS 清空失败: {e}")

    return result


# ─── 全优化 ──────────────────────────────────────────────────

def run_all_optimizations(
    config: dict = None,
    options: dict = None,
    progress_callback=None,
    step_callback=None,
) -> dict:
    """运行所有勾选的优化项"""
    if options is None:
        options = {
            "clean_memory": True,
            "kill_processes": True,
            "kill_categories": {},
            "clean_temp": True,
            "power_plan": True,
            "flush_dns": True,
        }

    results = {}

    def log(msg):
        if progress_callback:
            progress_callback(msg)

    steps = []

    # 1. 清理内存
    if options.get("clean_memory", False):
        if step_callback:
            step_callback("memory", "running")
        log("=" * 40)
        log("🔄 步骤 1/5：清理内存...")
        results["memory"] = clean_memory(progress_callback)
        steps.append("memory")
        if step_callback:
            step_callback("memory", "done")

    # 2. 结束进程
    if options.get("kill_processes", False):
        if step_callback:
            step_callback("processes", "running")
        log("=" * 40)
        log("🔄 步骤 2/5：结束后台进程...")
        results["processes"] = kill_background_processes(
            config,
            options.get("kill_categories"),
            target_pids=options.get("target_pids"),
            progress_callback=progress_callback,
        )
        steps.append("processes")
        if step_callback:
            step_callback("processes", "done")

    # 3. 清理临时文件
    if options.get("clean_temp", False):
        if step_callback:
            step_callback("temp", "running")
        log("=" * 40)
        log("🔄 步骤 3/5：清理临时文件...")
        results["temp"] = clean_temp_files(progress_callback)
        steps.append("temp")
        if step_callback:
            step_callback("temp", "done")

    # 4. 电源计划
    if options.get("power_plan", False):
        if step_callback:
            step_callback("power", "running")
        log("=" * 40)
        log("🔄 步骤 4/5：设置电源计划...")
        results["power"] = set_high_performance_power_plan(progress_callback)
        steps.append("power")
        if step_callback:
            step_callback("power", "done")

    # 5. DNS 缓存
    if options.get("flush_dns", False):
        if step_callback:
            step_callback("dns", "running")
        log("=" * 40)
        log("🔄 步骤 5/5：清空 DNS 缓存...")
        results["dns"] = flush_dns(progress_callback)
        steps.append("dns")
        if step_callback:
            step_callback("dns", "done")

    # 总计
    total_mb = 0
    for r in results.values():
        if isinstance(r, dict):
            total_mb += r.get("freed_mb", 0)

    log("=" * 40)
    log(f"🎮 优化完成！总计释放内存约 {total_mb:.0f} MB")

    results["total_freed_mb"] = round(total_mb, 1)
    results["steps"] = steps
    return results


# ─── 直接运行测试 ──────────────────────────────────────────────

if __name__ == "__main__":
    def p(msg):
        print(msg)
    print("=== 游戏性能加速器 — 核心模块测试 ===\n")
    print(f"管理员权限: {'是' if is_admin() else '否（建议以管理员运行）'}")
    print(f"系统状态: {get_system_stats()}")
