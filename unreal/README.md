# PlayGoblin for Unreal Engine (experimental)

> **Status: compiles (0 errors, 0 warnings; UE 5.8.3, Win64 Development Editor) and writes a run folder, report and frames on a real game (first run 2026-11-02). Walking is NOT yet verified, see Known issues below. Earlier note: written, not yet compiled or run.** The code follows the Unity package's design and was written against UE 5.8 headers from memory.
> The first build attempt did not get as far as the compiler (a running editor blocked it), so expect compile errors to fix on first use.
> Nothing here has been verified against a real level. If you try it, please open an issue with what happened.

A C++ runtime plugin that walks a game and writes the same run folder as the Unity package ([docs/PROTOCOL.md](../docs/PROTOCOL.md)),
so `playgoblin report`, `playgoblin file-issues` and the MCP server work unchanged.

## What it does

- Finds the possessed pawn of player 0 (best with an `ACharacter`), picks random walkable destinations and moves with `AddMovementInput`.
- Uses the NavMesh when the level has one (`UNavigationSystemV1`), otherwise a floor grid sampled with line traces.
- Detects stuck spots (no horizontal progress for 5 s), falls below the level, and respawns the player at the last safe spot.
- Captures `Error` logs (and `LogScript` warnings, as exceptions), grouped by message.
- Optional frame capture through `FScreenshotRequest` (not with `-nullrhi`), and frame-rate drops with a budget.
- Local HTTP API (same routes as Unity) on 127.0.0.1:47321 for `playgoblin run`.

Not implemented yet: menu crawling (UMG buttons), placeholder-text and blank/pink frame checks on the engine side (the CLI does the frame checks), a hook for custom start flows.
Positions are written as metres with Y up (`[x, z, y]` of Unreal's cm values) so the CLI reads them like Unity runs.

## Use it

1. Copy `PlayGoblin/` into your project's `Plugins/` folder (or add `unreal/` to `AdditionalPluginDirectories` in the `.uproject`) and enable the plugin.
2. Nothing runs unless you launch with `-playgoblin`, so shipping with the plugin enabled is harmless.

```bash
# the plugin starts a run by itself and quits when it is done
UnrealEditor-Cmd.exe MyGame.uproject -game -playgoblin -PlayGoblinSeconds=300 -PlayGoblinSeed=1 -PlayGoblinOut=D:/runs -PlayGoblinTimeScale=3 -PlayGoblinExit
playgoblin report D:/runs/<run id>

# or leave the game open and drive it from the CLI / an AI agent
UnrealEditor.exe MyGame.uproject -game -playgoblin
playgoblin run --minutes 10
```

| Flag | Meaning |
|---|---|
| `-playgoblin` | Enable the plugin and open the local API (`-PlayGoblinPort=` to change the port) |
| `-PlayGoblinSeconds=N` | Start a run by itself, N game seconds long |
| `-PlayGoblinSeed=`, `-PlayGoblinOut=`, `-PlayGoblinTimeScale=`, `-PlayGoblinFpsBudgetMs=`, `-PlayGoblinExit` | Seed, output folder, game speed, frame budget, quit afterwards |

**Security note:** the API is control-only (start/stop a run, read events) but Unreal's HTTP server may listen on all interfaces. Only use `-playgoblin` on a trusted network.

## Build notes (Windows)

- Close Unreal Editor first. UnrealBuildTool refuses to build while an editor with Live Coding is open.
- Keep the project in a **short path** (for example `C:\pg`). Unreal fails on paths over 260 characters.
- `samples/unreal-sandbox` is a minimal C++ project with a generated level (a trapdoor, a 70 cm step the character cannot climb, an error after the first fall) and a `-PGClean` mode with none of those:

```bash
Build.bat PlayGoblinSandboxEditor Win64 Development -Project=C:/pg/samples/unreal-sandbox/PlayGoblinSandbox.uproject -WaitMutex
python scripts/verify_unreal.py --editor "C:/Program Files/Epic Games/UE_5.8/Engine/Binaries/Win64/UnrealEditor-Cmd.exe"
```

## Known issues (first real run, 2026-11-02)
- The floor grid used to trace from the top of the level on `ECC_Visibility` and rejected every cell in a game with roof/sky meshes. It now traces on `ECC_Pawn` from head height and skips actors the trace starts inside.
- In the first game tested the pawn stayed in `MOVE_Falling` with zero velocity and no floor at spawn, so the bot never moved. `waypoint_timeout` events now carry movement diagnostics. Whether this is a game bug (player embedded in collision) or a plugin issue is not yet resolved.
- Launching with `UnrealEditor-Cmd.exe` while the project enables Epic's experimental toolset plugins logs ~47 Python `Error` lines at t=0; they are reported as findings. These are false positives and need a filter (planned).
