# legacy/ —— Python 原件留档（不再是入口）

这里放的是重构前的三个 Python/tkinter 小工具。**它们已经不再被使用**，保留的原因只有一个：
将来要核对某个行为原来是怎么做的时，有原始实现可查（例如进程分类名单、电源操作参数）。

| 目录 | 原入口 | 现在去哪了 |
| --- | --- | --- |
| `gaming_booster/` | `run_booster.bat` | HZ.exe →「游戏加速」页 |
| `定时休眠/` | `定时睡眠.bat` | HZ.exe →「定时电源」页 |
| `定时关闭应用/` | `定时关闭应用.bat` | HZ.exe →「定时关闭应用」页 |

## 为什么不再用它们

1. **两个工具在本机根本跑不起来**：`app_killer.py` 与 `gaming_booster.py` 都 `import psutil`，
   而本机三个 Python 解释器都没装 psutil → 实测直接 `ModuleNotFoundError`；
2. **每次都要弹一个 cmd 控制台**（`.bat` 里 `start /b py -3 xxx.py`）；
3. **三个工具 = 三个 Python 进程 + 三份重复的主题代码**，而 HZ.exe 是一个进程、约 9 MB（托盘态）。

## 已知缺陷（重构时修掉的，留档备查）

- `sleep_timer.py` / `app_killer.py` 用**递减计数**：机器睡眠/休眠后计时停摆，唤醒后比预期晚；
- `booster_core.py` 的 `clean_memory()` 里那段"清理 Standby Memory"是两条**无效的**
  PowerShell 咒语（`EventLogSession.ClearLog('')`、`ReleaseComObject(GetActiveObject(''))`），
  实际什么都没清；
- `gaming_booster/config.json` 把 `steam.exe` 等同时写进 `game_processes`（可杀）与
  `never_kill`（不杀），自相矛盾；
- 电源计划"恢复平衡"写死 Balanced GUID，而不是恢复切换前的那一个。

## 如果要重新跑它们

```bat
cd legacy\定时休眠 && py -3 sleep_timer.py
```

（需要 Python 3；`gaming_booster` 与 `定时关闭应用` 还需要 `pip install psutil`。）
