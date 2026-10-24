"""Run N engine games in parallel and print score statistics.

Usage:
  py -3 bench.py --seeds 8 --budget 300 --parallel 8 --size 4 -- --w-empty 200 --w-merges 900
"""
from __future__ import annotations

import argparse
import concurrent.futures as cf
import json
import pathlib
import statistics
import subprocess

HERE = pathlib.Path(__file__).resolve().parent
EXE = HERE / "engine4.exe"


def run_game(seed: int, size: int, budget: float, extra: list[str]) -> dict | None:
    args = [str(EXE), "--size", str(size), "--games", "1", "--seed", str(seed),
            "--budget", str(budget), *extra]
    out = subprocess.run(args, capture_output=True, text=True, timeout=7200)
    for line in out.stdout.splitlines():
        line = line.strip()
        if line.startswith("{"):
            try:
                obj = json.loads(line)
            except json.JSONDecodeError:
                continue
            if "score" in obj and "game" in obj:
                return obj
    return None


def main() -> int:
    ap = argparse.ArgumentParser()
    ap.add_argument("--seeds", type=int, default=8)
    ap.add_argument("--budget", type=float, default=300)
    ap.add_argument("--parallel", type=int, default=8)
    ap.add_argument("--size", type=int, default=4)
    ap.add_argument("--start-seed", type=int, default=1)
    ap.add_argument("extra", nargs="*", help="extra engine args after --")
    args = ap.parse_args()
    extra = args.extra
    if extra and extra[0] == "--":
        extra = extra[1:]

    games: list[dict] = []
    with cf.ThreadPoolExecutor(max_workers=args.parallel) as pool:
        futs = {
            pool.submit(run_game, args.start_seed + i, args.size, args.budget, extra): args.start_seed + i
            for i in range(args.seeds)
        }
        for fut in cf.as_completed(futs):
            g = fut.result()
            if g:
                games.append(g)
                print(json.dumps(g, ensure_ascii=False), flush=True)
    if not games:
        print("no games")
        return 1
    scores = [g["score"] for g in games]
    print(json.dumps({
        "games": len(games),
        "size": args.size,
        "budget": args.budget,
        "mean": round(statistics.mean(scores), 1),
        "median": statistics.median(scores),
        "min": min(scores),
        "max": max(scores),
        "tiles": sorted({g["max_tile"] for g in games}),
        "avg_seconds": round(statistics.mean(g["seconds"] for g in games), 1),
        "avg_depth": round(statistics.mean(g["avg_depth"] for g in games), 2),
    }, ensure_ascii=False))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())

