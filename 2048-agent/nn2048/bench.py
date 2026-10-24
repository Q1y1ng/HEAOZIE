"""Run N engine games in parallel and print score statistics.

Usage:
  py -3 bench.py --seeds 8 --budget 300 --parallel 8 --size 4 -- --w-empty 200 --w-merges 900
  py -3 bench.py --exe engine_bb.exe --seeds 8 --budget 100 --parallel 8
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


def run_game(exe: pathlib.Path, seed: int, size: int, budget: float, extra: list[str],
             save_path: pathlib.Path | None = None) -> dict | None:
    args = [str(exe), "--size", str(size), "--games", "1", "--seed", str(seed),
            "--budget", str(budget), *extra]
    if save_path is not None:
        save_path.parent.mkdir(parents=True, exist_ok=True)
        args += ["--save", str(save_path)]
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
    ap.add_argument("--exe", default="engine4.exe",
                    help="engine binary in this directory (default engine4.exe)")
    ap.add_argument("--seeds", type=int, default=8)
    ap.add_argument("--budget", type=float, default=300)
    ap.add_argument("--parallel", type=int, default=8)
    ap.add_argument("--size", type=int, default=4)
    ap.add_argument("--start-seed", type=int, default=1)
    ap.add_argument("--save-dir", default="",
                    help="record every game's step JSON here (engine --save)")
    ap.add_argument("--tag", default="", help="label for the summary line")
    ap.add_argument("extra", nargs="*", help="extra engine args after --")
    args = ap.parse_args()
    extra = args.extra
    if extra and extra[0] == "--":
        extra = extra[1:]
    exe = pathlib.Path(args.exe)
    if not exe.is_absolute():
        exe = HERE / exe
    if not exe.exists():
        print(f"missing engine: {exe}")
        return 1

    games: list[dict] = []
    save_dir = pathlib.Path(args.save_dir) if args.save_dir else None
    with cf.ThreadPoolExecutor(max_workers=args.parallel) as pool:
        futs = {
            pool.submit(run_game, exe, args.start_seed + i, args.size, args.budget, extra,
                        (save_dir / f"s{args.start_seed + i}.json") if save_dir else None): args.start_seed + i
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
        "tag": args.tag,
        "games": len(games),
        "size": args.size,
        "budget": args.budget,
        "parallel": args.parallel,
        "mean": round(statistics.mean(scores), 1),
        "median": statistics.median(scores),
        "min": min(scores),
        "max": max(scores),
        "tiles": sorted({g["max_tile"] for g in games}),
        "avg_seconds": round(statistics.mean(g["seconds"] for g in games), 1),
        "avg_depth": round(statistics.mean(g["avg_depth"] for g in games), 2),
        "avg_nps": round(statistics.mean(g.get("nps", 0) for g in games) / 1e6, 1),
    }, ensure_ascii=False))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())

