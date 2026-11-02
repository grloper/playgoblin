"""Attach evidence to findings: copy the frames around the moment it happened and, when ffmpeg is installed,
turn them into a short MP4 clip. Everything is copied under the report directory so the report is self-contained."""
from __future__ import annotations

import shutil
import subprocess
from pathlib import Path

from . import frames
from .model import Finding, Run

WINDOW_SECONDS = 3.0
MAX_FRAMES = 12


def ffmpeg_path() -> str | None:
    return shutil.which("ffmpeg")


def attach(findings: list[Finding], runs: list[Run], out_dir: Path) -> None:
    """Fills finding.evidence with paths relative to out_dir. Findings are expected to have ids in finding.data['id']."""
    by_id = {r.run_id: r for r in runs}
    for f in findings:
        run = by_id.get(f.run_id)
        if run is None or run.directory is None:
            f.evidence = []
            continue
        fid = f.data["id"]
        dest = out_dir / "evidence" / fid
        picked = _frames_for(f, run)
        if not picked:
            f.evidence = []
            continue
        dest.mkdir(parents=True, exist_ok=True)
        copied: list[Path] = []
        for p in picked:
            target = dest / p.name
            shutil.copyfile(p, target)
            copied.append(target)
        evidence = [c.relative_to(out_dir).as_posix() for c in copied]
        clip = _make_clip(copied, dest / "clip.mp4")
        if clip is not None:
            evidence.insert(0, clip.relative_to(out_dir).as_posix())
        f.evidence = evidence


def _frames_for(f: Finding, run: Run) -> list[Path]:
    if run.frames_dir is None or not run.frames_dir.is_dir():
        return []
    if f.evidence:  # frame checks already know which frames proved the finding (paths relative to the run dir)
        return [run.directory / e for e in f.evidence[:MAX_FRAMES] if (run.directory / e).is_file()]
    timed = [(frames.frame_time(p), p) for p in frames.list_frames(run.frames_dir)]
    near = [(t, p) for t, p in timed if t is not None and abs(t - f.t) <= WINDOW_SECONDS]
    near.sort(key=lambda x: x[0])
    if len(near) > MAX_FRAMES:
        step = len(near) / MAX_FRAMES
        near = [near[int(i * step)] for i in range(MAX_FRAMES)]
    return [p for _, p in near]


def _make_clip(frame_files: list[Path], out: Path) -> Path | None:
    ffmpeg = ffmpeg_path()
    if ffmpeg is None or len(frame_files) < 3:
        return None
    listing = out.parent / "frames.txt"
    lines = []
    for p in frame_files:
        lines.append(f"file '{p.name}'")
        lines.append("duration 0.5")
    lines.append(f"file '{frame_files[-1].name}'")
    listing.write_text("\n".join(lines) + "\n", encoding="utf-8")
    try:
        result = subprocess.run(
            [ffmpeg, "-y", "-loglevel", "error", "-f", "concat", "-safe", "0", "-i", listing.name,
             "-vf", "scale=trunc(iw/2)*2:trunc(ih/2)*2,format=yuv420p", "-r", "10", "-movflags", "+faststart", out.name],
            cwd=out.parent, capture_output=True, text=True, timeout=60)
    except (OSError, subprocess.TimeoutExpired):
        return None
    finally:
        listing.unlink(missing_ok=True)
    return out if result.returncode == 0 and out.exists() else None
