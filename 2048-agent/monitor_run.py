"""Sample a running 2048 agent run into monitor.log (GPU + progress + phase).

Usage:
  py -3 monitor_run.py runs/qwen38-9b-plain-s1b 30
"""
from __future__ import annotations

import json
import pathlib
import subprocess
import sys
import time
from datetime import datetime


def read_json(path: pathlib.Path) -> dict:
    try:
        return json.loads(path.read_text(encoding="utf-8"))
    except Exception:  # noqa: BLE001 - a partial write must not kill the monitor
        return {}


def main() -> int:
    run_dir = pathlib.Path(sys.argv[1] if len(sys.argv) > 1 else "runs/qwen38-9b-plain-s1b")
    interval = float(sys.argv[2]) if len(sys.argv) > 2 else 30.0
    log_path = run_dir / "monitor.log"
    print(f"monitoring {run_dir} every {interval}s -> {log_path}", flush=True)
    while True:
        meta = read_json(run_dir / "meta.json")
        status = read_json(run_dir / "status.json")
        try:
            gpu = subprocess.check_output(
                [
                    "nvidia-smi",
                    "--query-gpu=memory.free,memory.used,utilization.gpu,temperature.gpu",
                    "--format=csv,noheader",
                ],
                text=True,
                timeout=8,
            ).strip()
        except Exception as exc:  # noqa: BLE001
            gpu = f"nvidia-smi-error:{exc}"
        line = (
            f"{datetime.now().astimezone().isoformat(timespec='seconds')} "
            f"status={meta.get('status')} moves={meta.get('moves_done')} "
            f"score={meta.get('score')} max={meta.get('max_tile')} "
            f"phase={status.get('phase')} thinking_step={status.get('step')} "
            f"reasoning_chars={len(status.get('reasoning') or '')} gpu={gpu}"
        )
        with open(log_path, "a", encoding="utf-8") as fh:
            fh.write(line + "\n")
        print(line, flush=True)
        if meta.get("status") not in ("running", "starting"):
            print("run finished; monitor exiting", flush=True)
            break
        time.sleep(interval)
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
