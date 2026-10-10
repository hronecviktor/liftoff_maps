#!/bin/sh
# Builds the mod and assembles dist/overlay/: BepInEx 5 for Linux plus our
# plugin, laid out to be copied straight into the Liftoff install folder.
#
#   scripts/package.sh            # needs the .NET 8 SDK and game/Managed
set -e
cd "$(dirname "$0")/.."
BEPINEX=5.4.23.5
ZIP=game/BepInEx_linux_x64_$BEPINEX.zip

if [ ! -f "$ZIP" ]; then
  mkdir -p game
  curl -fsSL -o "$ZIP" "https://github.com/BepInEx/BepInEx/releases/download/v$BEPINEX/BepInEx_linux_x64_$BEPINEX.zip"
fi
if [ ! -d game/BepInEx/core ]; then
  python3 -c "import zipfile,sys; zipfile.ZipFile(sys.argv[1]).extractall('game')" "$ZIP"
fi

dotnet build mod/LiftoffImport -c Release -nologo -v quiet

rm -rf dist/overlay
mkdir -p dist/overlay/BepInEx/plugins/LiftoffImport
python3 -c "import zipfile,sys; zipfile.ZipFile(sys.argv[1]).extractall('dist/overlay')" "$ZIP"
chmod +x dist/overlay/run_bepinex.sh
cp mod/LiftoffImport/bin/Release/net472/LiftoffImport.dll dist/overlay/BepInEx/plugins/LiftoffImport/
echo "dist/overlay ready"
