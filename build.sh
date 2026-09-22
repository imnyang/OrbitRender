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
"${MSBUILD_COMMAND[@]}" OrbitRender.sln \
  /t:Rebuild \
  /p:Configuration=Release \
  "/p:GameDir=$GAME_DIR" \
  /v:minimal

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
echo "Install the output folder under the game's Mods directory."

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
