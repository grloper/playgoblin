from __future__ import annotations

from ..model import Finding


def check(run, config):
    for e in run.events:
        if e.type != "stuck":
            continue
        detail = e.detail
        if e.data.get("blocked_by"):
            detail += f". Blocked by: {e.data['blocked_by']}"
        if e.data.get("controller"):
            detail += f". Controller: {e.data['controller']}"
        yield Finding(type="stuck", severity="medium", title="Player got stuck", t=e.t, detail=detail,
                      position=e.pos, data=dict(e.data))
