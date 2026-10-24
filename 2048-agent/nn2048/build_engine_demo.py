"""Build a self-contained playback page for an engine game (engine_bb --save JSON).

Usage:
  py -3 build_engine_demo.py runs/engine-bb-s1/engine.json
  py -3 build_engine_demo.py engine.json --out demo.html
"""
from __future__ import annotations

import argparse
import json
import pathlib

ROOT = pathlib.Path(__file__).resolve().parent

TEMPLATE = r"""<!doctype html>
<html lang="zh-CN">
<head>
<meta charset="utf-8">
<meta name="viewport" content="width=device-width, initial-scale=1">
<title>2048 · expectimax 演示</title>
<style>
  :root {
    --bg:#0e1116; --panel:#161c24; --line:#232c38; --fg:#e8eef6; --dim:#8a96a6;
    --accent:#6ea8fe; --tile-empty:#1e2630;
  }
  * { box-sizing: border-box; }
  body { margin:0; background:var(--bg); color:var(--fg);
         font:14px/1.5 -apple-system,"Segoe UI","Microsoft YaHei",sans-serif; }
  .wrap { display:flex; gap:28px; padding:26px 30px; max-width:1180px; margin:0 auto; }
  .board-col { flex:0 0 auto; }
  #board { display:grid; grid-template-columns:repeat(4,96px); grid-gap:10px;
           background:#121820; padding:10px; border-radius:12px; }
  .tile { width:96px; height:96px; border-radius:9px; display:flex; align-items:center;
          justify-content:center; font-weight:700; font-size:34px; color:#776e65;
          background:var(--tile-empty); transition:background .08s linear; }
  .tile.small { font-size:24px; }
  .tile.tiny { font-size:19px; }
  .panel { flex:1 1 auto; min-width:0; }
  .title { font-size:20px; font-weight:600; margin:0 0 4px; }
  .sub { color:var(--dim); font-size:13px; margin-bottom:18px; }
  .stats { display:grid; grid-template-columns:repeat(4,1fr); gap:10px; margin-bottom:18px; }
  .stat { background:var(--panel); border:1px solid var(--line); border-radius:10px;
          padding:10px 12px; }
  .stat .k { color:var(--dim); font-size:12px; }
  .stat .v { font-size:22px; font-weight:600; font-variant-numeric:tabular-nums; }
  .stat .v.arrow { color:var(--accent); }
  #chart { width:100%; height:120px; background:var(--panel); border:1px solid var(--line);
           border-radius:10px; display:block; }
  .controls { margin-top:16px; display:flex; flex-wrap:wrap; gap:8px; align-items:center; }
  button { background:var(--panel); color:var(--fg); border:1px solid var(--line);
           border-radius:8px; padding:7px 12px; cursor:pointer; font:inherit; }
  button:hover { border-color:var(--accent); }
  button.on { background:var(--accent); color:#08111f; border-color:var(--accent); font-weight:600; }
  input[type=range] { width:100%; margin:14px 0 0; accent-color:var(--accent); }
  .hint { color:var(--dim); font-size:12px; margin-top:10px; }
  .ms { color:var(--dim); font-size:12px; margin-left:6px; }
</style>
</head>
<body>
<div class="wrap">
  <div class="board-col">
    <div id="board"></div>
  </div>
  <div class="panel">
    <h1 class="title">2048 · expectimax bitboard</h1>
    <div class="sub" id="sub"></div>
    <div class="stats">
      <div class="stat"><div class="k">分数</div><div class="v" id="score">0</div></div>
      <div class="stat"><div class="k">步数</div><div class="v" id="moves">0</div></div>
      <div class="stat"><div class="k">最大块</div><div class="v" id="maxTile">0</div></div>
      <div class="stat"><div class="k">本步</div><div class="v arrow" id="move">-</div></div>
    </div>
    <canvas id="chart" width="1200" height="240"></canvas>
    <input id="scrub" type="range" min="0" max="0" value="0">
    <div class="controls">
      <button id="play">▶ 播放</button>
      <button id="prev">◀ 上一步</button>
      <button id="next">下一步 ▶</button>
      <span class="ms">速度</span>
      <button class="speed" data-s="0.1">0.1s</button>
      <button class="speed" data-s="0.25">0.25s</button>
      <button class="speed on" data-s="0.5">0.5s</button>
      <button class="speed" data-s="1">1s</button>
      <button class="speed" data-s="2">2s</button>
      <span class="ms">跳到</span>
      <span id="jumps"></span>
    </div>
    <div class="hint">快捷键：空格 播放/暂停 · ←/→ 前后一步 · C 循环速度 · Home/End 首尾</div>
  </div>
</div>
<script>
const DATA = /*__DATA__*/;
const steps = DATA.steps, meta = DATA.meta;

const COLORS = {
  0:["#1e2630","#776e65"], 1:["#eee4da","#776e65"], 2:["#ede0c8","#776e65"],
  3:["#f2b179","#f9f6f2"], 4:["#f59563","#f9f6f2"], 5:["#f67c5f","#f9f6f2"],
  6:["#f65e3b","#f9f6f2"], 7:["#edcf72","#f9f6f2"], 8:["#edcc61","#f9f6f2"],
  9:["#edc850","#f9f6f2"], 10:["#edc53f","#f9f6f2"], 11:["#3c3a32","#f9f6f2"],
  12:["#3c3a32","#f9f6f2"], 13:["#34322c","#f9f6f2"], 14:["#2c2a25","#f9f6f2"],
  15:["#24231f","#f9f6f2"], 16:["#1c1b18","#f9f6f2"]
};
const ARROW = {left:"←", right:"→", up:"↑", down:"↓", "":""};
const ZH = {left:"左", right:"右", up:"上", down:"下", "":""};

document.getElementById("sub").textContent =
  `seed ${meta.seed} · ${meta.budget_ms} ms/步 · ${steps.length} 步 · 最高 ${steps[steps.length-1].score.toLocaleString()} 分`;

const board = document.getElementById("board");
const cells = [];
for (let i = 0; i < 16; i++) {
  const d = document.createElement("div");
  d.className = "tile";
  board.appendChild(d);
  cells.push(d);
}

function decode(s) {
  const out = [];
  for (let i = 0; i < 16; i++) {
    const c = s.charCodeAt(i);
    out.push(c >= 97 ? c - 87 : c - 48);
  }
  return out;
}

function draw(idx) {
  const st = steps[idx];
  const grid = decode(st.b);
  let mx = 0;
  for (let i = 0; i < 16; i++) {
    const lv = grid[i];
    mx = Math.max(mx, lv);
    const [bg, fg] = COLORS[Math.min(lv, 16)];
    const el = cells[i];
    el.style.background = bg;
    el.style.color = fg;
    if (lv === 0) { el.textContent = ""; el.className = "tile"; continue; }
    const v = 2 ** lv;
    el.textContent = v >= 100000 ? (v / 1024) + "K" : v;
    el.className = "tile" + (v >= 10000 ? " tiny" : v >= 1000 ? " small" : "");
  }
  document.getElementById("score").textContent = st.s.toLocaleString();
  document.getElementById("moves").textContent = (idx + 1);
  document.getElementById("maxTile").textContent = 2 ** mx;
  document.getElementById("move").textContent =
    `${ARROW[st.m]} ${ZH[st.m]}` + (st.g ? ` +${st.g}` : "");
  document.getElementById("scrub").value = idx;
  drawChart(idx);
}

const canvas = document.getElementById("chart");
const ctx = canvas.getContext("2d");
const scores = [0, ...steps.map(s => s.s)];

function drawChart(idx) {
  const w = canvas.width, h = canvas.height;
  ctx.clearRect(0, 0, w, h);
  const maxS = Math.max(scores[scores.length - 1], 1);
  ctx.strokeStyle = "#2b3644";
  ctx.lineWidth = 1;
  for (let k = 1; k <= 3; k++) {
    const y = h - (h * k / 4);
    ctx.beginPath(); ctx.moveTo(0, y); ctx.lineTo(w, y); ctx.stroke();
  }
  ctx.strokeStyle = "#6ea8fe";
  ctx.lineWidth = 2.5;
  ctx.beginPath();
  const n = scores.length;
  const step = Math.max(1, Math.floor(n / 800));
  for (let i = 0; i < n; i += step) {
    const x = (i / (n - 1)) * w;
    const y = h - (scores[i] / maxS) * (h - 8) - 4;
    i ? ctx.lineTo(x, y) : ctx.moveTo(x, y);
  }
  ctx.stroke();
  const px = (idx / Math.max(1, n - 2)) * w;
  ctx.strokeStyle = "#e8a33d";
  ctx.lineWidth = 2;
  ctx.beginPath(); ctx.moveTo(px, 0); ctx.lineTo(px, h); ctx.stroke();
}

let cur = 0, timer = null, delay = 500;

function show(idx) {
  cur = Math.max(0, Math.min(steps.length - 1, idx));
  draw(cur);
}

function play() {
  if (timer) return;
  document.getElementById("play").textContent = "⏸ 暂停";
  timer = setInterval(() => {
    if (cur >= steps.length - 1) { pause(); return; }
    show(cur + 1);
  }, delay);
}
function pause() {
  if (timer) { clearInterval(timer); timer = null; }
  document.getElementById("play").textContent = "▶ 播放";
}
document.getElementById("play").onclick = () => (timer ? pause() : play());
document.getElementById("prev").onclick = () => { pause(); show(cur - 1); };
document.getElementById("next").onclick = () => { pause(); show(cur + 1); };
document.getElementById("scrub").max = steps.length - 1;
document.getElementById("scrub").oninput = (e) => { pause(); show(+e.target.value); };
document.querySelectorAll(".speed").forEach(btn => {
  btn.onclick = () => {
    delay = parseFloat(btn.dataset.s) * 1000;
    document.querySelectorAll(".speed").forEach(b => b.classList.toggle("on", b === btn));
    if (timer) { pause(); play(); }
  };
});

// jump buttons at each new max tile
const jumps = document.getElementById("jumps");
let seen = 0;
[1024, 2048, 4096, 8192, 16384, 32768].forEach(target => {
  const idx = steps.findIndex(st => {
    for (let i = 0; i < 16; i++) {
      const lv = st.b.charCodeAt(i) >= 97 ? st.b.charCodeAt(i) - 87 : st.b.charCodeAt(i) - 48;
      if ((2 ** lv) >= target) return true;
    }
    return false;
  });
  if (idx < 0) return;
  const b = document.createElement("button");
  b.textContent = target;
  b.onclick = () => { pause(); show(idx); };
  jumps.appendChild(b);
});

document.addEventListener("keydown", e => {
  if (e.code === "Space") { e.preventDefault(); timer ? pause() : play(); }
  else if (e.code === "ArrowLeft") { e.preventDefault(); pause(); show(cur - 1); }
  else if (e.code === "ArrowRight") { e.preventDefault(); pause(); show(cur + 1); }
  else if (e.key === "c" || e.key === "C") {
    const order = [100, 250, 500, 1000, 2000];
    delay = order[(order.indexOf(delay) + 1) % order.length];
    document.querySelectorAll(".speed").forEach(b =>
      b.classList.toggle("on", Math.round(parseFloat(b.dataset.s) * 1000) === delay));
    if (timer) { pause(); play(); }
  }
  else if (e.code === "Home") { pause(); show(0); }
  else if (e.code === "End") { pause(); show(steps.length - 1); }
});

show(0);
</script>
</body>
</html>
"""


def compact(steps: list[dict]) -> list[dict]:
    out = []
    for st in steps:
        grid = st.get("grid_after") or [[0] * 4 for _ in range(4)]
        code = []
        for r in range(4):
            for c in range(4):
                v = grid[r][c]
                lv = 0 if v <= 0 else v.bit_length() - 1
                code.append("0123456789abcdef"[min(lv, 15)])
        out.append({"i": st.get("i", 0), "m": st.get("move", ""), "g": st.get("gained", 0),
                    "s": st.get("score", 0), "d": st.get("depth", 0), "b": "".join(code)})
    return out


def main() -> int:
    ap = argparse.ArgumentParser()
    ap.add_argument("json", type=pathlib.Path)
    ap.add_argument("--out", type=pathlib.Path, default=None)
    args = ap.parse_args()
    data = json.loads(args.json.read_text(encoding="utf-8"))
    payload = {"meta": data.get("meta", {}), "steps": compact(data["steps"])}
    html = TEMPLATE.replace("/*__DATA__*/", json.dumps(payload, ensure_ascii=False))
    out = args.out or (args.json.parent / "demo.html")
    out.write_text(html, encoding="utf-8")
    print(f"{out}  ({out.stat().st_size / 1e6:.2f} MB, {len(payload['steps'])} steps)")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
