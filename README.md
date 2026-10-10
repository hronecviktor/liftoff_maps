# liftoff-import

Fly your own photogrammetry scans in [Liftoff](https://store.steampowered.com/app/410340/Liftoff_FPV_Drone_Racing/):
a [BepInEx](https://github.com/BepInEx/BepInEx) mod for the native Linux build
(Unity 2022.3, Mono) that loads a scanned map, first `data/pirita_full.obj`,
into the game.

Unofficial and single-player only: keep the mod out of multiplayer. Game files,
decompiled code and source scans stay out of git (see `.gitignore`).

The game notices the mod: its anti-cheat logs `Custom code injection detected:
BepInEx.Preloader` at startup, and it uploads unhandled exceptions as crash
reports to the developer. The plugin therefore never lets an exception escape.

## Status

1. **Loader check (now):** BepInEx plus a read-only probe that logs what the
   game's levels are made of. It changes nothing in the game.
2. Mesh conversion: decimate and chunk the scan, compact binary format.
3. Map loading: replace a level's scenery with the scan, move the spawn.

## Build

Needs the .NET 8 SDK and the game's `Liftoff_Data/Managed` copied to
`game/Managed`.

```bash
scripts/package.sh      # builds the plugin and assembles dist/overlay/
```

## Install

`dist/overlay/` mirrors the game folder. With the game closed:

```bash
L=~/.local/share/Steam/steamapps/common/Liftoff
cp -r dist/overlay/. "$L/"
```

Then in Steam: Liftoff → Properties → General → **Launch options**:

```
./run_bepinex.sh %command%
```

Start the game. BepInEx writes `BepInEx/LogOutput.log` in the game folder.

**Uninstall:** clear the launch option, then remove `run_bepinex.sh`,
`libdoorstop.so`, `.doorstop_version`, `changelog.txt` and `BepInEx/` from the
game folder.

## Probe

On every scene load the probe logs the scene's anti-cheat components, shaders,
collider layers, terrains, rigidbodies and cameras. <kbd>F8</kbd> writes the
full hierarchy of the loaded scenes to `BepInEx/LiftoffImport/dump-*.txt`.
The key can be changed in `BepInEx/config/is.0s.liftoff-import.cfg`.
