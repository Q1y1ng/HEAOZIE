"""Weight sweep for the C++ 2048 engine.

Runs several heuristic weight sets on the same seeds, a few games in parallel,
and prints mean / max / tile distribution.

Usage:
  py -3 sweep.py --budget 100 --seeds 4 --parallel 4
  py -3 sweep.py --budget 300 --seeds 8 --parallel 4 --configs best
"""
from __future__ import annotations

import argparse
import json
import pathlib
import statistics
import subprocess
import sys

HERE = pathlib.Path(__file__).resolve().parent
EXE = HERE / "engine2.exe"

# name -> extra CLI args
CONFIGS = {
    "base": [],
    "smooth60": ["--w-smooth", "60"],
    "snake300": ["--w-snake", "300"],
    "cprox20": ["--w-cprox", "20"],
    "empty200_merge900": ["--w-empty", "200", "--w-merges", "900"],
    "mono70": ["--w-mono", "70"],
    "smooth120_snake600": ["--w-smooth", "120", "--w-snake", "600"],
}


def run_game(seed: int, budget: float, extra: list[str]) -> dict | None:
    args = [str(EXE), "--size", "4", "--games", "1", "--seed", str(seed), "--budget", str(budget), *extra]
    out = subprocess.run(args, capture_output=True, text=True, timeout=3600)
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


def sweep(names: list[str], seeds: int, budget: float, parallel: int) -> list[dict]:
    results = []
    for name in names:
        extra = CONFIGS[name]
        games: list[dict] = []
        pending: list[tuple[int, subprocess.Popen]] = []
        import concurrent.futures as cf

        with cf.ThreadPoolExecutor(max_workers=parallel) as pool:
            futs = {pool.submit(run_game, s, budget, extra): s for s in range(1, seeds + 1)}
            for fut in cf.as_completed(futs):
                g = fut.result()
                if g:
                    games.append(g)
        if not games:
            print(f"{name}: no games")
            continue
        scores = [g["score"] for g in games]
        tiles = sorted({g["max_tile"] for g in games})
        row = {
            "config": name,
            "games": len(games),
            "mean": round(statistics.mean(scores), 1),
            "median": statistics.median(scores),
            "max": max(scores),
            "min": min(scores),
            "tiles": tiles,
            "avg_sec": round(statistics.mean(g["seconds"] for g in games), 1),
            "avg_depth": round(statistics.mean(g["avg_depth"] for g in games), 2),
        }
        results.append(row)
        print(json.dumps(row, ensure_ascii=False), flush=True)
    return results


def main() -> int:
    ap = argparse.ArgumentParser()
    ap.add_argument("--budget", type=float, default=100)
    ap.add_argument("--seeds", type=int, default=4)
    ap.add_argument("--parallel", type=int, default=4)
    ap.add_argument("--configs", nargs="*", default=None, help="subset of config names")
    args = ap.parse_args()
    names = args.configs or list(CONFIGS)
    res = sweep(names, args.seeds, args.budget, args.parallel)
    print("\n== summary ==")
    for r in sorted(res, key=lambda x: -x["mean"]):
        print(f'{r["config"]:24s} mean={r["mean"]:9.1f}  max={r["max"]:7d}  tiles={r["tiles"]}  depth={r["avg_depth"]}')
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
