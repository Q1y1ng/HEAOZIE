"""Summarise an engine game (engine_bb --save JSON): phase timing, empties, score rate.

Usage:
  py -3 analyze_game.py runs/batchC/s33.json [more.json ...]
"""
from __future__ import annotations

import json
import pathlib
import statistics
import sys


def level_of(v: int) -> int:
    return 0 if v <= 0 else v.bit_length() - 1


def analyse(path: pathlib.Path) -> dict:
    data = json.loads(path.read_text(encoding="utf-8"))
    steps = data["steps"]
    meta = data.get("meta", {})
    first: dict[int, int] = {}
    empties_by_step: list[int] = []
    for st in steps:
        grid = st.get("grid_after") or []
        mx = 0
        e = 0
        for row in grid:
            for v in row:
                if v <= 0:
                    e += 1
                else:
                    mx = max(mx, level_of(v))
        empties_by_step.append(e)
        tile = 1 << mx if mx else 0
        if tile and tile not in first:
            first[tile] = st["i"]
    total = steps[-1]["i"] if steps else 0
    score = steps[-1]["score"] if steps else 0
    moves = len(steps)
    tail = empties_by_step[-500:] or [0]
    phases = []
    prev_i, prev_tile = 0, 0
    for tile in sorted(first):
        at = first[tile]
        span = at - prev_i
        gain = st_score_at(steps, at) - st_score_at(steps, prev_i)
        phases.append({"tile": tile, "reached_at": at, "moves_spent": span,
                       "points": gain,
                       "pts_per_move": round(gain / span, 2) if span else 0})
        prev_i, prev_tile = at, tile
    return {
        "file": path.name,
        "seed": meta.get("seed"),
        "budget_ms": meta.get("budget_ms"),
        "moves": moves,
        "score": score,
        "max_tile": 1 << max(level_of(0), 0) if False else (1 << max((level_of(v) for row in steps[-1]["grid_after"] for v in row), default=0)),
        "pts_per_move": round(score / moves, 2) if moves else 0,
        "avg_empties": round(statistics.mean(empties_by_step), 2),
        "avg_empties_last500": round(statistics.mean(tail), 2),
        "min_empties_last500": min(tail),
        "phases": phases,
    }


def st_score_at(steps: list[dict], i: int) -> int:
    for st in steps:
        if st["i"] >= i:
            return st["score"]
    return steps[-1]["score"] if steps else 0


def main() -> int:
    if len(sys.argv) < 2:
        print(__doc__)
        return 2
    rows = []
    for raw in sys.argv[1:]:
        p = pathlib.Path(raw)
        if not p.exists():
            print(f"missing {p}")
            continue
        rows.append(analyse(p))
    for r in rows:
        print(f"\n== {r['file']}  seed={r['seed']}  {r['budget_ms']} ms/步 ==")
        print(f"  {r['moves']} 步 · {r['score']:,} 分 · 最大块 {r['max_tile']} · "
              f"{r['pts_per_move']} 分/步")
        print(f"  平均空格 {r['avg_empties']}（最后 500 步 {r['avg_empties_last500']}，"
              f"最少 {r['min_empties_last500']}）")
        print("  里程碑          到达步数   耗时步数   阶段得分   分/步")
        for ph in r["phases"]:
            print(f"  {ph['tile']:>7}  {ph['reached_at']:>10}  {ph['moves_spent']:>9}  "
                  f"{ph['points']:>9}  {ph['pts_per_move']:>6}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
