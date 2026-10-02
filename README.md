# Nivalis Tool Belt

A multi-tool sandbox mod for Nivalis Nights (BepInEx 6, IL2CPP). Press **F1** in-game to open the menu.

| Tool | What it does |
| --- | --- |
| Fly / ghost mode (**F2**) | The game's own no-clip mode: fly along the view direction, **E** up, **Q** down, pass through walls. |
| Movement speed | Multiplier for walk, sprint and fly speed (x0.5 to x5). |
| Freeze clock | Stops the in-game clock. |
| Skip 1 hour | Advances the in-game clock by one hour. |

Leaving fly mode with no ground underneath (over water, inside a building) would drop you out of the
world, so the mod puts you back on the last spot you stood on.

Nothing is written to save files by the mod itself, but the game saves whatever state you are in:
a skipped hour or an odd position ends up in the next save like any other progress.

## Install

1. Install [BepInEx 6 (IL2CPP, x64)](https://builds.bepinex.dev/projects/bepinex_be) into the game folder and start the game once.
2. Copy `NivalisToolBelt.dll` to `<game>\BepInEx\plugins\NivalisToolBelt\`.

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
- `src/Sandbox.cs`: the tools. New tools go here, plus a row in the menu.
- `src/SelfTest.cs`: scripted in-game test, only compiled with `-p:SelfTest=true`.

## Testing in the real game

`-p:SelfTest=true` builds a variant that, while `BepInEx\config\toolbelt-selftest.txt` exists
(content: a save name without extension), loads that save from the title screen and writes one status
line per second to `BepInEx\toolbelt-selftest.log` (player state, position, speeds, clock, cursor).
`tools\drive.ps1` has helpers to press real keys, click and take screenshots, and refuses to send
input unless the game window is in the foreground. Back up the save folder first, delete the flag
file afterwards, and deploy a normal build again before playing.
