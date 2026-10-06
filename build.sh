#!/usr/bin/env bash
set -euo pipefail

# Cross-platform build entry point. The game DLLs must come from the local
# ADOFAI installation; no game binaries or FFmpeg binaries are redistributed.
ROOT_DIR="$(CDPATH= cd -- "$(dirname -- "$0")" && pwd)"
GAME_DIR="${GAME_DIR:-}"
MSBUILD_PATH="${MSBUILD_PATH:-}"
RUN_TESTS="${TEST:-0}"
FFMPEG_PATH="${FFMPEG_PATH:-ffmpeg}"

if [[ -z "$GAME_DIR" ]]; then
  echo "Set GAME_DIR to the ADOFAI installation directory." >&2
  echo "Example: GAME_DIR=\"$HOME/.steam/steam/steamapps/common/A Dance of Fire and Ice\" bash ./build.sh" >&2
  exit 2
fi

MANAGED_DIR="$GAME_DIR/A Dance of Fire and Ice_Data/Managed"
if [[ ! -f "$MANAGED_DIR/Assembly-CSharp.dll" ]]; then
  echo "ADOFAI managed assemblies were not found: $MANAGED_DIR" >&2
  exit 2
fi

if [[ -n "$MSBUILD_PATH" ]]; then
  if ! command -v "$MSBUILD_PATH" >/dev/null 2>&1 && [[ ! -x "$MSBUILD_PATH" ]]; then
    echo "The configured MSBUILD_PATH was not found: $MSBUILD_PATH" >&2
    exit 2
  fi
  case "$(basename "$MSBUILD_PATH")" in
    dotnet|dotnet.exe) MSBUILD_COMMAND=("$MSBUILD_PATH" msbuild) ;;
    *) MSBUILD_COMMAND=("$MSBUILD_PATH") ;;
  esac
elif command -v msbuild >/dev/null 2>&1; then
  MSBUILD_COMMAND=(msbuild)
elif command -v xbuild >/dev/null 2>&1; then
  MSBUILD_COMMAND=(xbuild)
elif command -v dotnet >/dev/null 2>&1; then
  MSBUILD_COMMAND=(dotnet msbuild)
else
  echo "No MSBuild toolchain was found. Install msbuild, xbuild, or the .NET SDK, or set MSBUILD_PATH." >&2
  exit 2
fi

cd "$ROOT_DIR"
for command in curl unzip python3; do
  if ! command -v "$command" >/dev/null 2>&1; then
    echo "Building all loader packages requires $command." >&2
    exit 2
  fi
done

get_archive() {
  local url="$1" destination="$2" expected="$3"
  if [[ -f "$destination/$expected" ]]; then return; fi
  mkdir -p "$destination"
  curl --fail --location "$url" --output "$destination/package.zip"
  unzip -oq "$destination/package.zip" -d "$destination"
  rm -f "$destination/package.zip"
  if [[ ! -f "$destination/$expected" ]]; then
    echo "Dependency archive is incomplete: $url" >&2
    exit 2
  fi
}

get_archive 'https://api.nuget.org/v3-flatcontainer/unitymodmanager/0.32.4/unitymodmanager.0.32.4.nupkg' \
  packages/UnityModManager lib/net35/UnityModManager.dll
get_archive 'https://api.nuget.org/v3-flatcontainer/microsoft.netframework.referenceassemblies.net48/1.0.3/microsoft.netframework.referenceassemblies.net48.1.0.3.nupkg' \
  packages/net48 build/.NETFramework/v4.8/mscorlib.dll
if [[ ! -f packages/0Harmony.dll ]]; then
  get_archive 'https://api.nuget.org/v3-flatcontainer/lib.harmony/2.2.2/lib.harmony.2.2.2.nupkg' \
    packages/Harmony lib/net48/0Harmony.dll
  cp packages/Harmony/lib/net48/0Harmony.dll packages/0Harmony.dll
fi
# Managed Mono references are platform independent; do not redistribute loaders.
get_archive 'https://github.com/LavaGang/MelonLoader/releases/download/v0.6.6/MelonLoader.x64.zip' \
  packages/MelonLoader MelonLoader/net35/MelonLoader.dll
get_archive 'https://github.com/BepInEx/BepInEx/releases/download/v5.4.23.2/BepInEx_win_x64_5.4.23.2.zip' \
  packages/BepInEx BepInEx/core/BepInEx.dll

"${MSBUILD_COMMAND[@]}" OrbitRender.sln \
  /restore \
  /t:Rebuild \
  /p:Configuration=Release \
  "/p:GameDir=$GAME_DIR" \
  /v:minimal

for loader in MelonLoader BepInEx; do
  "${MSBUILD_COMMAND[@]}" OrbitRender/OrbitRender.csproj \
    /t:Rebuild /p:Configuration=Release "/p:GameDir=$GAME_DIR" "/p:ModLoader=$loader" /v:minimal
done

# Remove FFmpeg artifacts produced by older versions of this build script.
# The exact Release/FFmpeg directory is generated output, not user data.
RELEASE_ROOT="$ROOT_DIR/OrbitRender/bin/Release"
if [[ -d "$RELEASE_ROOT/FFmpeg" ]]; then
  rm -rf "$RELEASE_ROOT/FFmpeg"
fi
for stale_name in ffmpeg ffmpeg.exe ffprobe ffprobe.exe FFmpeg-LICENSE.txt FFmpeg-README.txt; do
  if [[ -f "$RELEASE_ROOT/$stale_name" ]]; then
    rm -f "$RELEASE_ROOT/$stale_name"
  fi
done

echo "Mod output: $ROOT_DIR/OrbitRender/bin/Release"
echo "FFmpeg will be downloaded by the mod after first-launch consent."
python3 - "$RELEASE_ROOT" "$ROOT_DIR/Builds" <<'PY'
from pathlib import Path
import sys
from zipfile import ZipFile, ZIP_DEFLATED

release, builds = map(Path, sys.argv[1:])
builds.mkdir(exist_ok=True)
for loader in ('UMM', 'MelonLoader', 'BepInEx'):
    source = release if loader == 'UMM' else release / loader
    name = 'OrbitRender.zip' if loader == 'UMM' else f'OrbitRender-{loader}.zip'
    output = builds / name
    temporary = output.with_suffix('.zip.tmp')
    with ZipFile(temporary, 'w', ZIP_DEFLATED) as archive:
        for file in ('OrbitRender.dll', 'LICENSE.md', 'Info.json', 'Localization/en.ftl', 'Localization/ko.ftl'):
            if file == 'Info.json' and loader != 'UMM':
                continue
            if loader == 'UMM':
                target = f'OrbitRender/{file}'
            elif loader == 'BepInEx':
                target = f'BepInEx/plugins/OrbitRender/{file}'
            else:
                target = 'Mods/OrbitRender.dll' if file == 'OrbitRender.dll' else f'Mods/OrbitRender/{file}'
            archive.write(source / file, target)
    temporary.replace(output)
    print(f'Package: {output}')
PY

if [[ "$RUN_TESTS" == "1" ]]; then
  if [[ "$FFMPEG_PATH" == */* ]]; then
    if [[ ! -x "$FFMPEG_PATH" ]]; then
      echo "The configured FFMPEG_PATH is not executable: $FFMPEG_PATH" >&2
      exit 2
    fi
  elif ! command -v "$FFMPEG_PATH" >/dev/null 2>&1; then
    echo "FFmpeg was not found: $FFMPEG_PATH" >&2
    echo "Install FFmpeg or set FFMPEG_PATH to its executable." >&2
    exit 2
  fi

  if ! command -v mono >/dev/null 2>&1; then
    echo "The standalone .NET Framework tests require Mono on macOS/Linux." >&2
    echo "Install Mono or set up a Windows test environment." >&2
    exit 2
  fi

  "${MSBUILD_COMMAND[@]}" Tests/RendererTests.csproj \
    /t:Rebuild \
    /v:minimal

  for mod_path in OrbitRender.dll MelonLoader/OrbitRender.dll BepInEx/OrbitRender.dll; do
    mono "$ROOT_DIR/Tests/bin/Release/RendererTests.exe" --user-presets \
      "$RELEASE_ROOT/$mod_path" "$MANAGED_DIR"
  done

  # macOS normally exposes a per-user TMPDIR under /var/folders, but it can
  # become stale or unavailable when the shell inherits an old environment.
  # Fall back to /tmp instead of passing a non-existent path to the test.
  TEST_TMP_ROOT="${TMPDIR:-/tmp}"
  if [[ ! -d "$TEST_TMP_ROOT" ]]; then
    TEST_TMP_ROOT="/tmp"
  fi
  if ! TEST_OUTPUT="$(mktemp -d "$TEST_TMP_ROOT/orbit-render-tests.XXXXXX" 2>/dev/null)"; then
    TEST_OUTPUT="$(mktemp -d /tmp/orbit-render-tests.XXXXXX)"
  fi

  mono "$ROOT_DIR/Tests/bin/Release/RendererTests.exe" "$FFMPEG_PATH" "$TEST_OUTPUT"
  echo "Test videos: $TEST_OUTPUT"
fi
