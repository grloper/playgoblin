from __future__ import annotations

from ..model import Finding


def check(run, config):
    for e in run.events:
        if e.type != "trapped":
            continue
        d = e.data
        yield Finding(
            type="trapped", severity="high", title="Player spawns in a small enclosed area", t=e.t,
            detail=f"Can reach {d.get('reachable_cells', '?')} of {d.get('walkable_cells', '?')} walkable cells "
                   f"(cells are {d.get('cell_m', '?')} m)",
            position=e.pos, data=dict(d))
