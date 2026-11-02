"""Walk the Unreal sandbox with the PlayGoblin plugin and check the report against the planted bugs.

  python scripts/verify_unreal.py --editor "C:/Program Files/Epic Games/UE_5.8/Engine/Binaries/Win64/UnrealEditor-Cmd.exe"

The project must be built first (see unreal/README.md). The sandbox started normally has three planted bugs
(a trapdoor, a step the character cannot climb, an error after the first fall); started with -PGClean it has none
and PlayGoblin must report nothing.
"""
from __future__ import annotations

import argparse
import shutil
import subprocess
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
sys.path.insert(0, str(ROOT / "cli"))

from playgoblin.config import Config  # noqa: E402
from playgoblin.pipeline import analyze  # noqa: E402

PROJECT = ROOT / "samples" / "unreal-sandbox" / "PlayGoblinSandbox.uproject"
EXPECTED = [("out_of_world", ""), ("stuck", ""), ("exception", "DeathScreen")]
# The generated grey sandbox is made of engine cubes and the default material, so it triggers prototype_look on purpose:
# expected and reported, but not counted as a missed bug or a false positive.
EXPECTED_EXTRA = "prototype_look"


def walk(editor: str, clean: bool, seed: int, seconds: int, out: Path, log: Path, render: bool, extra: list[str]) -> Path:
    shutil.rmtree(out, ignore_errors=True)
    cmd = [editor, str(PROJECT), "-game", "-playgoblin", f"-PlayGoblinSeconds={seconds}", f"-PlayGoblinSeed={seed}",
           f"-PlayGoblinOut={out}", "-PlayGoblinTimeScale=3", "-PlayGoblinExit", "-unattended", "-nosplash",
           "-nosound", f"-abslog={log}"]
    if not render:
        cmd.append("-nullrhi")
    if clean:
        cmd.append("-PGClean")
    cmd += extra
    subprocess.run(cmd, check=False)
    runs = sorted(p for p in out.glob("*") if (p / "run.json").is_file())
    if not runs:
        raise SystemExit(f"no run folder produced; see {log}")
    return runs[-1]


def main() -> int:
    ap = argparse.ArgumentParser()
    ap.add_argument("--editor", required=True, help="path to UnrealEditor-Cmd.exe (or UnrealEditor.exe to render frames)")
    ap.add_argument("--seconds", type=int, default=150)
    ap.add_argument("--seeds", type=int, nargs="+", default=[1])
    ap.add_argument("--render", action="store_true", help="render frames (otherwise -nullrhi: no frames)")
    ap.add_argument("--game-arg", action="append", default=[], help="extra argument for the game, e.g. --game-arg=-PlayGoblinKeys")
    ap.add_argument("--out", default=str(ROOT / "out" / "verify-unreal"))
    args = ap.parse_args()
    out = Path(args.out)
    out.mkdir(parents=True, exist_ok=True)

    detected = {e: 0 for e in EXPECTED}
    clean_findings: list[str] = []
    proto: dict[str, int] = {}
    for seed in args.seeds:
        print(f"\n== seed {seed} ==")
        run = walk(args.editor, False, seed, args.seconds, out / f"broken-s{seed}", out / f"broken-s{seed}.log", args.render, args.game_arg)
        findings, _ = analyze([run], out / f"broken-report-s{seed}", Config())
        proto[f"broken s{seed}"] = sum(f.type == EXPECTED_EXTRA for f in findings)
        for typ, text in EXPECTED:
            hit = any(f.type == typ and text.lower() in (f.title + f.detail).lower() for f in findings)
            detected[(typ, text)] += hit
            print(f"  {'found  ' if hit else 'MISSED '} {typ} {text}")
        run = walk(args.editor, True, seed, args.seconds, out / f"clean-s{seed}", out / f"clean-s{seed}.log", args.render, args.game_arg)
        clean, _ = analyze([run], out / f"clean-report-s{seed}", Config())
        proto[f"clean s{seed}"] = sum(f.type == EXPECTED_EXTRA for f in clean)
        clean = [f for f in clean if f.type != EXPECTED_EXTRA]
        clean_findings += [f"seed {seed} {f.type}: {f.title}" for f in clean]
        print(f"  clean sandbox: {len(clean)} finding(s) besides {EXPECTED_EXTRA}")

    n = len(args.seeds)
    print(f"\nRecall over {n} seed(s)")
    for (typ, text), v in detected.items():
        print(f"  {v}/{n}  {typ} {text}")
    print(f"{EXPECTED_EXTRA} (expected on the generated grey level, reported only): " + ", ".join(f"{k}={v}" for k, v in proto.items()))
    print(f"False positives on the clean sandbox: {len(clean_findings)}")
    for line in clean_findings:
        print("   ", line)
    ok = all(v == n for v in detected.values()) and not clean_findings
    print("\nOK" if ok else "\nFAILED")
    return 0 if ok else 1


if __name__ == "__main__":
    raise SystemExit(main())
