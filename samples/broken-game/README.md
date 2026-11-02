# broken-game

A tiny Unity level used to check that PlayGoblin finds what it should and stays quiet when there is nothing to find.
It has two scenes built from the same layout by `Assets/Editor/BrokenGameBuilder.cs`:

- **Broken**: seven planted bugs.
- **Clean**: the same level with every bug switched off. PlayGoblin must report nothing here.

| Planted bug | Where | What PlayGoblin reports |
|---|---|---|
| Floor tile gives way under the player | `Trapdoor.cs`, tile near the start | Player fell out of the world |
| `NullReferenceException` every frame after the first fall | `DeathHud.cs` (unassigned label) | Exception with the stack hash and count |
| 0.5 m step across the level. The walk grid allows 0.6 m, the controller climbs 0.3 m | `Ledge` object | Stuck spot, confirmed when it repeats |
| Menu button with no listener | `Options` button | Dead button |
| Placeholder text on the main menu | `TODO: game title` | Placeholder text on screen |
| Missing-material (magenta) decal on the floor | `Missing Material Decal` | Missing-material pink on screen |
| Black screen for 8 s | `CameraBlackout.cs` | Screen rendered blank |
| About 15 fps for 40 s | `FrameStutter.cs` | Frame rate dropped (needs `fpsBudgetMs`) |

Not planted: editor gizmos left on, because PlayGoblin cannot detect them yet.

## Run it

```bash
# build the scenes (only needed after editing the builder)
Unity -batchmode -nographics -quit -projectPath samples/broken-game -executeMethod BrokenGame.Editor.BrokenGameBuilder.BuildAll

# walk both scenes for several seeds and compare the reports with the table above
python scripts/verify_samples.py --unity "<path to Unity.exe>" --seeds 1 2 3 4 5
```

The script opens a Unity window: frames cannot be captured under `-nographics`. Each walk is 120 game seconds at 4x speed.

## What this does and does not show

All planted bugs were found in 5 of 5 seeds, and the Clean scene had no findings in 5 of 5 runs. The planted bugs were chosen by the people who wrote the checks, so this is a regression test, not a measure of accuracy on real games. Real-game numbers still need to be collected.
