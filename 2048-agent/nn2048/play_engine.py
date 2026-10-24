"""Play full games with the expectimax engine and report score statistics.

Usage:
  py -3 play_engine.py --size 4 --games 10 --budget 0.1
  py -3 play_engine.py --size 5 --games 5 --budget 0.2
  py -3 play_engine.py --size 4 --games 1 --save runs-nn/game.json

`--save` writes a replay-ready JSON (meta + steps) compatible with
`build_demo.py` conventions (grid_before / grid_after / move / score ...).
"""
from __future__ import annotations

import argparse
import json
import pathlib
import random
import statistics
import time

from core import DIRS, best_move, evaluate, legal_moves, max_tile, move, spawn


def play_one(
    n: int,
    seed: int,
    budget: float,
    max_depth: int,
    prob4: float,
    max_moves: int = 100000,
    keep_steps: bool = False,
) -> dict:
    rng = random.Random(seed)
    board = spawn(spawn(tuple([0] * (n * n)), rng, prob4), rng, prob4)
    score = 0
    steps = []
    started = time.time()
    depths = []
    while True:
        moves = legal_moves(board, n)
        if not moves:
            break
        d, v, st = best_move(board, n, time_budget=budget, max_depth=max_depth, prob4=prob4)
        if keep_steps:
            values = {}
            for cand in moves:
                nb, gain = move(board, cand, n)
                values[cand] = round(gain + evaluate(nb, n), 1)
        before = board
        nb, gain = move(board, d, n)
        score += gain
        board = spawn(nb, rng, prob4)
        depths.append(st.depth_reached)
        if keep_steps:
            steps.append(
                {
                    "move": d,
                    "gained": gain,
                    "score": score,
                    "grid_before": [list(before[i * n:(i + 1) * n]) for i in range(n)],
                    "grid_after": [list(board[i * n:(i + 1) * n]) for i in range(n)],
                    "values": values,
                    "depth": st.depth_reached,
                    "nodes": st.nodes,
                }
            )
        if len(steps) >= max_moves:
            break
    return {
        "seed": seed,
        "size": n,
        "score": score,
        "moves": len(steps) if keep_steps else None,
        "max_tile": 1 << max_tile(board) if max_tile(board) else 0,
        "seconds": round(time.time() - started, 2),
        "avg_depth": round(statistics.mean(depths), 2) if depths else 0,
        "steps": steps if keep_steps else None,
    }


def main() -> int:
    ap = argparse.ArgumentParser()
    ap.add_argument("--size", type=int, default=4)
    ap.add_argument("--games", type=int, default=10)
    ap.add_argument("--seed", type=int, default=1)
    ap.add_argument("--budget", type=float, default=0.1, help="seconds per move")
    ap.add_argument("--max-depth", type=int, default=4)
    ap.add_argument("--prob4", type=float, default=0.1)
    ap.add_argument("--save", default=None, help="write one game as JSON for the demo")
    args = ap.parse_args()

    if args.save:
        game = play_one(args.size, args.seed, args.budget, args.max_depth, args.prob4, keep_steps=True)
        out = pathlib.Path(args.save)
        out.parent.mkdir(parents=True, exist_ok=True)
        out.write_text(json.dumps(game, ensure_ascii=False), encoding="utf-8")
        print(json.dumps({k: v for k, v in game.items() if k != "steps"}, indent=2))
        return 0

    rows = []
    for i in range(args.games):
        g = play_one(args.size, args.seed + i, args.budget, args.max_depth, args.prob4)
        rows.append(g)
        print(json.dumps(g, ensure_ascii=False), flush=True)
    scores = [r["score"] for r in rows]
    tiles = sorted({r["max_tile"] for r in rows})
    print(json.dumps({
        "size": args.size,
        "games": len(rows),
        "mean": round(statistics.mean(scores), 1),
        "median": statistics.median(scores),
        "min": min(scores),
        "max": max(scores),
        "tiles": tiles,
        "avg_seconds": round(statistics.mean(r["seconds"] for r in rows), 1),
    }, ensure_ascii=False))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
