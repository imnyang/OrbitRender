#!/usr/bin/env bash
set -euo pipefail

# Cross-platform build entry point. The game DLLs must come from the local
# ADOFAI installation; no game binaries or FFmpeg binaries are redistributed.
ROOT_DIR="$(CDPATH= cd -- "$(dirname -- "$0")" && pwd)"
GAME_DIR="${GAME_DIR:-}"
MSBUILD_PATH="${MSBUILD_PATH:-}"

if [[ -z "$GAME_DIR" ]]; then
  echo "Set GAME_DIR to the ADOFAI installation directory." >&2
  echo "Example: GAME_DIR=\"$HOME/.steam/steam/steamapps/common/A Dance of Fire and Ice\" bash ./build.sh" >&2
  exit 2
fi

if [[ -d "$GAME_DIR/A Dance of Fire and Ice_Data/Managed" ]]; then
  MANAGED_DIR="$GAME_DIR/A Dance of Fire and Ice_Data/Managed"
elif [[ -d "$GAME_DIR/ADanceOfFireAndIce_Data/Managed" ]]; then
  # Linux builds use the executable name without spaces for the Unity data
  # directory, unlike the Windows build.
  MANAGED_DIR="$GAME_DIR/ADanceOfFireAndIce_Data/Managed"
else
  MANAGED_DIR="$GAME_DIR/A Dance of Fire and Ice_Data/Managed"
fi
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
elif command -v dotnet >/dev/null 2>&1; then
  MSBUILD_COMMAND=(dotnet msbuild)
else
  echo "No MSBuild toolchain was found. Install msbuild or the .NET SDK, or set MSBUILD_PATH." >&2
  exit 2
fi

# The .NET SDK does not include .NET Framework reference assemblies. Use a
# local Mono targeting pack when the repository's net48 pack is absent.
FRAMEWORK_BUILD_OPTIONS=()
if [[ ! -f "$ROOT_DIR/packages/net48/build/.NETFramework/v4.8/mscorlib.dll" ]]; then
  MONO_REFERENCE_ROOTS=(
    /usr/lib/mono
    /usr/local/lib/mono
    /Library/Frameworks/Mono.framework/Versions/Current/lib/mono
    /nix/var/nix/profiles/default/lib/mono
    /run/current-system/sw/lib/mono
    /nix/store/*-mono-*/lib/mono
  )
  for mono_reference_root in "${MONO_REFERENCE_ROOTS[@]}"; do
    if [[ -f "$mono_reference_root/4.8-api/mscorlib.dll" &&
          -d "$mono_reference_root/xbuild-frameworks/.NETFramework/v4.8" ]]; then
      FRAMEWORK_BUILD_OPTIONS=(
        "/p:FrameworkPathOverride=$mono_reference_root/4.8-api"
        "/p:TargetFrameworkRootPath=$mono_reference_root/xbuild-frameworks"
      )
      echo "Using .NET Framework 4.8 references: $mono_reference_root/4.8-api"
      break
    fi
  done
fi

cd "$ROOT_DIR"
"${MSBUILD_COMMAND[@]}" OrbitRender.sln \
  /restore \
  /t:Rebuild \
  /p:Configuration=Release \
  "/p:GameDir=$GAME_DIR" \
  "/p:ManagedDir=$MANAGED_DIR" \
  "${FRAMEWORK_BUILD_OPTIONS[@]}" \
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
