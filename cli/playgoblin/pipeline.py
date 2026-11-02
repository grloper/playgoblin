"""Run directories in, report out. Pure offline: no Unity, no network, no API key."""
from __future__ import annotations

from pathlib import Path

from . import dedupe, evidence, report
from .checks import run_checks
from .config import Config
from .model import Finding, Run, load_run


def analyze(run_dirs: list[Path], out_dir: Path, config: Config, summarize=None) -> tuple[list[Finding], dict[str, Path]]:
    runs: list[Run] = [load_run(d) for d in run_dirs]
    if not runs:
        raise ValueError("no runs to analyze")
    raw: list[Finding] = []
    for run in runs:
        raw += run_checks(run, config)
    findings = dedupe.merge(raw, runs_total=len(runs), config=config)
    for i, f in enumerate(findings, 1):
        f.data["id"] = f"F{i}"
    evidence.attach(findings, runs, out_dir)
    summary = summarize([f.to_dict() for f in findings]) if summarize else None
    paths = report.write_reports(findings, runs, out_dir, summary)
    return findings, paths
