from __future__ import annotations

from ..model import Finding


def check(run, config):
    """Placeholder or TODO text visible in the UI (reported by the Unity package as placeholder_text events)."""
    for e in run.events:
        if e.type != "placeholder_text":
            continue
        where = e.data.get("path", "")
        yield Finding(type="debug_leftover", severity="medium", title=f"Placeholder text on screen: \"{e.detail[:60]}\"",
                      t=e.t, detail=f"UI object {where}" if where else "", key="text:" + e.detail.strip().lower(),
                      data=dict(e.data))
