"""Merge findings across runs: same type and same key (stack hash) or same place (within a radius)."""
from __future__ import annotations

from .config import Config
from .model import SEVERITY_ORDER, Finding, distance_xz

POSITIONAL = {"stuck", "out_of_world", "trapped"}


def _same(a: Finding, b: Finding, radius: float) -> bool:
    if a.type != b.type:
        return False
    if a.type in POSITIONAL and a.position and b.position:
        return distance_xz(a.position, b.position) <= radius
    return a.key == b.key and (a.key != "" or a.title == b.title)


def merge(findings: list[Finding], runs_total: int, config: Config) -> list[Finding]:
    merged: list[Finding] = []
    seen_runs: list[set[str]] = []
    for f in sorted(findings, key=lambda x: (x.run_id, x.t)):
        for i, m in enumerate(merged):
            if _same(m, f, config.merge_radius_m):
                m.occurrences += f.occurrences
                seen_runs[i].add(f.run_id)
                m.evidence.extend(e for e in f.evidence if e not in m.evidence)
                break
        else:
            merged.append(f)
            seen_runs.append({f.run_id})

    for f, runs in zip(merged, seen_runs):
        f.runs_seen = len(runs)
        f.runs_total = runs_total
        f.confidence, f.confirmed = _confidence(f, config)
    merged.sort(key=lambda f: (not f.confirmed, SEVERITY_ORDER.get(f.severity, 9), -f.runs_seen, f.t))
    return merged


def _confidence(f: Finding, config: Config) -> tuple[str, bool]:
    if f.type == "stuck":
        # Only trust a stuck spot that repeats in the same place (2+ times, or in 2+ runs).
        if f.occurrences >= config.stuck_min_occurrences or f.runs_seen >= 2:
            return ("high" if f.runs_seen >= 2 else "medium"), True
        return "low", False
    if f.type in ("exception", "setup_error"):
        return "high", True
    if f.runs_total > 1 and f.runs_seen == f.runs_total:
        return "high", True
    return "medium", True
