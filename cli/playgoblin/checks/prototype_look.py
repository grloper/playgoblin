from __future__ import annotations

from ..model import Finding

REASONS = {
    "basic_shape": "an engine basic shape mesh (cube, sphere, ...)",
    "default_material": "the engine default or grid material",
}


def check(run, config):
    """Visible actors that still look like a prototype (reported by the Unreal plugin as prototype_look events).

    One finding per actor. The engine side caps events at 50 per run; `data.total` says how many it found,
    so the finding detail says when more exist than were reported.
    """
    for e in run.events:
        if e.type != "prototype_look":
            continue
        reason = e.data.get("reason", "")
        parts = [f"{k}: {e.data[k]}" for k in ("mesh", "material", "component") if e.data.get(k)]
        total = e.data.get("total", "")
        if total.isdigit() and int(total) > 50:
            parts.append(f"{total} actors look like a prototype; only the first 50 are reported")
        yield Finding(type="prototype_look", severity="medium",
                      title=f"Prototype look: {e.detail} uses {REASONS.get(reason, reason or 'placeholder art')}",
                      t=e.t, detail="; ".join(parts), position=e.pos, key="prototype:" + e.detail, data=dict(e.data))
