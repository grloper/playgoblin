"""A Model Context Protocol server (stdio, no dependencies) so AI agents can run PlayGoblin and read its findings.

Run it with `playgoblin mcp`. Example client config:

    { "mcpServers": { "playgoblin": { "command": "playgoblin", "args": ["mcp"] } } }

Tools: playgoblin_status, playgoblin_run, playgoblin_report, playgoblin_findings, playgoblin_file_issues.
Posting GitHub issues needs confirm=true and should only be done after the person has agreed to it.
"""
from __future__ import annotations

import json
import sys
from pathlib import Path

from . import __version__, issues
from .client import Client, PlayGoblinError
from .config import load_config
from .service import build_report, run_walks

PROTOCOL = "2024-11-05"

TOOLS = [
    {
        "name": "playgoblin_status",
        "description": "Check whether a game with the PlayGoblin package/plugin is running and reachable on the local API, and what state its current run is in.",
        "inputSchema": {"type": "object", "properties": {"port": {"type": "integer", "description": "Local API port, default 47321"}}},
    },
    {
        "name": "playgoblin_run",
        "description": ("Walk the open game for a number of minutes (blocks until done), then build a report and return the findings. "
                        "The game must be in Play Mode / running with the plugin. Keep minutes small (1-5) for a first check; use several runs "
                        "(different seeds) to confirm stuck spots."),
        "inputSchema": {"type": "object", "properties": {
            "minutes": {"type": "number", "default": 2, "description": "Game minutes per run"},
            "runs": {"type": "integer", "default": 1}, "seed": {"type": "integer", "default": 1},
            "out": {"type": "string", "default": "playgoblin-report"}, "port": {"type": "integer"},
            "config": {"type": "string", "description": "Path to playgoblin.toml"}}},
    },
    {
        "name": "playgoblin_report",
        "description": "Build a report from existing run folders (PlayGoblinRuns/<id>). Works offline: no game, network or API key.",
        "inputSchema": {"type": "object", "required": ["run_dirs"], "properties": {
            "run_dirs": {"type": "array", "items": {"type": "string"}}, "out": {"type": "string", "default": "playgoblin-report"},
            "config": {"type": "string"}}},
    },
    {
        "name": "playgoblin_findings",
        "description": "Read the findings of an existing report folder (findings.json). Every finding has an id, type, severity, title, time, position, how often it was seen and whether it is confirmed.",
        "inputSchema": {"type": "object", "properties": {"report_dir": {"type": "string", "default": "playgoblin-report"},
                                                         "include_unconfirmed": {"type": "boolean", "default": True}}},
    },
    {
        "name": "playgoblin_file_issues",
        "description": ("File a report's confirmed findings as GitHub issues with the gh CLI. DRY RUN unless confirm=true; "
                        "ask the person before setting confirm=true. Skips findings that are already filed."),
        "inputSchema": {"type": "object", "required": ["repo"], "properties": {
            "repo": {"type": "string", "description": "owner/name"}, "report_dir": {"type": "string", "default": "playgoblin-report"},
            "confirm": {"type": "boolean", "default": False}, "label": {"type": "string"},
            "include_unconfirmed": {"type": "boolean", "default": False}}},
    },
]


def _brief(f: dict) -> dict:
    keys = ("type", "severity", "title", "t", "position", "detail", "occurrences", "runs_seen", "runs_total", "confidence", "confirmed")
    out = {k: f.get(k) for k in keys}
    out["id"] = f.get("data", {}).get("id")
    out["evidence_files"] = len(f.get("evidence", []))
    return out


def _payload(findings, paths) -> dict:
    items = [_brief(f.to_dict()) for f in findings]
    return {"findings": items, "confirmed": sum(1 for f in findings if f.confirmed),
            "unconfirmed": sum(1 for f in findings if not f.confirmed),
            "report": {k: str(v) for k, v in paths.items()}}


def call_tool(name: str, args: dict) -> dict:
    cfg_path = Path(args["config"]) if args.get("config") else (Path("playgoblin.toml") if Path("playgoblin.toml").exists() else None)
    if name == "playgoblin_status":
        return Client(port=args.get("port")).status()
    if name == "playgoblin_run":
        findings, paths, runs = run_walks(Client(port=args.get("port")), float(args.get("minutes", 2)), int(args.get("seed", 1)),
                                          int(args.get("runs", 1)), Path(args.get("out", "playgoblin-report")), load_config(cfg_path))
        return {**_payload(findings, paths), "run_dirs": [str(r) for r in runs]}
    if name == "playgoblin_report":
        findings, paths = build_report([Path(d) for d in args["run_dirs"]], Path(args.get("out", "playgoblin-report")), load_config(cfg_path))
        return _payload(findings, paths)
    if name == "playgoblin_findings":
        data = json.loads((Path(args.get("report_dir", "playgoblin-report")) / "findings.json").read_text(encoding="utf-8"))
        items = [f for f in data["findings"] if args.get("include_unconfirmed", True) or f["confirmed"]]
        return {"findings": [_brief(f) for f in items], "summary": data.get("summary"), "runs": data.get("runs")}
    if name == "playgoblin_file_issues":
        lines: list[str] = []
        issues.file_issues(Path(args.get("report_dir", "playgoblin-report")), args["repo"], bool(args.get("confirm", False)),
                           args.get("label"), bool(args.get("include_unconfirmed", False)), echo=lines.append)
        return {"posted": bool(args.get("confirm", False)), "log": lines}
    raise ValueError(f"unknown tool {name}")


def handle(msg: dict) -> dict | None:
    """One JSON-RPC message in, one response out (None for notifications)."""
    method, mid = msg.get("method"), msg.get("id")
    if mid is None:
        return None  # notifications such as notifications/initialized
    try:
        if method == "initialize":
            result = {"protocolVersion": msg.get("params", {}).get("protocolVersion", PROTOCOL),
                      "capabilities": {"tools": {}}, "serverInfo": {"name": "playgoblin", "version": __version__},
                      "instructions": "PlayGoblin walks a game and reports crashes, stuck spots, falls, dead buttons and more. "
                                      "Call playgoblin_status first, then playgoblin_run. Never post issues without the person's OK."}
        elif method == "tools/list":
            result = {"tools": TOOLS}
        elif method == "tools/call":
            p = msg.get("params", {})
            try:
                out = call_tool(p["name"], p.get("arguments") or {})
                result = {"content": [{"type": "text", "text": json.dumps(out, indent=2, default=str)}], "isError": False}
            except (PlayGoblinError, FileNotFoundError, ValueError, RuntimeError, KeyError, OSError) as e:
                result = {"content": [{"type": "text", "text": f"{type(e).__name__}: {e}"}], "isError": True}
        elif method == "ping":
            result = {}
        else:
            return {"jsonrpc": "2.0", "id": mid, "error": {"code": -32601, "message": f"method not found: {method}"}}
    except Exception as e:  # never let one bad request kill the server
        return {"jsonrpc": "2.0", "id": mid, "error": {"code": -32603, "message": str(e)}}
    return {"jsonrpc": "2.0", "id": mid, "result": result}


def serve(stdin=None, stdout=None) -> int:
    stdin, stdout = stdin or sys.stdin, stdout or sys.stdout
    for line in stdin:
        line = line.strip()
        if not line:
            continue
        try:
            msg = json.loads(line)
        except json.JSONDecodeError:
            stdout.write(json.dumps({"jsonrpc": "2.0", "id": None, "error": {"code": -32700, "message": "parse error"}}) + "\n")
            stdout.flush()
            continue
        resp = handle(msg)
        if resp is not None:
            stdout.write(json.dumps(resp) + "\n")
            stdout.flush()
    return 0
