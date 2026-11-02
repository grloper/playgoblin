<h1 align="center">PlayGoblin</h1>

<p align="center"><b>A goblin plays your game overnight and files the bugs it breaks.</b><br>
Open-source QA bot for Unity, Unreal (experimental) and any engine that can write a log file.</p>

<p align="center"><i>Status: pre-release (0.1.0-dev). Verified on Unity 6000.4 and on two sample games; not yet tried on a real shipped game. The Unreal plugin is written but has not been compiled yet. See "Status" below.</i></p>

---

PlayGoblin walks your game like a very persistent player: it picks destinations, runs into walls, falls into holes, clicks every menu button,
and writes down everything that goes wrong. Then it turns that into a report with frames and clips, and optionally into GitHub issues.

- **Open and free.** Apache-2.0, runs on your machine, no account.
- **Deterministic first, AI second.** The checks are plain code and give the same answer every run. An LLM is optional and only writes the summary.
- **Built for AI agents.** An MCP server, a Claude Code skill, and `--json` output on every command. See [Use it from an AI agent](#use-it-from-an-ai-agent).
- **Any engine.** Unity and Unreal plugins ship here. Anything else can write the [run-folder format](docs/PROTOCOL.md) (one JSON line per event) and get the same checks and reports.
- **Repeatable.** Seeded runs, and a stuck spot only counts as confirmed once it repeats, so one unlucky run does not file a fake bug.

## What it finds

| Check | How | Evidence | Limits today |
|---|---|---|---|
| **Crashes and exceptions** | Captures every error/exception log, grouped by message | Message, count, where the player was | |
| **Dead buttons** | Clicks each active uGUI button twice on menu screens; reports ones that change nothing | Button path and scene | Unity uGUI only; runs before a player exists; never clicks Quit/Exit/Delete/Reset/Buy |
| **Stuck spots** | Walks to random reachable points; flags where the player stops. Unconfirmed until it repeats | Position, what blocked it, frames and clip | |
| **Falling through the world** | Player goes below the level or off the NavMesh | Position, frames and clip | |
| **Blank frames** | Tells a dark scene apart from nothing being drawn | The frames | Needs a rendering device (not headless) |
| **Debug leftovers** | Magenta missing-material areas; TODO/placeholder text in uGUI and TextMeshPro | Frames, UI object path | **Editor gizmos are not detected** |
| **FPS drops** | Frame time above a budget for a full second | Position, average frame time | Off unless a budget is set |

Every finding comes from an event, a frame or a log line. If PlayGoblin cannot show it, it does not report it.
It does **not** judge fun, balance, visuals or audio, and it walks randomly rather than playing to win, so "no findings" never means "bug free".

## Quickstart (Unity)

Unity 6000.4 is the version tested. Python 3.11+; `numpy` and `Pillow` for frame checks; `ffmpeg` for clips.

```bash
pip install "./cli[frames]"        # the playgoblin command
```

1. In Unity: Package Manager > Add package from disk > `unity/package.json`.
2. Add the **PlayGoblin Player** component to your player object (CharacterController, NavMeshAgent, Rigidbody or plain transform; or implement `IPlayGoblinMover` for a custom controller).
3. Press Play, then:

```bash
playgoblin run --minutes 10 --runs 3         # walks, then writes playgoblin-report/report.html, report.md, findings.json
playgoblin report path/to/PlayGoblinRuns/<run>   # rebuild a report from run folders, offline
playgoblin file-issues --repo you/your-game      # dry run: shows what would be filed
playgoblin file-issues --repo you/your-game --yes
```

If your game starts from a keyboard-only menu, or spawns the player from code, add a few lines of glue (start the game, add the component, call `PlayGoblinRun.StartRun`). The [broken-game sample test](samples/broken-game/Assets/Tests/PlayMode/SampleRunTests.cs) shows the shape.

## Unreal Engine (experimental)

[`unreal/PlayGoblin`](unreal) is a C++ plugin that writes the same run folders and serves the same local API: it finds the player pawn,
walks the NavMesh (or a sampled floor grid when there is none), detects stuck spots, falls and error logs, and captures frames.
**It has not been compiled or run yet** (the first build attempt was blocked by a running editor). Read [unreal/README.md](unreal/README.md) before relying on it.

## Any other engine or game

Write `PlayGoblinRuns/<id>/events.jsonl` and `run.json` from your engine (Godot, a custom engine, a web game, a bot on top of a QA harness),
then run `playgoblin report <folder>`. The format is small and documented in [docs/PROTOCOL.md](docs/PROTOCOL.md);
[`playgoblin/writer.py`](cli/playgoblin/writer.py) is a short reference writer you can port.

## Use it from an AI agent

```bash
playgoblin mcp          # MCP server on stdio
```

```json
{ "mcpServers": { "playgoblin": { "command": "playgoblin", "args": ["mcp"] } } }
```

| MCP tool | What it does |
|---|---|
| `playgoblin_status` | Is a game with PlayGoblin reachable, and what is it doing |
| `playgoblin_run` | Walk for N minutes (several seeds), then return the findings |
| `playgoblin_report` | Build a report from existing run folders, offline |
| `playgoblin_findings` | Read a report's findings |
| `playgoblin_file_issues` | File GitHub issues; **dry run unless `confirm: true`** |

- **Claude Code:** this repo is also a plugin (`.claude-plugin/plugin.json`, `.mcp.json`, `skills/playgoblin/SKILL.md`). The skill tells the agent when to use PlayGoblin, how to read confidence, and that issues need your yes first.
- **Any agent or script:** every command takes `--json` and prints one JSON document on stdout (progress goes to stderr).
- [`llms.txt`](llms.txt) and [`AGENTS.md`](AGENTS.md) are the index and the developer guide for coding agents.

## Does it work? (the evidence so far)

`samples/broken-game` is a small Unity level with seven planted bugs and a clean twin scene. `python scripts/verify_samples.py --unity <Unity.exe> --seeds 1 2 3 4 5`
walks both and compares the report with what was planted. Last full run: all planted bugs found in 5 of 5 seeds, no findings on the clean scene.
The sample was written by the people who wrote the checks, so treat it as a regression test, **not** as accuracy on real games. Real-game numbers are the next thing to collect.

## Status

| Part | State |
|---|---|
| Unity package | Built and tested on Unity 6000.4: 80 edit-mode tests, 22 play-mode tests |
| Python CLI, report, MCP server | 48 tests |
| Unreal plugin | Written, **not yet compiled or tested** |
| Other Unity versions (2022.3, 6000.0-6000.3), player builds, UI Toolkit | Not tested / not supported yet |
| Real games | Not yet run on one |

## Roadmap

- Compile and verify the Unreal plugin on UE 5.x, add menu crawling and a sample level.
- Run on real projects and publish precision numbers.
- Editor-gizmo check, UI Toolkit buttons, Unity 2022.3 and player builds, a GitHub Action.
- A hook so games with custom start flows need no glue code.
- Comparing runs between builds ("new stuck spot since last night").

## Contributing

Issues and PRs welcome. The most useful contribution early on is a game where PlayGoblin misses a bug or reports one that is not there.
See [CONTRIBUTING.md](CONTRIBUTING.md).

## License

Apache-2.0
