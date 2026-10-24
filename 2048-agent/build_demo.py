"""Build a CS2-demo-style replay player + full report from a run directory.

Usage:
  py -3 build_demo.py runs/ds-flash-plain-s1

Outputs (inside the run directory):
  demo.json    the "demo file" (meta + every step, compact JSON)
  demo.html    self-contained playback player (play/pause/speed/scrub/typewriter)
  report.html  static full report with charts and the complete move table
  REPORT.md    the same report as markdown
"""
from __future__ import annotations

import json
import pathlib
import statistics
import sys
from datetime import datetime

ROOT = pathlib.Path(__file__).resolve().parent

DIR_ARROW = {"up": "↑", "down": "↓", "left": "←", "right": "→"}
DIR_ZH = {"up": "上", "down": "下", "left": "左", "right": "右"}
DIR_COLOR = {"up": "#5aa9ff", "down": "#ff9f43", "left": "#4cd97b", "right": "#c084fc"}


def load_run(run_dir: pathlib.Path) -> tuple[dict, list[dict]]:
    meta = json.loads((run_dir / "meta.json").read_text(encoding="utf-8"))
    steps = []
    with open(run_dir / "run.jsonl", encoding="utf-8") as fh:
        for line in fh:
            line = line.strip()
            if line:
                steps.append(json.loads(line))
    return meta, steps


def greedy_of(step: dict) -> str | None:
    cands = step.get("candidates") or {}
    legal = [d for d in step.get("legal") or [] if d in cands]
    if not legal:
        return None

    def key(d: str):
        c = cands[d]
        return (1 if c.get("canMove") else 0, c.get("gain", 0), c.get("empties", 0))

    return max(legal, key=key)


def pct(values: list[float], p: float) -> float:
    if not values:
        return 0.0
    values = sorted(values)
    k = (len(values) - 1) * p
    lo, hi = int(k), min(int(k) + 1, len(values) - 1)
    return values[lo] + (values[hi] - values[lo]) * (k - lo)


def fmt_secs(s: float | None) -> str:
    if not s:
        return "—"
    s = int(round(s))
    return f"{s // 60}:{s % 60:02d}"


def compute_stats(meta: dict, steps: list[dict]) -> dict:
    lat = [s["latency_ms"] for s in steps if s.get("latency_ms")]
    rchars = [s.get("reasoning_chars") or 0 for s in steps]
    usage_prompt = sum((s.get("usage") or {}).get("prompt_tokens") or 0 for s in steps)
    usage_completion = sum((s.get("usage") or {}).get("completion_tokens") or 0 for s in steps)
    usage_reasoning = sum(
        ((s.get("usage") or {}).get("completion_tokens_details") or {}).get("reasoning_tokens") or 0
        for s in steps
    )
    fallbacks = sum(1 for s in steps if s.get("fallback"))
    retries = sum(1 for s in steps if s.get("retries"))
    agree = 0
    for s in steps:
        g = greedy_of(s)
        if g and s.get("move") == g:
            agree += 1
    empties = [s.get("empties_before") for s in steps if s.get("empties_before") is not None]
    gains = [s.get("gained") or 0 for s in steps]
    max_gain = max(gains) if gains else 0
    max_gain_step = steps[gains.index(max_gain)]["i"] if steps else None
    longest = max(steps, key=lambda s: s.get("reasoning_chars") or 0) if steps else None
    final = meta.get("final") or {}
    return {
        "moves": len(steps),
        "lat_avg": statistics.mean(lat) / 1000 if lat else 0,
        "lat_p50": pct(lat, 0.5) / 1000 if lat else 0,
        "lat_p90": pct(lat, 0.9) / 1000 if lat else 0,
        "lat_max": max(lat) / 1000 if lat else 0,
        "rchars_total": sum(rchars),
        "rchars_avg": statistics.mean(rchars) if rchars else 0,
        "rchars_max": max(rchars) if rchars else 0,
        "usage_prompt": usage_prompt,
        "usage_completion": usage_completion,
        "usage_reasoning": usage_reasoning,
        "fallbacks": fallbacks,
        "retries": retries,
        "greedy_agree": agree,
        "greedy_pct": (agree / len(steps) * 100) if steps else 0,
        "empties_min": min(empties) if empties else 0,
        "max_gain": max_gain,
        "max_gain_step": max_gain_step,
        "longest_step": longest["i"] if longest else None,
        "longest_chars": longest.get("reasoning_chars") if longest else 0,
        "final": final,
        "seconds": final.get("seconds") or 0,
    }


def build_demo_json(meta: dict, steps: list[dict]) -> dict:
    return {"meta": meta, "steps": steps}


def js_safe(obj) -> str:
    return json.dumps(obj, ensure_ascii=False).replace("</", "<\\/")


# --------------------------------------------------------------------------- #
# demo.html — the playback player
# --------------------------------------------------------------------------- #
DEMO_TEMPLATE = r"""<!DOCTYPE html>
<html lang="zh-CN">
<head>
<meta charset="utf-8">
<meta name="viewport" content="width=device-width, initial-scale=1">
<title>__TITLE__ · demo</title>
<style>
:root{
  --bg:#0b0e14; --panel:#121722; --panel2:#171d2b; --line:#232c3d;
  --txt:#e7ecf5; --dim:#8b97ad; --accent:#4cd97b; --gold:#ffd166;
  --up:#5aa9ff; --down:#ff9f43; --left:#4cd97b; --right:#c084fc;
}
*{box-sizing:border-box}
html,body{margin:0;height:100%}
body{background:radial-gradient(1200px 700px at 50% -10%,#152033 0%,var(--bg) 55%);color:var(--txt);
  font:14px/1.5 "Segoe UI",system-ui,"Microsoft YaHei",sans-serif;display:flex;flex-direction:column;overflow:hidden}
button{font:inherit;color:inherit;background:var(--panel2);border:1px solid var(--line);border-radius:8px;
  padding:6px 10px;cursor:pointer}
button:hover{border-color:#3a4a66;background:#1d2637}
button.on{background:#20304a;border-color:#3f6ea8;color:#cfe3ff}

/* top bar */
.topbar{display:flex;align-items:center;gap:18px;padding:10px 18px;border-bottom:1px solid var(--line);
  background:linear-gradient(180deg,#121826,#0e131d)}
.brand{font-weight:800;letter-spacing:2px;font-size:18px;color:#fff}
.brand span{color:var(--accent)}
.runline{display:flex;gap:10px;align-items:baseline;color:var(--dim);font-size:13px;flex-wrap:wrap}
.runline b{color:var(--txt);font-size:15px}
.tag{border:1px solid var(--line);border-radius:999px;padding:1px 9px;font-size:12px;color:#b9c6dc;background:#151c29}
.scoreboard{margin-left:auto;display:flex;gap:18px;align-items:baseline;font-variant-numeric:tabular-nums}
.scoreboard .k{color:var(--dim);font-size:11px;letter-spacing:1px;margin-right:4px}
.scoreboard .v{font-size:22px;font-weight:800}
#status{font-size:12px;letter-spacing:1px;padding:3px 10px;border-radius:999px;border:1px solid var(--line)}
#status.live{color:#7ef0a5;border-color:#2b6b45;background:#10251a}
#status.live::before{content:"●";margin-right:6px;animation:blink 1.2s infinite}
@keyframes blink{50%{opacity:.25}}

/* layout */
main{flex:1;display:grid;grid-template-columns:minmax(420px,1fr) 420px;gap:16px;padding:16px 18px;min-height:0}
.stage{display:flex;flex-direction:column;gap:12px;min-height:0}
.board-wrap{position:relative;flex:1;display:flex;align-items:center;justify-content:center;min-height:0}
.board{position:relative;display:grid;gap:10px;background:#1a2231;border:1px solid #2a3549;border-radius:16px;
  padding:12px;box-shadow:0 18px 60px rgba(0,0,0,.45),inset 0 0 0 1px rgba(255,255,255,.02)}
.cell{position:relative;border-radius:10px;background:#202a3b;display:flex;align-items:center;justify-content:center;
  font-weight:800;font-variant-numeric:tabular-nums;transition:background .18s,color .18s,transform .12s}
.cell .v{transition:transform .18s}
.cell.bump .v{transform:scale(1.14)}
.cell.spawn{animation:spawn .28s ease-out}
@keyframes spawn{0%{transform:scale(.55);opacity:.2}100%{transform:scale(1);opacity:1}}
.cell.gain{animation:gain .45s ease-out}
@keyframes gain{0%{box-shadow:0 0 0 0 rgba(255,209,102,.85)}100%{box-shadow:0 0 0 14px rgba(255,209,102,0)}}
.float{position:absolute;right:26px;top:18px;font-weight:800;font-size:26px;color:var(--gold);
  text-shadow:0 2px 12px rgba(255,209,102,.45);opacity:0;pointer-events:none}
.float.show{animation:floatUp 1s ease-out}
@keyframes floatUp{0%{opacity:0;transform:translateY(10px)}20%{opacity:1}100%{opacity:0;transform:translateY(-26px)}}
.arrow{position:absolute;font-size:120px;font-weight:800;color:rgba(255,255,255,.07);pointer-events:none;
  transition:opacity .25s,transform .25s;opacity:0}
.arrow.show{opacity:1}
.hud{display:flex;gap:10px;flex-wrap:wrap}
.bookmarks{display:flex;gap:6px;flex-wrap:wrap;margin-top:2px}
.bookmarks button{padding:3px 9px;font-size:12px;color:#b9c6dc}
.bookmarks button b{color:var(--gold);margin-left:4px}
.hudrow{flex:1;min-width:120px;background:var(--panel);border:1px solid var(--line);border-radius:10px;padding:7px 11px}
.hudrow span{display:block;color:var(--dim);font-size:11px;letter-spacing:.5px}
.hudrow b{font-size:16px;font-variant-numeric:tabular-nums}
.graph{height:92px;background:var(--panel);border:1px solid var(--line);border-radius:10px;padding:6px 8px}
.graph svg{width:100%;height:100%}

/* side panel */
.side{display:flex;flex-direction:column;min-height:0;background:var(--panel);border:1px solid var(--line);border-radius:14px}
.tabs{display:flex;gap:6px;padding:8px;border-bottom:1px solid var(--line)}
.tabs button{flex:1;padding:6px}
.panel{flex:1;overflow:auto;padding:12px 14px;min-height:0}
.panel.hidden{display:none}
.meta{display:flex;gap:12px;flex-wrap:wrap;color:var(--dim);font-size:12px;margin-bottom:8px}
.meta b{color:var(--txt);font-weight:600}
.think{white-space:pre-wrap;word-break:break-word;font-size:13.5px;line-height:1.65;color:#cfd9ea}
.think .cursor{display:inline-block;width:8px;height:15px;background:var(--accent);vertical-align:-2px;
  animation:blink .9s steps(2) infinite;margin-left:2px}
.caret{color:var(--accent)}
.answer{margin-top:10px;padding:8px 10px;border:1px solid #2b6b45;background:#10251a;border-radius:8px;
  font-weight:700;color:#9ff0bc}
.answer.bad{border-color:#7a3030;background:#251010;color:#ff9d9d}
.cands{width:100%;border-collapse:collapse;font-size:13px}
.cands th,.cands td{padding:6px 7px;border-bottom:1px solid #1d2534;text-align:right;font-variant-numeric:tabular-nums}
.cands th:first-child,.cands td:first-child{text-align:left}
.cands th{color:var(--dim);font-weight:600;font-size:11px;letter-spacing:.5px}
.cands tr.pick td{background:#15291d;color:#bff5d0}
.cands tr.greedy td:first-child::after{content:"  ★";color:var(--gold);font-size:11px}
.cands tr.illegal td{opacity:.35;text-decoration:line-through}
.verdict{margin-top:10px;font-size:13px;color:#b9c6dc;padding:8px 10px;background:var(--panel2);border-radius:8px;border:1px solid var(--line)}
.log{display:flex;flex-direction:column;gap:4px}
.logrow{display:flex;gap:8px;align-items:baseline;padding:5px 8px;border-radius:7px;cursor:pointer;font-size:13px;
  border:1px solid transparent;font-variant-numeric:tabular-nums}
.logrow:hover{background:var(--panel2);border-color:var(--line)}
.logrow.cur{background:#182234;border-color:#31507a}
.logrow .i{color:var(--dim);width:44px}
.logrow .m{width:30px;font-weight:700}
.logrow .g{width:52px;color:var(--gold);text-align:right}
.logrow .s{width:64px;text-align:right}
.logrow .t{color:var(--dim);margin-left:auto;font-size:11px}
.logrow .fb{color:#ff8b8b;font-size:11px}

/* playbar */
.playbar{display:flex;align-items:center;gap:12px;padding:9px 18px;border-top:1px solid var(--line);
  background:linear-gradient(0deg,#0e131d,#121826)}
.playbar button{min-width:40px}
#play{font-size:15px;width:46px}
.time{font-variant-numeric:tabular-nums;color:var(--dim);font-size:13px;white-space:nowrap}
.time b{color:var(--txt)}
.track{position:relative;flex:1;height:34px;background:#0a0e16;border:1px solid var(--line);border-radius:8px;cursor:pointer;overflow:hidden}
.fill{position:absolute;left:0;top:0;bottom:0;background:linear-gradient(90deg,rgba(76,217,123,.25),rgba(90,169,255,.18));pointer-events:none}
.ticks{position:absolute;inset:0}
.tick{position:absolute;bottom:0;width:2px;border-radius:1px;opacity:.8;pointer-events:none}
.speeds{display:flex;gap:4px}
.speeds button{padding:4px 8px;font-size:12px}
.speeds button.on{background:#20304a;border-color:#3f6ea8}
.hint{color:var(--dim);font-size:11px;white-space:nowrap}
@media (max-width:1100px){main{grid-template-columns:1fr;grid-template-rows:auto auto}.side{max-height:46vh}}
</style>
</head>
<body>
<header class="topbar">
  <div class="brand">DEMO<span>2048</span></div>
  <div class="runline">
    <b id="model">—</b>
    <span class="tag" id="mode">—</span>
    <span class="tag" id="effort">—</span>
    <span class="tag" id="seed">—</span>
    <span class="tag" id="runid">—</span>
  </div>
  <div class="scoreboard">
    <span><span class="k">SCORE</span><span class="v" id="score">0</span></span>
    <span><span class="k">MAX</span><span class="v" id="max">0</span></span>
    <span><span class="k">STEP</span><span class="v" id="step">0</span><span class="k" id="total"></span></span>
  </div>
  <div id="status">FINAL</div>
</header>

<main>
  <section class="stage">
    <div class="board-wrap">
      <div class="board" id="board"></div>
      <div class="float" id="gain"></div>
      <div class="arrow" id="arrow"></div>
    </div>
    <div class="hud">
      <div class="hudrow"><span>本步耗时</span><b id="lat">—</b></div>
      <div class="hudrow"><span>思考字数</span><b id="rchars">—</b></div>
      <div class="hudrow"><span>思考 token</span><b id="rtok">—</b></div>
      <div class="hudrow"><span>决策来源</span><b id="how">—</b></div>
    </div>
    <div class="bookmarks" id="bookmarks"></div>
    <div class="graph"><svg id="graph" viewBox="0 0 600 90" preserveAspectRatio="none"></svg></div>
  </section>

  <aside class="side">
    <nav class="tabs">
      <button data-tab="think" class="on">思考</button>
      <button data-tab="decide">决策</button>
      <button data-tab="log">事件</button>
    </nav>
    <div class="panel" id="panel-think">
      <div class="meta" id="think-meta"></div>
      <div class="think" id="think"></div>
      <div class="answer" id="answer" style="display:none"></div>
    </div>
    <div class="panel hidden" id="panel-decide">
      <table class="cands" id="cands"></table>
      <div class="verdict" id="verdict"></div>
    </div>
    <div class="panel hidden" id="panel-log"><div class="log" id="log"></div></div>
  </aside>
</main>

<footer class="playbar">
  <button id="play" title="空格：播放/暂停">▶</button>
  <div class="time"><b id="tcur">0:00</b> / <span id="tend">0:00</span></div>
  <div class="track" id="track"><div class="fill" id="fill"></div><div class="ticks" id="ticks"></div></div>
  <div class="speeds" id="speeds"></div>
  <button id="dl" title="导出 demo.json">下载 demo</button>
  <span class="hint">空格 播放 · ←→ 单步 · ↑↓ 倍速 · 1–4 跳段</span>
</footer>

<script>
window.__RUN_DATA__ = /*__RUN_DATA__*/null;
</script>
<script>
"use strict";
const DATA = window.__RUN_DATA__;
const META = DATA.meta || {};
const STEPS = DATA.steps || [];
const N = STEPS.length;

/* ---------- helpers ---------- */
const $ = id => document.getElementById(id);
const clamp = (v,a,b) => Math.max(a, Math.min(b, v));
const fmtT = s => { s = Math.max(0, Math.round(s)); return Math.floor(s/60) + ":" + String(s%60).padStart(2,"0"); };
const dirColor = d => ({up:"#5aa9ff",down:"#ff9f43",left:"#4cd97b",right:"#c084fc"}[d] || "#8b97ad");
const dirArrow = d => ({up:"↑",down:"↓",left:"←",right:"→"}[d] || "?");

/* per-step playback duration: real latency, but enough time to read the thinking */
const QUICK = { on:false };
function stepDur(s){
  if (QUICK.on) return 0.55;
  const lat = (s.latency_ms || 0) / 1000;
  const read = (s.reasoning_chars || 0) / 95;      // ~95 chars per second
  return clamp(Math.max(lat, read, 1.2), 1.2, 150);
}
let DUR = STEPS.map(stepDur);
let TOTAL = DUR.reduce((a,b)=>a+b, 0);

/* ---------- state ---------- */
let idx = 0;            // current step index
let t = 0;              // playback time in seconds
let playing = false;
let speed = 2;
let lastFrame = 0;
let shownIdx = -1;
let afterShown = false;
let typeTimer = null;

/* ---------- board ---------- */
const board = $("board");
const size = (META.size || 4);
board.style.gridTemplateColumns = `repeat(${size}, 1fr)`;
const cells = [];
function buildBoard(){
  board.innerHTML = "";
  const n = size;
  const maxPx = Math.min(window.innerHeight * 0.52, window.innerWidth * 0.42, 560);
  board.style.width = maxPx + "px";
  board.style.height = maxPx + "px";
  const fs = maxPx / (n * 3.6);
  for (let i = 0; i < n*n; i++){
    const c = document.createElement("div");
    c.className = "cell";
    c.innerHTML = '<span class="v"></span>';
    board.appendChild(c);
    cells.push(c);
  }
  board.dataset.fs = fs;
}
function tileStyle(v){
  const colors = {
    0:["#202a3b","#0000"], 2:["#2b3a52","#dbe6f7"], 4:["#35507a","#e6f0ff"],
    8:["#3f6ea8","#ffffff"], 16:["#4f86c6","#ffffff"], 32:["#5f9ad6","#ffffff"],
    64:["#7bb3e8","#0b1220"], 128:["#ffd166","#3a2a00"], 256:["#ffb84d","#3a2200"],
    512:["#ff9f43","#3a1c00"], 1024:["#ff7a45","#ffffff"], 2048:["#ff5d5d","#ffffff"],
    4096:["#c084fc","#ffffff"], 8192:["#7c5cff","#ffffff"]
  };
  return colors[v] || ["#ff5d5d","#ffffff"];
}
function renderGrid(grid, prevGrid, spawned, gained){
  const n = size;
  const fs = Number(board.dataset.fs) || 28;
  for (let r = 0; r < n; r++) for (let c = 0; c < n; c++){
    const i = r*n + c;
    const v = grid[r][c];
    const el = cells[i];
    const val = el.querySelector(".v");
    const prev = prevGrid ? prevGrid[r][c] : null;
    val.textContent = v ? v : "";
    val.style.fontSize = Math.round(fs * (String(v).length > 3 ? 0.62 : String(v).length > 2 ? 0.78 : 1)) + "px";
    const [bg, fg] = tileStyle(v);
    el.style.background = bg;
    el.style.color = fg;
    el.classList.toggle("bump", prev !== null && prev !== v && v !== 0);
    const sp = (spawned || []).some(s => s.r === r && s.c === c);
    el.classList.toggle("spawn", sp);
  }
}

/* ---------- hud / panels ---------- */
function setMetaLine(){
  const eff = META.reasoning_effort ? ("effort " + META.reasoning_effort) : "effort —";
  $("model").textContent = META.model || "unknown";
  $("mode").textContent = META.assisted ? "候选板辅助" : "纯棋盘";
  $("effort").textContent = eff;
  $("seed").textContent = "seed " + (META.seed ?? "?");
  $("runid").textContent = META.run_id || "";
  $("total").textContent = " / " + N;
  const st = $("status");
  if (META.status === "running") { st.textContent = "LIVE"; st.classList.add("live"); }
  else { st.textContent = (META.status || "final").toUpperCase(); }
  $("tend").textContent = fmtT(TOTAL);
}
function hud(step){
  if (!step) return;
  $("score").textContent = step.score;
  $("max").textContent = step.max_tile;
  $("step").textContent = step.i;
  $("lat").textContent = step.latency_ms ? (step.latency_ms/1000).toFixed(1) + "s" : "—";
  $("rchars").textContent = (step.reasoning_chars || 0).toLocaleString();
  const usage = step.usage || {};
  const rt = ((usage.completion_tokens_details || {}).reasoning_tokens) || null;
  $("rtok").textContent = rt != null ? rt.toLocaleString() : "—";
  const how = step.how || "—";
  $("how").textContent = step.fallback ? how + " ⚠" : how;
  $("how").style.color = step.fallback ? "#ff8b8b" : "";
}

/* ---------- thinking typewriter ---------- */
let thinkFull = "";
let thinkShown = 0;
function setThinking(step){
  thinkFull = (step.reasoning || "").trim();
  thinkShown = 0;
  $("think").textContent = "";
  $("answer").style.display = "none";
  $("think-meta").innerHTML = [
    "step <b>" + step.i + "</b>",
    "耗时 <b>" + (step.latency_ms ? (step.latency_ms/1000).toFixed(1) + "s" : "—") + "</b>",
    "首字 <b>" + (step.first_token_ms ? (step.first_token_ms/1000).toFixed(1) + "s" : "—") + "</b>",
    "思考 <b>" + (step.reasoning_chars || 0).toLocaleString() + "</b> 字",
    step.truncated ? '<b style="color:#ff8b8b">截断</b>' : "",
    step.fallback ? '<b style="color:#ff8b8b">fallback</b>' : ""
  ].filter(Boolean).join(" · ");
  if (!thinkFull){
    $("think").textContent = "（这一步没有思考文本：模型直接给出答案，或由 fallback 兜底）";
    return;
  }
  // start typewriter driven by the main loop
}
function tickTypewriter(progress){
  if (!thinkFull) return;
  const target = Math.floor(thinkFull.length * clamp(progress, 0, 1));
  if (target <= thinkShown) return;
  thinkShown = target;
  const el = $("think");
  el.textContent = thinkFull.slice(0, thinkShown);
  const cur = document.createElement("span");
  cur.className = "cursor";
  el.appendChild(cur);
  el.scrollTop = el.scrollHeight;
}

/* ---------- decision panel ---------- */
function renderDecision(step){
  const cands = step.candidates || {};
  const legal = step.legal || Object.keys(cands);
  const greedy = (function(){
    const ls = legal.filter(d => cands[d]);
    if (!ls.length) return null;
    return ls.reduce((a,b) => {
      const ca = cands[a], cb = cands[b];
      const ka = [(ca.canMove?1:0), ca.gain, ca.empties];
      const kb = [(cb.canMove?1:0), cb.gain, cb.empties];
      for (let i=0;i<3;i++){ if (kb[i] !== ka[i]) return kb[i] > ka[i] ? b : a; }
      return a;
    });
  })();
  let html = "<thead><tr><th>方向</th><th>得分</th><th>空格</th><th>可继续</th><th></th></tr></thead><tbody>";
  for (const d of ["up","down","left","right"]){
    const c = cands[d];
    const illegal = !c;
    const pick = step.move === d;
    const isGreedy = greedy === d;
    html += '<tr class="' + (pick ? "pick " : "") + (isGreedy ? "greedy " : "") + (illegal ? "illegal" : "") + '">'
      + "<td>" + dirArrow(d) + " " + d + "</td>"
      + "<td>" + (c ? "+" + c.gain : "—") + "</td>"
      + "<td>" + (c ? c.empties : "—") + "</td>"
      + "<td>" + (c ? (c.canMove ? "✓" : "✗") : "—") + "</td>"
      + "<td>" + (pick ? "◀ 模型" : "") + "</td></tr>";
  }
  html += "</tbody>";
  $("cands").innerHTML = html;
  const same = greedy && step.move === greedy;
  $("verdict").innerHTML = "引擎贪心建议：<b style='color:#ffd166'>" + (greedy ? dirArrow(greedy) + " " + greedy : "—") + "</b>"
    + " · 模型选择：<b style='color:#9ff0bc'>" + dirArrow(step.move) + " " + step.move + "</b>"
    + " → " + (same ? "<b style='color:#9ff0bc'>一致</b>" : "<b style='color:#ff9f9f'>不一致</b>")
    + "<br><span style='color:#8b97ad'>决策来源：" + (step.how || "—")
    + (step.content ? " · 原文：" + escapeHtml(step.content.slice(0, 120)) : "") + "</span>";
}

function escapeHtml(s){
  return s.replace(/[&<>"]/g, ch => ({"&":"&amp;","<":"&lt;",">":"&gt;",'"':"&quot;"}[ch]));
}

/* ---------- log ---------- */
let logBuilt = false;
function buildLog(){
  if (logBuilt) return;
  logBuilt = true;
  const box = $("log");
  STEPS.forEach((s, i) => {
    const row = document.createElement("div");
    row.className = "logrow";
    row.dataset.i = s.i;
    row.innerHTML = '<span class="i">#' + s.i + '</span>'
      + '<span class="m" style="color:' + dirColor(s.move) + '">' + dirArrow(s.move) + '</span>'
      + '<span class="g">+' + (s.gained || 0) + '</span>'
      + '<span class="s">' + s.score + '</span>'
      + '<span class="t">' + (s.latency_ms ? (s.latency_ms/1000).toFixed(1) + "s" : "") + (s.fallback ? ' <span class="fb">FB</span>' : "") + '</span>';
    row.onclick = () => seekStep(i);
    box.appendChild(row);
  });
}
function markLog(i){
  const rows = $("log").children;
  for (let k = 0; k < rows.length; k++) rows[k].classList.toggle("cur", k === i);
  if (rows[i]) rows[i].scrollIntoView({block:"nearest"});
}

/* ---------- graph ---------- */
function drawGraph(){
  const svg = $("graph");
  const w = 600, h = 90;
  const scores = STEPS.map(s => s.score);
  const maxS = Math.max(1, ...scores);
  const pts = scores.map((v,i) => [ (i/(Math.max(1,N-1)))*w, h - 6 - (v/maxS)*(h-14) ]);
  let d = pts.map((p,i) => (i?"L":"M") + p[0].toFixed(1) + " " + p[1].toFixed(1)).join(" ");
  let out = '<path d="' + d + '" fill="none" stroke="#4cd97b" stroke-width="2" opacity=".85"/>';
  out += '<path d="' + d + ' L' + w + ' ' + h + ' L0 ' + h + ' Z" fill="url(#g1)" opacity=".35"/>';
  out = '<defs><linearGradient id="g1" x1="0" y1="0" x2="0" y2="1"><stop offset="0" stop-color="#4cd97b"/><stop offset="1" stop-color="#4cd97b" stop-opacity="0"/></linearGradient></defs>' + out;
  const cur = pts[clamp(idx,0,pts.length-1)];
  out += '<circle id="dot" cx="' + cur[0] + '" cy="' + cur[1] + '" r="4" fill="#ffd166"/>';
  svg.innerHTML = out;
}
function moveDot(){
  const svg = $("graph");
  const dot = svg.querySelector("#dot");
  if (!dot) return;
  const w = 600, h = 90;
  const scores = STEPS.map(s => s.score);
  const maxS = Math.max(1, ...scores);
  const i = clamp(idx, 0, N-1);
  const v = scores[i];
  dot.setAttribute("cx", ((i/Math.max(1,N-1))*w).toFixed(1));
  dot.setAttribute("cy", (h - 6 - (v/maxS)*(h-14)).toFixed(1));
}

/* ---------- playback ---------- */
function seekStep(i){
  i = clamp(i, 0, N-1);
  idx = i;
  t = DUR.slice(0, i).reduce((a,b)=>a+b, 0);
  shownIdx = -1;
  render(true);
}
function seekTime(sec){
  sec = clamp(sec, 0, TOTAL);
  t = sec;
  let acc = 0, i = 0;
  for (; i < N; i++){ if (acc + DUR[i] > sec) break; acc += DUR[i]; }
  idx = clamp(i, 0, N-1);
  shownIdx = -1;
  render(true);
}
function setSpeed(s){
  speed = s;
  document.querySelectorAll("#speeds button").forEach(b => b.classList.toggle("on", Number(b.dataset.s) === s));
}
function togglePlay(){
  playing = !playing;
  $("play").textContent = playing ? "❚❚" : "▶";
  if (playing) requestAnimationFrame(frame);
}

function render(force){
  const step = STEPS[idx];
  if (!step) return;
  const changed = idx !== shownIdx;
  if (changed){
    shownIdx = idx;
    afterShown = idx > 0;
    const prev = idx > 0 ? STEPS[idx-1].grid_after : null;
    renderGrid(idx === 0 ? step.grid_before : step.grid_after,
               idx === 0 ? null : prev,
               idx === 0 ? [] : step.spawned, step.gained);
    hud(step);
    setThinking(step);
    if (document.querySelector('#panel-decide').classList.contains("hidden") === false) renderDecision(step);
    markLog(idx);
    moveDot();
    $("score").textContent = step.score;
    $("max").textContent = step.max_tile;
    $("step").textContent = step.i;
    if (step.gained > 0){
      const f = $("gain");
      f.textContent = "+" + step.gained;
      f.classList.remove("show"); void f.offsetWidth; f.classList.add("show");
      const c = cells.find(el => el.querySelector(".v").textContent === String(step.max_tile));
      if (c){ c.classList.remove("gain"); void c.offsetWidth; c.classList.add("gain"); }
    }
    const ar = $("arrow");
    ar.textContent = dirArrow(step.move);
    ar.style.color = dirColor(step.move) + "33";
    ar.classList.remove("show"); void ar.offsetWidth; ar.classList.add("show");
    setTimeout(() => ar.classList.remove("show"), 420);
    const el = $("answer");
    el.style.display = "block";
    el.className = "answer" + (step.fallback ? " bad" : "");
    el.textContent = "MOVE: " + step.move + "   " + (step.fallback ? "（fallback 兜底）" : "");
  }
  // progress within the current step drives the typewriter
  if (!afterShown && t > 0.08){
    renderGrid(step.grid_after, step.grid_before, step.spawned, step.gained);
    afterShown = true;
  }
  let acc = 0;
  for (let i = 0; i < idx; i++) acc += DUR[i];
  const p = clamp((t - acc) / DUR[idx], 0, 1);
  tickTypewriter(p);
  // timeline
  $("tcur").textContent = fmtT(t);
  $("fill").style.width = (TOTAL ? (t / TOTAL * 100) : 0) + "%";
}

function frame(ts){
  if (!playing) return;
  const dt = Math.min(0.25, (ts - lastFrame) / 1000 || 0);
  lastFrame = ts;
  t += dt * speed;
  if (t >= TOTAL){
    t = TOTAL - 0.0001;
    idx = N - 1;
    render(true);
    playing = false;
    $("play").textContent = "▶";
    return;
  }
  let acc = 0, i = 0;
  for (; i < N; i++){ if (acc + DUR[i] > t) break; acc += DUR[i]; }
  if (i !== idx){ idx = clamp(i, 0, N-1); }
  render();
  requestAnimationFrame(frame);
}

/* ---------- timeline ticks ---------- */
function buildTicks(){
  const box = $("ticks");
  let acc = 0;
  const frag = document.createDocumentFragment();
  STEPS.forEach((s, i) => {
    const left = TOTAL ? (acc / TOTAL * 100) : 0;
    acc += DUR[i];
    const t = document.createElement("div");
    t.className = "tick";
    t.style.left = left + "%";
    t.style.height = (6 + Math.min(22, (s.gained || 0) / 3)) + "px";
    t.style.background = dirColor(s.move);
    frag.appendChild(t);
  });
  box.appendChild(frag);
}

/* ---------- key-moment bookmarks ---------- */
function buildBookmarks(){
  const box = $("bookmarks");
  const marks = [];
  [32, 64, 128, 256, 512, 1024, 2048].forEach(tile => {
    const s = STEPS.find(x => (x.max_tile || 0) >= tile);
    if (s) marks.push({label: "首次 " + tile, step: s.i, kind: "tile"});
  });
  if (STEPS.length){
    let best = STEPS[0], low = STEPS[0], long = STEPS[0];
    STEPS.forEach(s => {
      if ((s.gained || 0) > (best.gained || 0)) best = s;
      if ((s.empties_before ?? 99) < (low.empties_before ?? 99)) low = s;
      if ((s.reasoning_chars || 0) > (long.reasoning_chars || 0)) long = s;
    });
    marks.push({label: "最大单步 +" + (best.gained || 0), step: best.i, kind: "gain"});
    marks.push({label: "最险 空格 " + (low.empties_before ?? 0), step: low.i, kind: "low"});
    marks.push({label: "最长思考 " + Math.round((long.reasoning_chars || 0) / 100) / 10 + "K 字", step: long.i, kind: "long"});
  }
  marks.forEach(m => {
    const b = document.createElement("button");
    b.innerHTML = m.label + " <b>#" + m.step + "</b>";
    b.onclick = () => seekStep(m.step - 1);
    box.appendChild(b);
  });
}

/* ---------- wiring ---------- */
function init(){
  buildBoard();
  setMetaLine();
  buildTicks();
  drawGraph();
  buildLog();
  buildBookmarks();
  renderDecision(STEPS[0] || {});
  setSpeed(speed);
  render(true);
  // pre-seek: show first step
  $("play").textContent = "▶";

  $("play").onclick = togglePlay;
  document.querySelectorAll("#speeds button").forEach(b => b.onclick = () => setSpeed(Number(b.dataset.s)));
  $("track").onclick = e => {
    const r = $("track").getBoundingClientRect();
    seekTime((e.clientX - r.left) / r.width * TOTAL);
  };
  document.querySelectorAll(".tabs button").forEach(b => b.onclick = () => {
    document.querySelectorAll(".tabs button").forEach(x => x.classList.toggle("on", x === b));
    ["think","decide","log"].forEach(k => $("panel-" + k).classList.toggle("hidden", k !== b.dataset.tab));
    if (b.dataset.tab === "decide") renderDecision(STEPS[idx]);
    if (b.dataset.tab === "log") markLog(idx);
  });
  $("dl").onclick = () => {
    const blob = new Blob([JSON.stringify(DATA)], {type:"application/json"});
    const a = document.createElement("a");
    a.href = URL.createObjectURL(blob);
    a.download = (META.run_id || "demo") + ".demo.json";
    a.click();
    URL.revokeObjectURL(a.href);
  };
  document.addEventListener("keydown", e => {
    if (e.target.tagName === "INPUT" || e.target.tagName === "TEXTAREA") return;
    if (e.code === "Space"){ e.preventDefault(); togglePlay(); }
    else if (e.code === "ArrowRight"){ e.preventDefault(); playing = false; $("play").textContent="▶"; seekStep(idx + 1); }
    else if (e.code === "ArrowLeft"){ e.preventDefault(); playing = false; $("play").textContent="▶"; seekStep(idx - 1); }
    else if (e.code === "ArrowUp"){ e.preventDefault(); setSpeed([0.5,1,2,4,8][clamp([0.5,1,2,4,8].indexOf(speed)+1,0,4)]); }
    else if (e.code === "ArrowDown"){ e.preventDefault(); setSpeed([0.5,1,2,4,8][clamp([0.5,1,2,4,8].indexOf(speed)-1,0,4)]); }
    else if (e.code === "Home"){ e.preventDefault(); seekStep(0); }
    else if (e.code === "End"){ e.preventDefault(); seekStep(N-1); }
    else if (e.key === "1"){ seekTime(TOTAL*0.0); }
    else if (e.key === "2"){ seekTime(TOTAL*0.25); }
    else if (e.key === "3"){ seekTime(TOTAL*0.5); }
    else if (e.key === "4"){ seekTime(TOTAL*0.75); }
  });
  window.addEventListener("resize", () => { buildBoard(); render(true); });
  // speed buttons
  const sp = $("speeds");
  [0.5,1,2,4,8].forEach(s => {
    const b = document.createElement("button");
    b.textContent = s + "×";
    b.dataset.s = s;
    sp.appendChild(b);
  });
  setSpeed(2);
  // autoplay like a demo: start ~0.6s after load
  setTimeout(() => { if (!playing) togglePlay(); }, 600);
}
init();
</script>
</body>
</html>
"""


def build_demo_html(meta: dict, steps: list[dict]) -> str:
    title = f"{meta.get('model','run')} · {meta.get('run_id','')}"
    data = js_safe({"meta": meta, "steps": steps})
    return DEMO_TEMPLATE.replace("__TITLE__", title).replace("/*__RUN_DATA__*/null", data)


# --------------------------------------------------------------------------- #
# report
# --------------------------------------------------------------------------- #
def svg_score(meta: dict, steps: list[dict]) -> str:
    w, h = 720, 200
    scores = [s["score"] for s in steps]
    if not scores:
        return ""
    maxs = max(scores) or 1
    pts = [(i/(max(1,len(scores)-1))*w, h-18-(v/maxs)*(h-36)) for i,v in enumerate(scores)]
    d = " ".join((("M" if i==0 else "L") + f"{x:.1f} {y:.1f}") for i,(x,y) in enumerate(pts))
    return (f'<svg viewBox="0 0 {w} {h}" class="chart"><path d="{d}" fill="none" stroke="#4cd97b" stroke-width="2"/>'
            f'<line x1="0" y1="{h-18}" x2="{w}" y2="{h-18}" stroke="#2a3549"/>'
            f'<text x="4" y="14" fill="#8b97ad" font-size="11">score 0 → {maxs}</text></svg>')


def svg_latency(steps: list[dict]) -> str:
    w, h = 720, 160
    lat = [(s.get("latency_ms") or 0)/1000 for s in steps]
    if not lat:
        return ""
    mx = max(lat) or 1
    bw = w / max(1, len(lat))
    bars = "".join(
        f'<rect x="{i*bw:.1f}" y="{h-20-(v/mx)*(h-30):.1f}" width="{max(1,bw*0.7):.1f}" '
        f'height="{(v/mx)*(h-30):.1f}" fill="#5aa9ff" opacity=".75"/>'
        for i, v in enumerate(lat)
    )
    return (f'<svg viewBox="0 0 {w} {h}" class="chart">{bars}'
            f'<line x1="0" y1="{h-20}" x2="{w}" y2="{h-20}" stroke="#2a3549"/>'
            f'<text x="4" y="14" fill="#8b97ad" font-size="11">每步耗时（秒），峰值 {mx:.1f}s</text></svg>')


def key_moments(steps: list[dict]) -> list[tuple[str, str]]:
    out: list[tuple[str, str]] = []
    for tile in (32, 64, 128, 256, 512, 1024, 2048):
        hit = next((s for s in steps if s.get("max_tile", 0) >= tile), None)
        if hit:
            out.append((f"首次合出 {tile}", f"第 {hit['i']} 步 · 总分 {hit['score']}"))
    if steps:
        gains = [(s.get("gained") or 0, s) for s in steps]
        g, s = max(gains, key=lambda x: x[0])
        out.append(("单步最高得分", f"第 {s['i']} 步 {DIR_ARROW.get(s['move'],'')} {s['move']} · +{g}"))
        lows = [(s.get("empties_before") or 99, s) for s in steps]
        e, s = min(lows, key=lambda x: x[0])
        out.append(("最险局面（空格最少）", f"第 {s['i']} 步 · 空格 {e} · 总分 {s['score']}"))
        long = max(steps, key=lambda s: s.get("reasoning_chars") or 0)
        out.append(("思考最长的一步", f"第 {long['i']} 步 · {(long.get('reasoning_chars') or 0):,} 字 · {long['move']}"))
        sl = max(steps, key=lambda s: s.get("latency_ms") or 0)
        out.append(("最慢的一步", f"第 {sl['i']} 步 · {(sl.get('latency_ms') or 0)/1000:.1f}s · {sl['move']}"))
        fb = [s["i"] for s in steps if s.get("fallback")]
        if fb:
            out.append(("fallback 步", "、".join(f"#{i}" for i in fb[:12]) + (" …" if len(fb) > 12 else "")))
        retry = [s["i"] for s in steps if s.get("retries")]
        if retry:
            out.append(("重试步", "、".join(f"#{i}" for i in retry[:12]) + (" …" if len(retry) > 12 else "")))
    return out


def phases(steps: list[dict]) -> list[tuple[str, str, str, str, str]]:
    if not steps:
        return []
    n = len(steps)
    cuts = [0, n // 3, 2 * n // 3, n]
    names = ["前段", "中段", "后段"]
    out = []
    for k in range(3):
        seg = steps[cuts[k]:cuts[k + 1]]
        if not seg:
            continue
        gained = sum(s.get("gained") or 0 for s in seg)
        avg_gain = gained / len(seg)
        avg_r = sum(s.get("reasoning_chars") or 0 for s in seg) / len(seg)
        avg_lat = sum(s.get("latency_ms") or 0 for s in seg) / len(seg) / 1000
        out.append((names[k], f"{seg[0]['i']}–{seg[-1]['i']}", f"+{gained}", f"{avg_gain:.1f}", f"{avg_r:.0f} 字 / {avg_lat:.0f}s"))
    return out


def excerpt(step: dict, limit: int = 1400) -> str:
    text = (step.get("reasoning") or "").strip()
    if not text:
        text = (step.get("content") or "").strip()
    return text[:limit] + ("…" if len(text) > limit else "")


def escape_html(text: str) -> str:
    return (
        text.replace("&", "&amp;").replace("<", "&lt;").replace(">", "&gt;").replace('"', "&quot;")
    )


def build_report_html(meta: dict, steps: list[dict], stats: dict) -> str:
    final = stats["final"]
    rows = []
    for s in steps:
        rows.append(
            "<tr>"
            f"<td>{s['i']}</td><td>{DIR_ARROW.get(s['move'],'?')} {s['move']}</td>"
            f"<td>+{s.get('gained',0)}</td><td>{s['score']}</td><td>{s['max_tile']}</td>"
            f"<td>{s.get('empties_before','')}</td>"
            f"<td>{(s.get('latency_ms') or 0)/1000:.1f}s</td>"
            f"<td>{(s.get('reasoning_chars') or 0):,}</td>"
            f"<td>{s.get('how','')}{' ⚠' if s.get('fallback') else ''}</td>"
            "</tr>"
        )
    table = "\n".join(rows)
    baseline = [
        ("Qwen3.5-4B 纯棋盘", 780, 64, 108, "21m22s"),
        ("Qwen3.5-4B 候选板", 2148, 128, 214, "36m51s"),
        ("beam-free 纯棋盘", 840, 128, 81, "34m30s"),
        ("随机策略（10 局均值）", 1149, "64–128", "—", "—"),
        ("贪心策略（10 局均值）", 4362, "256–512", "—", "—"),
    ]
    this = (f"<b>本次：{meta.get('model','?')}</b>", final.get("score") or stats.get("score") or 0,
            final.get("max_tile") or 0, stats["moves"], fmt_secs(stats["seconds"]))
    cmp_rows = "\n".join(
        f"<tr{' class=hl' if b[0]==this[0] else ''}><td>{b[0]}</td><td>{b[1]}</td><td>{b[2]}</td><td>{b[3]}</td><td>{b[4]}</td></tr>"
        for b in [this] + baseline
    )
    km_rows = "\n".join(f"<tr><td>{k}</td><td>{v}</td></tr>" for k, v in key_moments(steps))
    ph_rows = "\n".join(
        f"<tr><td>{a}</td><td>{b}</td><td>{c}</td><td>{d}</td><td>{e}</td></tr>"
        for a, b, c, d, e in phases(steps)
    )
    long_step = max(steps, key=lambda s: s.get("reasoning_chars") or 0) if steps else None
    diss = None
    for s in steps:
        g = greedy_of(s)
        if g and s.get("move") != g and (s.get("reasoning_chars") or 0) > 200:
            diss = s
            break
    sample_blocks = []
    if long_step and (long_step.get("reasoning") or "").strip():
        sample_blocks.append(
            f"<h3>思考最长的一步 · 第 {long_step['i']} 步（{long_step.get('reasoning_chars') or 0:,} 字，{DIR_ARROW.get(long_step['move'],'')} {long_step['move']}）</h3>"
            f"<pre class=\"quote\">{escape_html(excerpt(long_step))}</pre>"
        )
    if diss and diss is not long_step:
        g = greedy_of(diss)
        sample_blocks.append(
            f"<h3>与贪心不一致的一步 · 第 {diss['i']} 步（模型 {DIR_ARROW.get(diss['move'],'')} {diss['move']}，贪心 {DIR_ARROW.get(g,'')} {g}）</h3>"
            f"<pre class=\"quote\">{escape_html(excerpt(diss, 1000))}</pre>"
        )
    samples = "\n".join(sample_blocks)
    return f"""<!DOCTYPE html>
<html lang="zh-CN"><head><meta charset="utf-8">
<title>{meta.get('model','run')} · 2048 对局报告</title>
<style>
:root{{--bg:#0b0e14;--panel:#121722;--line:#232c3d;--txt:#e7ecf5;--dim:#8b97ad;--accent:#4cd97b;--gold:#ffd166}}
*{{box-sizing:border-box}} body{{margin:0;background:var(--bg);color:var(--txt);
 font:15px/1.65 "Segoe UI",system-ui,"Microsoft YaHei",sans-serif;padding:34px 18px 60px}}
.wrap{{max-width:1000px;margin:0 auto}}
h1{{font-size:30px;margin:0 0 4px}} h2{{margin:34px 0 10px;font-size:20px;border-left:4px solid var(--accent);padding-left:10px}}
.sub{{color:var(--dim);margin-bottom:22px}}
.cards{{display:grid;grid-template-columns:repeat(auto-fit,minmax(150px,1fr));gap:12px}}
.card{{background:var(--panel);border:1px solid var(--line);border-radius:12px;padding:14px 16px}}
.card .k{{color:var(--dim);font-size:12px;letter-spacing:.5px}}
.card .v{{font-size:26px;font-weight:800;font-variant-numeric:tabular-nums}}
table{{width:100%;border-collapse:collapse;background:var(--panel);border:1px solid var(--line);border-radius:10px;overflow:hidden;font-size:13.5px}}
th,td{{padding:7px 10px;border-bottom:1px solid #1b2333;text-align:right;font-variant-numeric:tabular-nums}}
th:first-child,td:first-child{{text-align:left}} th{{background:#161d2b;color:#b9c6dc;font-weight:600;font-size:12px}}
tr.hl td{{background:#15291d;color:#bff5d0;font-weight:700}}
.chart{{width:100%;height:auto;background:var(--panel);border:1px solid var(--line);border-radius:10px;margin:10px 0}}
.scroll{{max-height:420px;overflow:auto;border-radius:10px}}
code{{background:#161d2b;border:1px solid var(--line);border-radius:6px;padding:1px 6px;font-size:13px}}
.note{{color:var(--dim);font-size:13px}}
a{{color:#7bb3e8}}
h3{{margin:18px 0 6px;font-size:15px;color:#b9c6dc}}
.quote{{background:#10151f;border:1px solid var(--line);border-left:3px solid var(--accent);border-radius:8px;
 padding:12px 14px;white-space:pre-wrap;word-break:break-word;font:13px/1.6 Consolas,"Microsoft YaHei",monospace;color:#cfd9ea;max-height:320px;overflow:auto}}
</style></head><body><div class="wrap">
<h1>{meta.get('model','?')} 玩 2048 · 对局报告</h1>
<div class="sub">run <code>{meta.get('run_id','')}</code> · seed {meta.get('seed')} · {meta.get('prompt_version','')}
 · {meta.get('reasoning_effort') or 'effort 默认'} · 温度 {meta.get('temperature')}
 · 生成于 {datetime.now().astimezone().isoformat(timespec='seconds')}</div>

<div class="cards">
  <div class="card"><div class="k">最终得分</div><div class="v">{final.get('score') or 0}</div></div>
  <div class="card"><div class="k">最大方块</div><div class="v">{final.get('max_tile') or 0}</div></div>
  <div class="card"><div class="k">步数</div><div class="v">{stats['moves']}</div></div>
  <div class="card"><div class="k">总耗时</div><div class="v">{fmt_secs(stats['seconds'])}</div></div>
  <div class="card"><div class="k">平均每步</div><div class="v">{stats['lat_avg']:.1f}s</div></div>
  <div class="card"><div class="k">结果</div><div class="v">{(final.get('status') or meta.get('status') or '').upper()}</div></div>
</div>

<h2>配置</h2>
<table>
<tr><th>模型</th><td>{meta.get('model')}</td><th>推理档位</th><td>{meta.get('reasoning_effort') or '—'}</td></tr>
<tr><th>模式</th><td>{'候选板辅助' if meta.get('assisted') else '纯棋盘（只看当前棋盘 + 规则 + 合法方向）'}</td>
    <th>思考上限</th><td>{'无' if meta.get('thinking_unlimited') else '有'}</td></tr>
<tr><th>seed</th><td>{meta.get('seed')}</td><th>温度</th><td>{meta.get('temperature')}</td></tr>
<tr><th>max_tokens</th><td>{meta.get('max_tokens')}</td><th>网关</th><td>{meta.get('base_url')}</td></tr>
</table>

<h2>过程数据</h2>
<div class="cards">
  <div class="card"><div class="k">思考总字数</div><div class="v">{stats['rchars_total']:,}</div></div>
  <div class="card"><div class="k">平均思考字数</div><div class="v">{stats['rchars_avg']:.0f}</div></div>
  <div class="card"><div class="k">最长一次</div><div class="v">{stats['rchars_max']:,}</div></div>
  <div class="card"><div class="k">思考 token 合计</div><div class="v">{stats['usage_reasoning']:,}</div></div>
  <div class="card"><div class="k">fallback 步数</div><div class="v">{stats['fallbacks']}</div></div>
  <div class="card"><div class="k">与贪心一致率</div><div class="v">{stats['greedy_pct']:.0f}%</div></div>
</div>
<div class="note">每步耗时：中位 {stats['lat_p50']:.1f}s · P90 {stats['lat_p90']:.1f}s · 峰值 {stats['lat_max']:.1f}s；
prompt tokens 合计 {stats['usage_prompt']:,}，completion tokens 合计 {stats['usage_completion']:,}（其中思考 {stats['usage_reasoning']:,}）。</div>

<h2>得分曲线</h2>
{svg_score(meta, steps)}
<h2>每步耗时</h2>
{svg_latency(steps)}

<h2>与其他策略对比</h2>
<table>
<tr><th>策略</th><th>得分</th><th>最大方块</th><th>步数</th><th>耗时</th></tr>
{cmp_rows}
</table>
<div class="note">同 seed 不代表同一局面：随机新块出现的位置/数值一致，但走法不同会导致后续局面分叉，所以只比量级，不做逐格对照。</div>

<h2>关键节点</h2>
<table><tr><th>事件</th><th>位置</th></tr>
{km_rows}
</table>

<h2>阶段走势</h2>
<table><tr><th>阶段</th><th>步数范围</th><th>本段得分</th><th>均步得分</th><th>平均思考 / 耗时</th></tr>
{ph_rows}
</table>

<h2>思考样本</h2>
{samples}

<h2>完整走子表</h2>
<div class="scroll"><table>
<tr><th>#</th><th>方向</th><th>得分</th><th>总分</th><th>最大块</th><th>空格</th><th>耗时</th><th>思考字数</th><th>来源</th></tr>
{table}
</table></div>

<h2>回放</h2>
<p>打开 <a href="demo.html">demo.html</a>（可播放/暂停/倍速/拖时间轴，右侧看每步完整思考），
或下载 <a href="demo.json">demo.json</a>（完整数据，可离线重放）。原始逐步记录：<a href="run.jsonl">run.jsonl</a>。</p>

<h2>复现</h2>
<p><code>python play.py --policy llm --plain --unlimited-thinking --model {meta.get('model')}
 --base-url {meta.get('base_url')} --reasoning-effort {meta.get('reasoning_effort') or 'high'}
 --seed {meta.get('seed')} --run-id {meta.get('run_id')}</code></p>
</div></body></html>
"""


def build_report_md(meta: dict, steps: list[dict], stats: dict) -> str:
    final = stats["final"]
    lines = [
        f"# {meta.get('model','?')} 玩 2048 · 对局报告",
        "",
        f"- run: `{meta.get('run_id')}`",
        f"- seed: {meta.get('seed')} · 模式: {'候选板辅助' if meta.get('assisted') else '纯棋盘'} · 推理档位: {meta.get('reasoning_effort') or '—'}",
        f"- 结果: **{final.get('score') or 0} 分 / 最大 {final.get('max_tile') or 0} / {stats['moves']} 步 / {fmt_secs(stats['seconds'])}**（{final.get('status') or meta.get('status')}）",
        "",
        "## 过程数据",
        "",
        f"- 思考总字数 {stats['rchars_total']:,}，平均 {stats['rchars_avg']:.0f}，最长 {stats['rchars_max']:,}",
        f"- 思考 token 合计 {stats['usage_reasoning']:,}（completion {stats['usage_completion']:,} / prompt {stats['usage_prompt']:,}）",
        f"- 每步耗时 中位 {stats['lat_p50']:.1f}s · P90 {stats['lat_p90']:.1f}s · 峰值 {stats['lat_max']:.1f}s",
        f"- fallback {stats['fallbacks']} 步，与贪心一致率 {stats['greedy_pct']:.0f}%",
        "",
        "## 与其他策略对比",
        "",
        "| 策略 | 得分 | 最大方块 | 步数 | 耗时 |",
        "| --- | ---: | ---: | ---: | ---: |",
        f"| {meta.get('model')} | {final.get('score') or 0} | {final.get('max_tile') or 0} | {stats['moves']} | {fmt_secs(stats['seconds'])} |",
        "| Qwen3.5-4B 纯棋盘 | 780 | 64 | 108 | 21m22s |",
        "| Qwen3.5-4B 候选板 | 2148 | 128 | 214 | 36m51s |",
        "| beam-free 纯棋盘 | 840 | 128 | 81 | 34m30s |",
        "| 随机（10 局均值） | 1149 | 64–128 | — | — |",
        "| 贪心（10 局均值） | 4362 | 256–512 | — | — |",
        "",
        "## 关键节点",
        "",
    ]
    for k, v in key_moments(steps):
        lines.append(f"- **{k}**：{v}")
    lines += [
        "",
        "## 阶段走势",
        "",
        "| 阶段 | 步数 | 本段得分 | 均步得分 | 平均思考 / 耗时 |",
        "| --- | --- | ---: | ---: | --- |",
    ]
    for a, b, c, d, e in phases(steps):
        lines.append(f"| {a} | {b} | {c} | {d} | {e} |")
    long_step = max(steps, key=lambda s: s.get("reasoning_chars") or 0) if steps else None
    if long_step and (long_step.get("reasoning") or "").strip():
        lines += [
            "",
            f"## 思考样本（第 {long_step['i']} 步，{DIR_ARROW.get(long_step['move'],'')} {long_step['move']}）",
            "",
            "```",
            excerpt(long_step),
            "```",
        ]
    lines += [
        "",
        "## 完整走子表",
        "",
        "| # | 方向 | 得分 | 总分 | 最大块 | 空格 | 耗时 | 思考字数 | 来源 |",
        "| ---: | --- | ---: | ---: | ---: | ---: | ---: | ---: | --- |",
    ]
    for s in steps:
        lines.append(
            f"| {s['i']} | {DIR_ARROW.get(s['move'],'?')} {s['move']} | +{s.get('gained',0)} | {s['score']} | "
            f"{s['max_tile']} | {s.get('empties_before','')} | {(s.get('latency_ms') or 0)/1000:.1f}s | "
            f"{s.get('reasoning_chars') or 0:,} | {s.get('how','')}{' ⚠' if s.get('fallback') else ''} |"
        )
    lines += [
        "",
        "## 回放",
        "",
        "- `demo.html`：播放器（空格播放、←→ 单步、↑↓ 倍速、拖时间轴、右侧看完整思考）",
        "- `demo.json`：完整数据（可离线重放）",
        "- `run.jsonl`：原始逐步记录",
    ]
    return "\n".join(lines) + "\n"


def main() -> int:
    if len(sys.argv) < 2:
        print("usage: py -3 build_demo.py runs/<run-id>", file=sys.stderr)
        return 2
    run_dir = pathlib.Path(sys.argv[1])
    if not run_dir.is_absolute():
        run_dir = ROOT / run_dir
    meta, steps = load_run(run_dir)
    if not steps:
        print("no steps recorded", file=sys.stderr)
        return 1
    stats = compute_stats(meta, steps)
    demo = build_demo_json(meta, steps)
    (run_dir / "demo.json").write_text(json.dumps(demo, ensure_ascii=False), encoding="utf-8")
    (run_dir / "demo.html").write_text(build_demo_html(meta, steps), encoding="utf-8")
    (run_dir / "report.html").write_text(build_report_html(meta, steps, stats), encoding="utf-8")
    (run_dir / "REPORT.md").write_text(build_report_md(meta, steps, stats), encoding="utf-8")
    print(json.dumps({
        "run_dir": str(run_dir),
        "moves": stats["moves"],
        "score": stats["final"].get("score"),
        "max_tile": stats["final"].get("max_tile"),
        "demo_bytes": (run_dir / "demo.html").stat().st_size,
        "report_bytes": (run_dir / "report.html").stat().st_size,
    }, ensure_ascii=False, indent=2))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
