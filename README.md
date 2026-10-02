# Nivalis Tool Belt

A multi-tool sandbox mod for Nivalis Nights (BepInEx 6, IL2CPP). Press **F1** in-game to open the menu.

The menu has three tabs.

**Player**

| Tool | What it does |
| --- | --- |
| Fly / ghost mode (**F2**) | The game's own no-clip mode: fly along the view direction, **E** up, **Q** down, pass through walls. |
| Movement speed | Multiplier for walk, sprint and fly speed (x0.5 to x5). |

Leaving fly mode with no ground underneath (over water, inside a building) would drop you out of the
world, so the mod puts you back on the last spot you stood on.

**Time**

| Tool | What it does |
| --- | --- |
| Freeze clock | Stops the in-game clock. |
| Clock speed | How fast the in-game clock runs, x0.25 to x10. Only the clock changes, not the game speed. Resets to x1 when the game restarts. |
| Skip 1 hour / Skip ahead to HH:00 | Advances the clock. It never goes back, and a skip stops at the curfew (02:00), where the game ends the day as usual. During the curfew you have to sleep to move on. |

**Teleport**

| Tool | What it does |
| --- | --- |
| Save current position | Adds a spot for the area you are in. |
| Spot buttons | Teleport to that spot; **X** deletes it. A spot saved in mid-air switches fly mode on when you arrive. |

The list shows every saved spot, the current area's first. A spot in another area is marked
**(travel)**: clicking it uses the game's own area travel to get there (the usual loading transition,
no cost in money or time) and then moves you to the spot. This also works during the curfew, when the
game itself does not let you leave an area; it stays blocked while you are not in control (cutscenes,
dialogues, the end-of-day screens).

Spots are stored in `<game>\BepInEx\config\renokk.nivalis.toolbelt.spots.txt` (one tab-separated line
per spot); edit the second column there to rename a spot while the game is closed.

The mod writes nothing to save files itself, but the game saves whatever state you are in:
skipped time or an odd position ends up in the next save like any other progress.

## Install

1. Install [BepInEx 6 (IL2CPP, x64)](https://builds.bepinex.dev/projects/bepinex_be) into the game folder and start the game once.
2. Download the zip from the [latest release](https://github.com/HiveSolution/nivalis-tool-belt/releases/latest) and extract it into the game folder, so that `NivalisToolBelt.dll` ends up in `<game>\BepInEx\plugins\NivalisToolBelt\`.

Hotkeys and the slider range are in `<game>\BepInEx\config\renokk.nivalis.toolbelt.cfg` (created on first start).

## Build

Needs the .NET SDK (6 or newer) and a game install with BepInEx that has been started once, because
the build references the generated assemblies in `BepInEx\interop`. They are regenerated after every
game update; rebuild after that.

```
dotnet build -c Release -p:Deploy=true
```

`Deploy=true` copies the DLL into the game. The game folder defaults to the path in
`NivalisToolBelt.csproj`; override it with `-p:GameDir="..."` or the `NIVALIS_GAME_DIR` environment variable.

## Layout

- `src/Plugin.cs`: BepInEx entry point.
- `src/Settings.cs`: config entries (hotkeys).
- `src/ToolBeltBehaviour.cs`: hotkeys, cursor handling, IMGUI menu.
- `src/Sandbox.cs`: player tools (fly, speed, fall rescue).
- `src/Clock.cs`: clock tools.
- `src/Teleports.cs`: saved spots.
- `src/SelfTest.cs`: scripted in-game test, only compiled with `-p:SelfTest=true`.

## Testing in the real game

`-p:SelfTest=true` builds a variant that, while `BepInEx\config\toolbelt-selftest.txt` exists
(content: a save name without extension), loads that save from the title screen and writes one status
line per second to `BepInEx\toolbelt-selftest.log` (player state, position, speeds, clock, cursor). In that build F9 takes a screenshot, F11 saves to a
`toolbelt_test` slot and F12 loads it.
`tools\drive.ps1` has helpers to press real keys, click and take screenshots, and refuses to send
input unless the game window is in the foreground. Back up the save folder first, delete the flag
file afterwards, and deploy a normal build again before playing.

## License

Copyright (c) 2026 RenokK. Licensed under [CC BY-NC 4.0](LICENSE.txt). You may share and modify it for noncommercial purposes with attribution; commercial use, including reselling or bundling it into commercial products, is not permitted.
