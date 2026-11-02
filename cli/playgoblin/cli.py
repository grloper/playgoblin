"""playgoblin run | report | file-issues | mcp"""
from __future__ import annotations

import argparse
import json
import sys
from pathlib import Path

from . import __version__, issues, llm
from .client import Client, PlayGoblinError
from .config import load_config
from .model import SEVERITY_ICON
from .pipeline import analyze
from .service import run_walks


def _say(args):
    """Progress goes to stderr with --json, so stdout stays a single JSON document."""
    return (lambda s: print(s, file=sys.stderr)) if getattr(args, "json", False) else print


def _emit(findings, paths, args, extra: dict | None = None) -> None:
    if getattr(args, "json", False):
        data = json.loads(Path(paths["json"]).read_text(encoding="utf-8"))
        data["report"] = {k: str(v) for k, v in paths.items()}
        data.update(extra or {})
        print(json.dumps(data, indent=2))
        return
    confirmed = [f for f in findings if f.confirmed]
    print(f"\n{len(confirmed)} finding(s), {len(findings) - len(confirmed)} unconfirmed")
    for f in findings:
        mark = "" if f.confirmed else " (unconfirmed)"
        print(f"  {SEVERITY_ICON.get(f.severity, '')} {f.data['id']} {f.title}{mark}")
    print(f"\nReport: {paths['html']}")
    print(f"        {paths['md']}")
    print(f"        {paths['json']}")


def _summarizer(args):
    return llm.summarize if getattr(args, "summary", False) else None


def _config_path(args) -> Path | None:
    if args.config:
        return Path(args.config)
    default = Path("playgoblin.toml")
    return default if default.exists() else None


def cmd_report(args) -> int:
    findings, paths = analyze([Path(d) for d in args.run_dirs], Path(args.out), load_config(_config_path(args)), _summarizer(args))
    _emit(findings, paths, args)
    return 0


def cmd_file_issues(args) -> int:
    lines: list[str] = []
    echo = lines.append if args.json else print
    code = issues.file_issues(Path(args.report_dir), args.repo, args.yes, args.label, args.include_unconfirmed, echo=echo)
    if args.json:
        print(json.dumps({"posted": args.yes, "log": lines}, indent=2))
    return code


def cmd_run(args) -> int:
    client = Client(args.host, args.port)
    say = _say(args)

    def tick(s):
        if not args.json and sys.stdout.isatty():
            print("\r" + f"  {s.get('state')} t={s.get('t', 0):.0f}s events={s.get('events', 0)}   ", end="", flush=True)

    try:
        findings, paths, _ = run_walks(client, args.minutes, args.seed, args.runs, Path(args.out), load_config(_config_path(args)),
                                       _summarizer(args), say=say, on_tick=tick)
    except PlayGoblinError as e:
        print(f"error: {e}", file=sys.stderr)
        return 2
    except KeyboardInterrupt:
        print("\nstopping the run...", file=sys.stderr)
        try:
            client.stop()
        except PlayGoblinError:
            pass
        return 130
    _emit(findings, paths, args)
    return 0


def cmd_mcp(args) -> int:
    from .mcp_server import serve
    return serve()


def _safe_console() -> None:
    """Windows consoles default to a legacy codepage that cannot print the severity icons."""
    for stream in (sys.stdout, sys.stderr):
        try:
            stream.reconfigure(encoding="utf-8", errors="replace")
        except (AttributeError, ValueError):
            pass


def main(argv: list[str] | None = None) -> int:
    _safe_console()
    p = argparse.ArgumentParser(prog="playgoblin", description="A QA bot that plays your game and reports bugs. Works with Unity, Unreal "
                                                              "(experimental) and any engine that writes the run-folder format.")
    p.add_argument("--version", action="version", version=f"playgoblin {__version__}")
    sub = p.add_subparsers(dest="command", required=True)

    def common(sp):
        sp.add_argument("--json", action="store_true", help="print one JSON document on stdout (progress goes to stderr); for scripts and AI agents")

    r = sub.add_parser("run", help="start walks in a running game and write a report")
    r.add_argument("--minutes", type=float, default=5.0)
    r.add_argument("--seed", type=int, default=1, help="first seed; later runs use seed+1, seed+2, ...")
    r.add_argument("--runs", type=int, default=1, help="number of walks; findings seen in several are merged")
    r.add_argument("--out", default="playgoblin-report")
    r.add_argument("--host", default="127.0.0.1")
    r.add_argument("--port", type=int, default=None)
    r.add_argument("--config", default=None, help="path to playgoblin.toml")
    r.add_argument("--summary", action="store_true", help="add an LLM-written summary (setup: playgoblin/llm.py)")
    common(r)
    r.set_defaults(func=cmd_run)

    rep = sub.add_parser("report", help="build a report from run folders (works offline)")
    rep.add_argument("run_dirs", nargs="+", help="PlayGoblinRuns/<run id> folders")
    rep.add_argument("--out", default="playgoblin-report")
    rep.add_argument("--config", default=None)
    rep.add_argument("--summary", action="store_true", help="add an LLM-written summary (setup: playgoblin/llm.py)")
    common(rep)
    rep.set_defaults(func=cmd_report)

    fi = sub.add_parser("file-issues", help="file a report's findings as GitHub issues (dry run unless --yes)")
    fi.add_argument("report_dir", nargs="?", default="playgoblin-report")
    fi.add_argument("--repo", required=True, help="owner/name")
    fi.add_argument("--yes", action="store_true", help="actually create the issues")
    fi.add_argument("--label", default=None, help="label to add (must already exist in the repo)")
    fi.add_argument("--include-unconfirmed", action="store_true")
    common(fi)
    fi.set_defaults(func=cmd_file_issues)

    m = sub.add_parser("mcp", help="run the MCP server on stdio so AI agents can drive PlayGoblin")
    m.set_defaults(func=cmd_mcp)

    argv = list(sys.argv[1:] if argv is None else argv)
    if argv[:2] == ["report", "file-issues"]:  # the spelling used in the README
        argv = argv[1:]
    args = p.parse_args(argv)
    if getattr(args, "runs", 1) < 1 or getattr(args, "minutes", 1) <= 0:
        p.error("--runs must be >= 1 and --minutes > 0")
    try:
        return args.func(args)
    except (FileNotFoundError, ValueError, RuntimeError, OSError) as e:
        print(f"error: {e}", file=sys.stderr)
        return 2
