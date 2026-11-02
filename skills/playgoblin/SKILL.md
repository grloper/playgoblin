---
name: playgoblin
description: Use when the user wants to QA-test, playtest or smoke-test a game (Unity, Unreal, or any engine that writes PlayGoblin run folders), find crashes, stuck spots, falls through the world, dead buttons, missing-material pink, placeholder text or FPS drops, turn an overnight walk into a bug report, or file the findings as GitHub issues.
---

# PlayGoblin: a bot that plays the game and reports what is broken

## Tools

If the `playgoblin` MCP server is connected, use its tools. Otherwise use the CLI with `--json`.

| Goal | MCP tool | CLI |
|---|---|---|
| Is a game reachable? | `playgoblin_status` | `playgoblin run --minutes 0.1 --json` fails with a clear message if not |
| Walk and report | `playgoblin_run` | `playgoblin run --minutes 3 --runs 2 --json` |
| Report from existing runs (offline) | `playgoblin_report` | `playgoblin report <run folders> --json` |
| Read findings | `playgoblin_findings` | read `playgoblin-report/findings.json` |
| File GitHub issues | `playgoblin_file_issues` | `playgoblin file-issues --repo owner/name [--yes]` |

## Workflow

1. **Check the game is ready.** Unity: the PlayGoblin package is installed, a `PlayGoblin Player` component is on the player object, and the editor is in Play Mode. Unreal: the game is started with `-playgoblin` (plugin is experimental). Other engines: write run folders (docs/PROTOCOL.md) and use `playgoblin report`.
2. **Start short.** One 2-3 minute run first. Then run 3+ seeds (`runs: 3`) so stuck spots can be confirmed.
3. **Read findings by confidence.** `confirmed: true` findings are safe to act on. `confirmed: false` (usually a stuck spot seen once) may be noise: rerun with another seed before reporting it as a bug.
4. **Explain, with evidence.** Quote the finding's title, time, position and `runs_seen/runs_total`. The report folder has frames and a clip under `evidence/<id>/`.
5. **Issues need consent.** `playgoblin_file_issues` is a dry run unless `confirm: true`. Show the person what would be filed and get a clear yes first. It skips findings that are already filed.

## What it can and cannot see

Finds: exceptions and error logs (grouped), stuck spots, falls, a closed-in start area, dead uGUI buttons on menus before the player exists, TODO/placeholder text, missing-material magenta, blank screens, frame-rate drops (only with a budget set).
Does not find: gameplay balance, visual polish, audio problems, editor gizmos, logic bugs that need a goal (it walks randomly, it does not play to win). It is not proof the game is bug free.

## Gotchas

- Frames and clips need a rendering device: not `-batchmode -nographics`.
- Results from a walk of 1-2 minutes are thin. Longer or repeated runs find more.
- Ignore known noise with `playgoblin.toml`: `[ignore] messages = ["regex"]`.
- The local API listens on 127.0.0.1:47321; set `PLAYGOBLIN_PORT` or `--port` if it is taken.
