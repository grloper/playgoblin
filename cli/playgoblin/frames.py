"""Frame analysis: blank-frame gate and missing-material pink detection.

Frames are written by the Unity Recorder as frames/f<index>_t<seconds>.jpg or .png. Needs numpy and Pillow
(pip install playgoblin[frames]); without them the frame checks are skipped and everything else still works.
"""
from __future__ import annotations

import re
from dataclasses import dataclass
from pathlib import Path

try:
    import numpy as np
    from PIL import Image
except ImportError:  # optional dependency
    np = None
    Image = None

_NAME = re.compile(r"_t(\d+(?:\.\d+)?)\.(?:jpg|jpeg|png)$", re.IGNORECASE)


@dataclass
class FrameStats:
    blank: bool
    pink_percent: float
    color_std: float
    distinct_colors: int


def available() -> bool:
    return np is not None


def list_frames(directory: Path) -> list[Path]:
    return sorted(p for p in Path(directory).iterdir() if _NAME.search(p.name))


def frame_time(path: Path) -> float | None:
    m = _NAME.search(path.name)
    return float(m.group(1)) if m else None


def analyze(path: Path) -> FrameStats:
    """A frame is blank when it has almost no variation: one flat color, a black or white screen, or a clear color
    with nothing drawn. A dark but textured scene stays above the threshold because of its grain and gradients."""
    with Image.open(path) as im:
        im = im.convert("RGB")
        im.thumbnail((256, 256))
        a = np.asarray(im, dtype=np.int16)
    std = float(a.std(axis=(0, 1)).mean())
    # Quantize to 4 bits per channel so JPEG noise doesn't count as variety.
    q = (a >> 4).reshape(-1, 3)
    distinct = int(len(np.unique(q[:, 0] * 256 + q[:, 1] * 16 + q[:, 2])))
    magenta = (a[..., 0] > 200) & (a[..., 1] < 70) & (a[..., 2] > 200)
    return FrameStats(blank=std < 3.0 and distinct <= 6, pink_percent=float(magenta.mean() * 100),
                      color_std=std, distinct_colors=distinct)
