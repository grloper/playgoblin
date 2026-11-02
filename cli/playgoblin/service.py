"""The two things the CLI and the MCP server both do: walk a game, and turn run folders into a report."""
from __future__ import annotations

import shutil
from pathlib import Path
from typing import Callable

from .client import Client
from .config import Config
from .pipeline import analyze


def run_walks(client: Client, minutes: float, seed: int, runs: int, out: Path, config: Config,
              summarize=None, say: Callable[[str], None] = lambda s: None, on_tick=None):
    """Start `runs` walks one after another in the game the client is connected to, then build the report.

    Returns (findings, report_paths, run_dirs). Raises PlayGoblinError if the game cannot be reached."""
    client.status()  # fail fast with a clear message when the game is not running
    run_dirs: list[Path] = []
    for i in range(runs):
        s = seed + i
        say(f"Run {i + 1}/{runs}: seed {s}, {minutes:g} min")
        client.start(s, minutes)
        status = client.wait_for_end(on_tick=on_tick, timeout=minutes * 60 * 4 + 120)
        say(f"  ended: {status.get('end_reason')}")
        local = client.download_run(status, out / "runs" / (status.get("run_id") or f"seed{s}"))
        kept = out / "runs" / local.name
        if local.resolve() != kept.resolve():
            shutil.copytree(local, kept, dirs_exist_ok=True)
        run_dirs.append(kept)
    findings, paths = analyze(run_dirs, out, config, summarize)
    return findings, paths, run_dirs


def build_report(run_dirs: list[Path], out: Path, config: Config, summarize=None):
    """Offline: no game, network or API key needed (unless `summarize` is given)."""
    return analyze(run_dirs, out, config, summarize)
