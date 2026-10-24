"""Build a self-contained replay.html from a run directory.

Usage:
  py -3 build_replay.py runs/plain-4b-s1
  py -3 build_replay.py runs/plain-4b-s1 runs/free-beam-s1b
"""
from __future__ import annotations

import json
import pathlib
import sys

ROOT = pathlib.Path(__file__).resolve().parent


def build(run_dir: pathlib.Path) -> pathlib.Path:
    meta = json.loads((run_dir / "meta.json").read_text(encoding="utf-8"))
    steps = [
        json.loads(line)
        for line in (run_dir / "run.jsonl").read_text(encoding="utf-8").splitlines()
        if line.strip()
    ]
    html = (ROOT / "viewer.html").read_text(encoding="utf-8")
    data = json.dumps({"meta": meta, "steps": steps}, ensure_ascii=False).replace("</", "<\\/")
    if "/*__RUN_DATA__*/" not in html:
        raise SystemExit("viewer.html 里找不到 /*__RUN_DATA__*/ 标记")
    html = html.replace("/*__RUN_DATA__*/", "window.__RUN_DATA__ = " + data + ";")
    out = run_dir / "replay.html"
    out.write_text(html, encoding="utf-8")
    print(out)
    return out


if __name__ == "__main__":
    args = sys.argv[1:]
    if not args:
        args = ["runs/plain-4b-s1", "runs/free-beam-s1b"]
    for raw in args:
        p = pathlib.Path(raw)
        if not p.is_absolute():
            p = ROOT / p
        build(p)
