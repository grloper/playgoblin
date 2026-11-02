from __future__ import annotations

from ..model import Finding


def check(run, config):
    """Buttons the MenuCrawler clicked twice without seeing any change."""
    for e in run.events:
        if e.type != "dead_button":
            continue
        path = e.data.get("path", e.detail)
        label = e.data.get("label") or e.detail
        yield Finding(type="dead_button", severity="medium", title=f"Button does nothing: \"{label}\"", t=e.t,
                      detail=f"{path} (scene {e.data.get('scene', '?')}). Clicked twice; no UI change, scene change or log output.",
                      key="button:" + path, data=dict(e.data))
