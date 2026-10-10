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

## How it works

1. `tools/convert_scan.py` turns a textured OBJ (RealityScan export) into an
   `.lmap` file: Unity axes, centred on the scan, split into 25 m chunks, each
   decimated (12 % of the triangles for rendering, 2 % for collisions) with
   chunk edges locked so neighbours still meet, plus the texture as JPEG.
2. The plugin loads that map when you fly in **The Drawing Board**, Liftoff's
   empty grid level: the scan is placed around the drone's spawn point (the
   scan's middle, on its top surface there), the grid floor moves just below
   the scan's lowest edges, and the scan's colliders use the floor's crash
   material.

```bash
pip install -r tools/requirements.txt
python3 tools/convert_scan.py data/pirita_full.obj dist/maps/pirita   # ~30 s
```

Options: `--chunk`, `--render-ratio`, `--collision-ratio`, `--texture-size`.

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

## Keys

| Key | Action |
| --- | --- |
| <kbd>F9</kbd> | Switch the scan between lit and emissive (texture as captured) shading |
| <kbd>F8</kbd> | Write a hierarchy dump of the loaded scenes (probe) |

Keys, the map file and the default shading are in
`BepInEx/config/is.0s.liftoff-import.cfg`.

## Probe

On every scene load the probe logs the scene's anti-cheat components, shaders,
collider layers, terrains, rigidbodies and cameras. <kbd>F8</kbd> writes the
full hierarchy of the loaded scenes to `BepInEx/LiftoffImport/dump-*.txt`.
