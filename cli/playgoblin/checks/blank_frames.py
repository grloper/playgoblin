from __future__ import annotations

from .. import frames
from ..model import Finding


def check(run, config):
    """Blank or all-magenta frames. Frames close to a scene change are skipped (loading screens)."""
    if run.frames_dir is None or not run.frames_dir.is_dir() or not frames.available():
        return
    scene_changes = [e.t for e in run.events if e.type == "scene_change"]
    flagged: list[tuple[float, str, str]] = []
    for path in frames.list_frames(run.frames_dir):
        t = frames.frame_time(path)
        if t is None:
            continue
        if any(abs(t - s) <= config.blank_frame_skip_seconds for s in scene_changes):
            continue
        stats = frames.analyze(path)
        rel = path.relative_to(run.directory).as_posix()
        if stats.blank:
            flagged.append((t, "blank", rel))
        elif stats.pink_percent >= config.pink_area_percent:
            flagged.append((t, "pink", rel))

    # Consecutive flagged frames (2 s apart or less) of the same kind become one finding.
    flagged.sort()
    groups: list[list[tuple[float, str, str]]] = []
    for item in flagged:
        if groups and groups[-1][-1][1] == item[1] and item[0] - groups[-1][-1][0] <= 2.0:
            groups[-1].append(item)
        else:
            groups.append([item])
    for g in groups:
        paths = [p for _, _, p in g]
        if g[0][1] == "blank":
            yield Finding(type="blank_frame", severity="high", title="Screen rendered blank", t=g[0][0],
                          detail=f"{len(g)} frame(s) with almost no color variation", key="blank",
                          evidence=paths, data={"frames": len(g)})
        else:
            yield Finding(type="debug_leftover", severity="medium", title="Missing-material pink on screen",
                          t=g[0][0], detail=f"Magenta covers {config.pink_area_percent}%+ of {len(g)} frame(s)",
                          key="pink", evidence=paths, data={"frames": len(g)})
