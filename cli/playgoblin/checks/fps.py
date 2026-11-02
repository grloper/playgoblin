from __future__ import annotations

from ..model import Finding


def check(run, config):
    """Sustained frame-time spikes reported by the Unity package (it only emits them when fpsBudgetMs is set)."""
    for e in run.events:
        if e.type != "fps_drop":
            continue
        ms = float(e.data.get("ms", 0) or 0)
        if config.fps_budget_ms is not None and ms < config.fps_budget_ms:
            continue
        secs = e.data.get("seconds", "?")
        yield Finding(type="fps_drop", severity="medium", title="Frame rate dropped", t=e.t,
                      detail=f"Average {ms:.0f} ms/frame for {secs} s (budget {e.data.get('budget_ms', '?')} ms)",
                      position=e.pos, key="fps", data=dict(e.data))
