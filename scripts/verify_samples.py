"""Walk the two sample games with a real Unity editor and check PlayGoblin's report against the planted bugs.

  python scripts/verify_samples.py --unity "C:/Program Files/Unity/Hub/Editor/6000.4.0f1/Editor/Unity.exe"

Broken must produce every kind of finding below. Clean (same level, no planted bugs) must produce none at all,
confirmed or not. Needs a Unity install; it opens a window (frames cannot be captured under -nographics).
"""
from __future__ import annotations

import argparse
import json
import os
import shutil
import subprocess
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
sys.path.insert(0, str(ROOT / "cli"))

from playgoblin.config import Config  # noqa: E402
from playgoblin.pipeline import analyze  # noqa: E402

# (finding type, text that must appear in the title) for each planted bug.
EXPECTED = [
    ("exception", "NullReferenceException"),
    ("out_of_world", ""),
    ("stuck", ""),
    ("dead_button", "Options"),
    ("debug_leftover", "Placeholder text"),
    ("debug_leftover", "pink"),
    ("fps_drop", ""),
    ("blank_frame", ""),
]


def walk(unity: str, scene: str, out: Path, seconds: int, seed: int, log: Path) -> Path:
    shutil.rmtree(out, ignore_errors=True)
    env = dict(os.environ, PLAYGOBLIN_SAMPLE_SCENE=scene, PLAYGOBLIN_SAMPLE_OUT=str(out), PLAYGOBLIN_SAMPLE_SECONDS=str(seconds),
               PLAYGOBLIN_SAMPLE_SEED=str(seed))
    cmd = [unity, "-batchmode", "-projectPath", str(ROOT / "samples" / "broken-game"), "-runTests", "-testPlatform",
           "PlayMode", "-testFilter", "BrokenGame.Tests", "-testResults", str(log.with_suffix(".xml")), "-logFile", str(log)]
    code = subprocess.run(cmd, env=env).returncode
    runs = sorted(p for p in out.glob("*") if (p / "run.json").is_file())
    if code != 0 or not runs:
        raise SystemExit(f"Unity run for {scene} failed (exit {code}); see {log}")
    return runs[-1]


def main() -> int:
    ap = argparse.ArgumentParser()
    ap.add_argument("--unity", required=True)
    ap.add_argument("--seconds", type=int, default=120)
    ap.add_argument("--seeds", type=int, nargs="+", default=[7])
    ap.add_argument("--out", default=str(ROOT / "out" / "verify"))
    args = ap.parse_args()
    out = Path(args.out)
    out.mkdir(parents=True, exist_ok=True)

    detected = {i: 0 for i in range(len(EXPECTED))}
    clean_findings: list[str] = []
    broken_extra: list[str] = []
    for seed in args.seeds:
        print(f"\n== seed {seed} ==")
        run = walk(args.unity, "Broken", out / f"broken-s{seed}", args.seconds, seed, out / f"broken-s{seed}.log")
        findings, _ = analyze([run], out / f"broken-report-s{seed}", Config())
        for i, (typ, text) in enumerate(EXPECTED):
            hit = any(f.type == typ and text.lower() in f.title.lower() for f in findings)
            detected[i] += hit
            print(f"  {'found  ' if hit else 'MISSED '} {typ} {text}")
        broken_extra += [f"{f.type}: {f.title}" for f in findings
                         if not any(f.type == t and x.lower() in f.title.lower() for t, x in EXPECTED)]
        run = walk(args.unity, "Clean", out / f"clean-s{seed}", args.seconds, seed, out / f"clean-s{seed}.log")
        clean, _ = analyze([run], out / f"clean-report-s{seed}", Config())
        clean_findings += [f"seed {seed} {f.type}: {f.title}" for f in clean]
        print(f"  clean sample: {len(clean)} finding(s)")

    n = len(args.seeds)
    print(f"\nRecall over {n} seed(s), one 120 s run each")
    for i, (typ, text) in enumerate(EXPECTED):
        print(f"  {detected[i]}/{n}  {typ} {text}")
    print(f"False positives on the clean sample: {len(clean_findings)} across {n} run(s)")
    for line in clean_findings:
        print("   ", line)
    for line in broken_extra:
        print("  note: unexpected finding in broken sample:", line)

    ok = all(v == n for v in detected.values()) and not clean_findings
    (out / "verify.json").write_text(json.dumps({"seeds": args.seeds, "detected": {EXPECTED[i][0] + ":" + EXPECTED[i][1]: v for i, v in detected.items()},
                                                 "clean_findings": clean_findings, "passed": ok}, indent=2), encoding="utf-8")
    print("\nOK" if ok else "\nFAILED")
    return 0 if ok else 1


if __name__ == "__main__":
    raise SystemExit(main())
