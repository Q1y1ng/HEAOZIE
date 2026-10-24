# 2048 · Agent 控制与本地大模型对局方案（提案）

> 状态：**已落地**（2026-10-24）——实现、三局结果与 viewer 见 `../2048-agent/README.md`；
> 实际按用户要求以**纯棋盘**为主，并加云端免费档 `beam-free` 对照。
> 目标：`E:\AI\tools\HEAOZIE\2048\index.html`（单文件 2048，4×4–36×36）
> 目标产物：一个能被 agent 稳定控制、可复现、可回放的 2048；让本机 llama.cpp 上的模型实际玩，并产出可对比的成绩报告。

---

## 0. 结论（TL;DR）

1. **不需要先把游戏改成“API 版”也能让 agent 玩**。这个 2048 的逻辑已经以 `window.Game2048` 暴露（`index.html:294-459`），页面本身可以用 Playwright 驱动：读 DOM 得到棋盘、用 `Game2048.move` 预判合法步、用方向键落子。已实测端到端 **约 6 ms/步**（含浏览器往返），相对本地模型每步 0.5–2 s 的延迟可以忽略。
2. **可复现性已经验证**：在页面加载前覆写 `Math.random` 为固定种子 PRNG，并清掉 `localStorage` 旧局，同一 seed 的起始盘一致；同一 seed + 同一策略整局跑两遍，分数/步数/终盘**逐格一致**（实测 7592 分 / 564 步 / 512 块）。
3. **已测出两个基线**（5 个 seed）：随机合法步均分 **1149**（676–1868，最高块 64–128）；一步贪心均分 **4362**（924–7468，最高块 256–512）。本地 9B 如果低于 ~4.4k，就是“不如一步贪心”；这是首轮最直观的判据。
4. 推荐路线：**外部 Playwright Driver（不改游戏）先跑通 → 专用 8096 端口的 4B 模型跑 1 局（约 5–10 分钟）→ 再用 9B/多 seed 做正式对比**。游戏内置 `?agent=1` API 作为二期加固，不是首轮阻塞项。
5. 首轮最大的风险不是代码，而是**本地模型的“思考”开关与显存**：8095 现有 Ornith 配置是 `-rea on` + RP LoRA，直接拿来每步可能吐几百个思考 token（每步 10–45 s）；应另起一个 `-rea off`、小 ctx、无 LoRA 的专用实例。

---

## 1. 现状盘点：这个 2048 已经具备什么

| 事实 | 位置 | 对 agent 的意义 |
| --- | --- | --- |
| 纯逻辑 API：`createBoard / move / canMove / emptyCells / addRandomTile(s) / slideLine / hasReached2048` | `index.html:294-459` | 不用重写规则；合法步/结果可直接在页面里算 |
| 逻辑与 UI 分离，逻辑可被 Node `vm` 提取 | `tests/logic.test.mjs:10-19` | 已有测试基础，后续可做 headless 基线 |
| UI 状态（`grid/score/undoState/busy`）是 IIFE 私有变量 | `index.html:461-892` | 外部只能读 DOM；要拿内部状态需加桥（二期） |
| 键盘入口：方向键/WASD，`isFormTarget` 会忽略输入框焦点 | `index.html:774-795` | Playwright `keyboard.press` 可直接控制 |
| 胜利弹层会**拦截键盘**（`overlay.show` 时 keydown 直接 return） | `index.html:775-778`, `675-692` | agent 到 2048 后必须点“继续玩”，否则卡死 |
| 每步 `saveState()` 写 `localStorage` | `index.html:694-725`, `537-549` | 重开/换 seed 前必须清 storage，否则恢复旧局 |
| 支持 4×4–36×36、每步生成 1–12 个“2” | `index.html:301-353`, `854-864` | 可作为难度旋钮；LLM 实验固定 4×4 / 1 个 |
| 单文件、无构建、离线 | `2048/README.md` | 优先保持；外部 driver 不破坏这个属性 |

**结论：这个项目已经“半个 agent-ready”**——规则是纯函数，输入是标准键盘事件。缺的只是：确定性种子、状态读取、动作确认、日志/回放。

---

## 2. 可行性验证（本次已实测，不是纸面推演）

环境：Playwright 1.63 Python（`E:\AI\tools\playwright\.venv`，chromium-1243）+ 本机 2048。

| 验证项 | 结果 |
| --- | --- |
| 打开 `file:///E:/AI/tools/HEAOZIE/2048/index.html`，读 16 格 + 分数 | 通过（截图见 `%TEMP%\heao2048_spike.png`） |
| 加载前 `add_init_script` 覆写 `Math.random`，清 `localStorage` 后重载 | 同 seed 起始盘一致（`[[4,0,0,0],[2,0,0,0]]`） |
| 同 seed + 同 greedy 策略整局跑两遍 | **7592 分 / 564 步 / 512 块 / 终盘逐格一致** |
| 读 DOM → `Game2048.move` 求合法步 → 方向键落子 | 通过；20 步循环 693 ms（含 20 ms 等待/步） |
| 4×4 单局 greedy 564 步 | 3.4 s（6.0 ms/步） |
| 胜利盘（1024+1024）左移 | 弹“你合成了 2048！”，键盘被拦；点“继续玩”后恢复 |
| 死局盘加载 | 直接弹“游戏结束” |

基线（5 seeds，4×4，spawn=1）：

| 策略 | 均分 | 范围 | 最高块 | 步数 |
| --- | --- | --- | --- | --- |
| random legal | **1149** | 676–1868 | 64–128 | 93–188 |
| greedy（一步最大合并分 + 空格数） | **4362** | 924–7468 | 256–512 | 126–564 |

> 这两个数很重要：它们是“本地模型到底会不会玩”的标尺。一个 9B 若无脑选方向，分数会落在 random 附近；如果能稳定超过 4.4k，说明它至少学到了“往一处堆”的局部策略。

已知坑（实测）：

- **旧局恢复**：页面 boot 时优先 `loadState()`（`index.html:869-892`），不清 `localStorage` 会接着上一局玩。
- **胜利弹层**：到 2048 后键盘完全失效，必须点 `#btn-overlay-continue`。
- **Edge headless 截图不可用**（本机 tools.md §5 已记录），但 Playwright 自带 chromium 正常。
- **输入焦点**：如果 agent 误点 `#spawn-count` 等输入框，方向键会被 `isFormTarget` 忽略。driver 不要点表单控件。
- **同 seed 不等于“同一副牌”**：随机流相同，但不同策略在不同盘面消耗 RNG，落点会分叉；seed 只保证“同一策略可复现”，不保证跨模型逐格配对。

---

## 3. 目标与非目标

**目标**

- 任意 agent（本地 LLM 脚本、Python/Node、未来 MCP）能：读状态 → 选动作 → 落子 → 拿结果，且**每一步可验证**。
- 同一 seed + 同一动作序列 → 完全相同的棋局（可回放、可断点续跑）。
- 能记录：棋盘、分数、合法步、模型原始输出、token 用量、每步耗时、非法/重试。
- 能看到：实时 HUD / 终局截图 / 逐步回放；能对比基线。

**非目标（首轮不做）**

- 不做联网、不做多用户、不做排行榜。
- 不改 2048 的玩法/视觉；不改 36×36 与 spawn 自定义。
- 不做模型微调/RL；先测“现成模型 + 提示词”能玩成什么样。
- 不做云端模型对比（除非之后想加对照组）。

---

## 4. 方案对比

| 方案 | 做法 | 优点 | 缺点 | 建议 |
| --- | --- | --- | --- | --- |
| **A. 外部 Driver（推荐 MVP）** | Playwright 打开真实页面；DOM 读盘、`Game2048.move` 判合法、方向键落子；加载前覆写 `Math.random` 定种子 | 零改游戏；单文件属性不变；已是“真实玩家”路径；已实测 | 依赖 DOM 结构；种子靠覆写 `Math.random`（非正式接口） | **首轮采用** |
| **B. 游戏内置 `?agent=1` API** | 在 UI IIFE 内加 `window.__2048Agent`（state/newGame/move）+ 正式 seeded RNG | 稳定、快、可测；可给 HTTP/MCP 复用；不用 DOM 猜 | 需要改 `index.html`（约 80–120 行）；要补测试 | 二期加固 |
| **C. 页面 ↔ 本地 WebSocket/HTTP 桥** | 页面主动连本地 server；server 广播 state、收 move | 可远程/实时；浏览器只负责显示 | 组件最多；异常恢复复杂；首轮收益低 | 三期可选 |
| **D. 纯 headless 引擎** | 从 `index.html` 抽 `Game2048` 到 Node/Python，完全不用浏览器 | 最快；易并行/大批量基线 | 要复刻 UI 的 spawn/存档/胜负逻辑；与真实页面有漂移风险 | 只用于大规模基线/CI，不作为主路径 |

**推荐组合：A 先跑通 → B 作为“正式 agent 接口” → C/D 按需。**
理由：A 今天就能出结果，且不碰用户的可玩游戏；B 在 A 验证了协议之后再固化，避免一开始就改错接口。

---

## 5. 总体架构

```
┌────────────────────┐    OpenAI 兼容 HTTP     ┌─────────────────────────┐
│ llama-server :8096 │ <────────────────────── │ agent/play.py           │
│ 4B / 9B / 2B       │   /v1/chat/completions  │  · 策略循环（LLM/bot）   │
└────────────────────┘                         │  · 提示词 / JSON 解析    │
                                               │  · 重试 / 日志 / 指标    │
                                               └───────────┬─────────────┘
                                                           │ Playwright（本机）
                                               ┌───────────▼─────────────┐
                                               │ Chromium（headless/有头）│
                                               │ file:///.../index.html  │
                                               │ DOM 读盘 + 键盘落子      │
                                               └─────────────────────────┘
```

关键原则：**浏览器是唯一规则真源**。LLM 只负责“看盘 + 选方向”，不负责算合并；合法性由页面里的 `Game2048.move` 判定，避免 Python/JS 两套规则漂移。

---

## 6. 方案 A：外部 Driver（首轮实现）

### 6.1 页面生命周期与种子

每个 seed 用一个全新的 browser context（隔离 `localStorage`）：

```python
SEED_JS = r"""
(() => {
  let a = %d >>> 0;
  Math.random = function () {           // mulberry32
    a |= 0; a = (a + 0x6D2B79F5) | 0;
    let t = Math.imul(a ^ (a >>> 15), 1 | a);
    t = (t + Math.imul(t ^ (t >>> 7), 61 | t)) ^ t;
    return ((t ^ (t >>> 14)) >>> 0) / 4294967296;
  };
})();
"""
ctx = browser.new_context()
ctx.add_init_script(SEED_JS % seed)
page = ctx.new_page()
page.goto(URL)
page.evaluate("localStorage.clear()")   # 丢掉上一次的 2048-state
page.reload()
page.wait_for_selector("#board .cell")
```

> 注意：`add_init_script` 在**每次导航**都执行，所以 `a` 会重置；清 storage 后的 reload 才是“干净开局”。

### 6.2 读状态

```python
STATE_JS = r"""
() => {
  const cells = [...document.querySelectorAll('#board .cell')];
  const n = Math.round(Math.sqrt(cells.length));
  const grid = [];
  for (let r = 0; r < n; r++) {
    grid.push(cells.slice(r*n, (r+1)*n).map(e => e.textContent ? +e.textContent : 0));
  }
  return {
    n, grid,
    score: +document.getElementById('score').textContent,
    overlay: document.getElementById('overlay').classList.contains('show'),
    canMove: Game2048.canMove(grid),
    maxTile: Math.max(...grid.flat())
  };
}
"""
```

### 6.3 合法步与落子

```python
LEGAL_JS = r"""
(grid) => ['up','down','left','right'].filter(d => Game2048.move(grid, d).moved)
"""
KEYS = {"up": "ArrowUp", "down": "ArrowDown", "left": "ArrowLeft", "right": "ArrowRight"}
```

落子后**不需要等动画**：`keydown` 处理器同步完成 `G.move → afterMove → render`。读 DOM 立即是最新盘。

### 6.4 终止条件

- `overlay && !canMove` → 输。
- `overlay && canMove` → 刚合成 2048 的胜利弹层：记录 `reached2048=true`，`page.click("#btn-overlay-continue")`，继续。
- `moves >= max_moves` 或 wall time 超预算 → 截断并记录（不当作失败）。

### 6.5 HUD（让“看”更直观）

用 `page.evaluate` 注入一个 `position:fixed` 的小面板，显示：模型名、seed、第几步、分数、最大块、上一步、耗时、tokens/s。**不改游戏源码**；截图/有头窗口都能看到。

### 6.6 截图 / 录像 / 回放

- 每局终局截图（`page.screenshot`）。
- 演示局可 `record_video_dir`（Playwright 自带 ffmpeg）出 WebM；或每 N 步截一张，用 Pillow 合 GIF。
- 全部对局用 JSONL 落盘，M3 生成自包含 `replay.html`（时间轴 + 自动播放 + 每步分数）。

---

## 7. 方案 B：游戏内置 Agent API（二期，建议在 A 跑通后做）

### 7.1 改动范围

- 只改 `index.html` 的 UI IIFE 与两处随机调用，**不改视觉、不改默认行为**。
- 入口：`?agent=1`（可选 `?seed=123`）。
- 新增约 100 行：seeded RNG、`window.__2048Agent`、HUD 开关。

### 7.2 接口草案

```ts
window.__2048Agent = {
  version: 1,
  state(): {
    size: number; grid: number[][]; score: number; best: number;
    spawnCount: number; moves: number;
    canMove: boolean; legal: ("up"|"down"|"left"|"right")[];
    over: boolean; won: boolean;
    overlay: "none" | "win" | "lose";
  },
  newGame(opts?: { size?: number; seed?: number; spawnCount?: number }): State,
  move(dir: "up"|"down"|"left"|"right"): {
    ok: boolean; reason?: "busy"|"illegal"|"over";
    gained?: number; state: State;
  },
  setSpawnCount(n: number): void,
  continueAfterWin(): void
}
```

实现要点：

- `mulberry32(seed)`；`let rng = Math.random`，把 `newGame` 里的 `G.addRandomTile(grid)`（`index.html:663-664`）和 `afterMove` 里的 `G.addRandomTiles(grid, spawnCount)`（`index.html:707`）改为传 `rng`。
- `move()` 内部走 `G.move` + `afterMove`，不经过 `busy` 的键盘路径；`over` 时返回 `ok:false, reason:"over"`。
- 胜利弹层：`move()` 不自动关；driver 显式 `continueAfterWin()`（或参数 `autoContinue`），保持行为明确。
- `newGame` 清 `2048-state`（`clearState()`），不重置 `best`（按尺寸分开存，和现逻辑一致）。
- 不在默认模式暴露 `window.__2048Agent`，避免影响普通玩家/测试。

### 7.3 为什么值得做

- 读状态不再依赖 DOM 文案；36×36 时也快。
- 可把同一接口包成 HTTP（`GET /state`、`POST /move`）或 MCP，任何 agent 都能接。
- 可以写真正的单测（不必起浏览器）：把 UI 状态机抽成可测函数，或在 Playwright smoke 里断言 100 步无漂移。

---

## 8. 本地模型接入

### 8.1 专用服务（不要直接用 8095 的 RP 配置）

现有 `D:\AI\bat\22_server_ornith_ud_8095.bat`：`-rea on` + NSFW-RP LoRA + 64K ctx。拿来玩 2048 有两个问题：思考 token 让每步变 10–45 s；LoRA 与任务无关。

建议新起 **8096**（示例，4B 先跑）：

```bat
D:\AI\llama.cpp\llama-server.exe ^
  -m D:\AI\models\Qwen3.5-4B-Deckard-HERETIC\Qwen3.5-4B-Deckard-HERETIC-UNCENSORED-Thinking.i1-Q4_K_M.gguf ^
  -ngl 99 -c 8192 -ctk q4_0 -ctv q4_0 -fa on ^
  -rea off -t 16 --cpu-range 0-19 -np 1 ^
  --host 127.0.0.1 --port 8096 --alias 2048-4b --api-key local
```

9B 版把 `-m` 换成 Ornith-1.5-9B-uncensored（或 Qwen3.8-9B-Distill），`-ngl 28`，其余相同；不再挂 LoRA。启动后先 `GET /health` 和一次 8 token 的冒烟请求，确认没有 `<think>` 泄漏。

> 显存门禁：本机 6 GB；4B 约 4.2 GB、9B 约 5.0–5.3 GB。起模型前 `nvidia-smi` 看 free；浏览器用 `--disable-gpu` 或 headless，别和模型抢显存。

### 8.2 请求

```python
payload = {
  "model": "2048-4b",
  "messages": [{"role": "system", "content": SYSTEM}, {"role": "user", "content": prompt}],
  "max_tokens": 16,
  "temperature": 0.2,
  "top_p": 0.9,
  "seed": 1234,
  "stream": False,
  "response_format": {
    "type": "json_schema",
    "json_schema": {
      "name": "move", "strict": True,
      "schema": {
        "type": "object",
        "properties": {"move": {"type": "string", "enum": ["up","down","left","right"]}},
        "required": ["move"], "additionalProperties": False
      }
    }
  }
}
```

llama.cpp b11200 已实测支持严格 `json_schema`（tools.md §3.3-2），合法 JSON 通过率可到 100%。仍保留正则/同义词兜底（`L/ArrowLeft/左` 等），并记录 `raw_response`。

### 8.3 提示词 v0

```text
You are a precise 2048 player. Reply with JSON only.

2048 on a 4x4 grid. 0 = empty. After every move a new tile (2 or 4) appears.
Board (top row first):
   0    0    2    0
   0    0    0    0
   0    0    0    0
   0    0    0    0
Score: 0
Legal moves: up, left, right

Keep the largest tile in a corner. Keep rows/columns monotonic (sorted from one edge).
Avoid moves that fill the board or break the corner.
Choose one legal move: {"move":"left"}
```

设计要点：

- 英文、固定宽度对齐、`0=empty` 明示；4×4 约 150–250 token。
- `Legal moves` 由页面算，模型不用自己判；非法步直接重问。
- 先给 **plain**（只规则，不给策略提示），再加 **hint**（corner/monotonic）做消融；两者结果要分开报，别把“提示词教得好”当成“模型会玩”。
- 可选 `last 3 moves` 防止来回横跳；可选“给 4 个候选盘面选一个”（assisted 模式）作为上界，不作为主结果。

### 8.4 每步循环与容错

```
state = driver.state()
if state.over: break
legal = driver.legal(state.grid)
for attempt in 1..3:
    raw = llm(prompt(state, legal))
    move = parse(raw)
    if move in legal: break
    prompt += "\nIllegal move: ...; legal moves are ..."
if still bad: move = random.choice(legal); invalid_fallback += 1
driver.move(move)
```

- 单次请求超时 60 s（思考模式 180 s）；超时 → 记 `timeout`，随机合法步兜底。
- 连续 5 次解析失败 → 中止该局，保留日志，不污染统计。
- 每步 flush JSONL，崩溃最多丢一步。

---

## 9. 实验设计

### 9.1 固定项

- 4×4，spawn=1，无 undo，单局上限 5000 步 / 90 分钟。
- 同一 seed 集合（建议 1–5；首轮 1 个，正式 3–10 个）。
- 同一 driver、同一提示词版本、同一 llama.cpp 参数；记录服务器启动命令与模型文件 hash。

### 9.2 变量

| 维度 | 取值 | 目的 |
| --- | --- | --- |
| 模型 | 2B / 4B / 9B（可选 Gemma-4-E4B、Qwen3.8-9B-Distill） | 规模/指令跟随差异 |
| 提示词 | plain / +hint / +CoT（先给一句策略再给 move） | 策略提示与推理的增益 |
| 温度 | 0.2 / 0.7 | 稳定性 vs 多样性 |
| 思考 | off / on（只跑 200 步样本） | 延迟与质量 |
| 动作顺序 | 固定 / 每步轮换 | 检测“总选第一个”的位置偏置 |

### 9.3 指标

**每局**：score、max tile、moves、wall time、avg/p95 每步延迟、prompt/completion tokens、非法步数、重试次数、兜底次数、是否到 2048、动作分布（up/down/left/right 占比）、首选位命中率。

**汇总**：均值 ± 标准差、min/max、2048 率、相对 random/greedy 的倍率。

**定性**：是否形成角落、是否保持单调、是否来回横跳、失败前的典型盘面；用 replay 抽 3 局看。

### 9.4 基线与预期

| 策略 | 实测/预期 | 说明 |
| --- | --- | --- |
| random legal | 1149（实测） | 下限 |
| greedy 1-ply | 4362（实测） | “会一点”的分界线 |
| expectimax depth 3 | 约 2–6 万（文献/待实测） | 强基线，M4 再补 |
| 本地 9B plain | 待测（假设 1k–8k） | 首轮核心问题 |
| 本地 9B + hint | 待测（假设 5k–20k） | 提示词能教多少 |

> 不要用 1 局下结论：greedy 的 5 个 seed 从 924 到 7468，方差极大。正式结论至少 3 个 seed，最好 10 个。

### 9.5 时间预算（估算）

| 模型 | 生成速度 | 每步 | 500 步/局 | 3 局 |
| --- | --- | --- | --- | --- |
| 2B MiniCPM5 | ~99 t/s | ~0.3 s | ~3 min | ~9 min |
| 4B Qwen3.5 | ~50 t/s | ~0.6 s | ~5 min | ~15 min |
| 9B Ornith | ~22–26 t/s | ~1.2–2 s | ~10–17 min | ~30–50 min |

加上模型冷启、浏览器、失败重试；首轮 4B 一局（~10 分钟）即可看到“会不会玩”。思考模式另算：200 步样本约 1–2 小时。

---

## 10. 产物

```
2048/agent/
  README.md              用法、模型矩阵、结论摘要
  play.py                主入口：--model/--policy/--games/--seed/--headed/--hud
  driver.py              Playwright 封装：state/legal/move/reset/screenshot
  llm.py                 llama-server OpenAI 客户端（stdlib urllib）
  prompts.py             plain/hint/CoT 模板
  policies.py            random / greedy / expectimax(可选)
  report.py              JSONL → summary.md / CSV / replay.html
  runs/                  运行产物（gitignore）
    <run-id>/
      meta.json          模型、命令、seed、版本
      game-<seed>.jsonl  每步：盘面、合法步、raw、move、tokens、耗时
      game-<seed>.png    终局截图
      summary.json
  replay.html            模板（自包含，可离线打开）
```

---

## 11. 里程碑

| 里程碑 | 内容 | 验收 | 估时 |
| --- | --- | --- | --- |
| **M0** | 可行性验证（本次已完成） | 能读盘、能按键、同 seed 复现、greedy 基线 | 已完成 |
| **M1** | Driver + random/greedy + seeded + HUD + smoke 测试 | 100 步无漂移；同 seed 两局逐格一致；截图 | 0.5 天 |
| **M2** | LLM 客户端 + 提示词 + 严格 JSON + 重试 + JSONL | 固定盘面 10 题能稳定出合法 move；单局可跑完 | 0.5 天 |
| **M3** | 首局演示（4B，1 seed）+ 截图/回放 | 有分数、max tile、延迟、动作分布；人可看 | 0.5 天（含 10 分钟跑） |
| **M4** | 基线 + 模型矩阵（3 seeds × 2–3 模型 × 2 提示词） | 汇总表、基线对照、方差说明 | 1 天（多为等模型） |
| **M5** | 报告 + README/CHANGELOG + commit/push | 可复现命令、结论、已知坑 | 0.5 天 |

首轮建议只做 **M1–M3**，拿到“9B/4B 到底什么水平”的第一手数据，再决定要不要 M4 的大矩阵。

---

## 12. 风险与坑

| 风险 | 影响 | 对策 |
| --- | --- | --- |
| 模型开启思考 | 每步 10–45 s，一局数小时 | 专用 `-rea off`；请求里不要 `enable_thinking`；先冒烟看有没有 `<think>` |
| 6 GB 显存被占 | OOM / 掉到 CPU | 起模型前 `nvidia-smi`；浏览器 `--disable-gpu`/headless；9B 不够就上 4B |
| 16 GB 内存换页 | t/s 从 22 掉到 4.6（tools.md §3.3-3） | 预热测速门禁：连续 20 步中位 <15 t/s 判为换页，停跑 |
| 胜利弹层拦截 | 到 2048 后卡死 | driver 检测 `overlay && canMove` → 点“继续玩” |
| localStorage 旧局 | seed 失效/接着上局 | 每个 seed 新建 context + 清 storage + reload |
| 输入框焦点 | 方向键被忽略 | 只点 `#btn-overlay-continue`；不碰 input/select |
| 非法/格式错误 | 统计被污染 | 严格 JSON schema + 同义词归一 + 3 次重试；仍失败随机兜底并记账 |
| 动作位置偏置 | 模型总选列表第一个 | 每步轮换合法步顺序；统计首选位命中率 |
| seed 方差 | 1 局结论不可靠 | 至少 3 seeds；报均值±范围，不报单局 |
| 同 seed 非配对 | 跨模型不能逐格比 | 明确“同随机流、非配对”；用多 seed 分布比较 |
| 长跑中断 | 数据丢失 | 每步 flush JSONL；按 seed/步数断点续跑（重放已走步） |
| 代码漂移 | 规则/接口变化 | 游戏侧只读；页面逻辑版本记录 commit；M1 加 smoke |
| 本地模型偏见 | 输出“左/上”固定 | 记录动作分布；先跑 sanity 盘面（明显该往哪走） |

---

## 13. 验收标准（首轮）

1. `python agent/play.py --policy greedy --games 2 --seed 1` 两局结果逐格一致，且与本次 spike 的 7592/564/512 对上。
2. `--policy llm --model 2048-4b --games 1 --seed 1` 能完整跑完一局，日志每步有盘面、合法步、raw、move、tokens、耗时。
3. 终局有截图；`replay.html` 能离线逐步回放。
4. 报告里同时有 random、greedy、LLM 三行；LLM 有分数、max tile、每步延迟、非法率、动作分布。
5. `git status` 干净；README/CHANGELOG 更新；如果改动 `index.html`，Node 测试仍全过。

---

## 14. 待你确认

1. **先跑哪个模型**：4B（~10 分钟/局，先看会不会玩）还是 9B（~25 分钟/局，更接近“本地大模型”的门面）？
2. **改造范围**：先只做外部 Driver（不动游戏），还是一开始就把 `?agent=1` API 加进 `index.html`？
3. **看的方式**：有头浏览器实时看 + HUD，还是 headless 跑完看回放/截图？
4. **代码位置**：放 `HEAOZIE/2048/agent/`（同仓库，推荐），还是新建独立仓库？
5. **首轮规模**：1 局演示，还是直接 3 seeds × 2 模型？
6. **GPU 现在可以用吗**：起 8096 会占约 4–5 GB 显存；当前 `nvidia-smi` 显示 679 MiB 已用、5.3 GB 空闲。

---

## 15. 参考

- 本机 2048：`E:\AI\tools\HEAOZIE\2048\index.html`；测试 `tests/logic.test.mjs`、`tests/structure.test.mjs`
- 本机 Playwright：`E:\AI\tools\playwright\.venv`，内核 `E:\AI\tools\playwright\browsers`（chromium-1243）
- 本机本地模型栈：`E:\AI\tools.md` §3（llama.cpp b11200、8095、显存/换页结论）
- 本机浏览器自动化坑：`E:\AI\tools.md` §2.7 / §5（Edge headless 截图不可用；Playwright 命名管道）
- 2048 expectimax 参考实现：[MrDaDaDo/Auto-2048](https://github.com/MrDaDaDo/Auto-2048)（Unlicense，bitboard + expectimax）
- 经典强 AI：[nneonneo/2048-ai](https://github.com/nneonneo/2048-ai)
- LLM 玩 2048 的公开 benchmark 参考：[lucasmacori/2048-ai-agent-benchmarks](https://github.com/lucasmacori/2048-ai-agent-benchmarks)（仅作思路参考，未采用其代码）
- 游戏 agent 环境综述（含 2048）：[Orak benchmark, arXiv:2506.03610](https://arxiv.org/html/2506.03610v2)
