# OrbitRender

OrbitRender is a Unity Mod Manager mod that renders custom levels in A Dance of Fire and Ice (ADOFAI) into video files.

You can configure the resolution, FPS, bitrate, encoder, and more. Instead of recording gameplay in real time, OrbitRender renders each frame individually.

## Features

- **Video Rendering** — Up to 1024 Target FPS, 240 Video FPS, and custom resolutions
- **Multiple Encoders** — H.264, HEVC, VP9, AV1, with support for NVIDIA NVENC, Intel QSV, AMD AMF, and VAAPI
- **Audio Capture** — Capture in-game audio and encode it using AAC or Opus
- **BGA Mode** — Render backgrounds without tiles, planets, or gameplay effects
- **Presets** — Built-in presets and the ability to save custom presets
- **Real-Time Progress** — Rendering speed, progress, estimated completion time, and live preview
- **RPC API** — Automate rendering through a local HTTP API
- **Automatic Updates** — Check for and install updates from official GitHub releases

Supports Windows, macOS, and Linux.

## Installation

1. Download the latest version from [Releases](../../releases).
2. Extract the archive and place the mod folder in `A Dance of Fire and Ice/Mods/`.
3. Launch the game with Unity Mod Manager installed.

FFmpeg is required for rendering. If FFmpeg is not installed, OrbitRender will ask whether you want to install it automatically on the first launch. You can also specify the path to an existing FFmpeg executable in the settings.

FFmpeg is not included in the mod distribution.

## Usage

Open a custom level in the ADOFAI editor, then select `File → Export Video` or press `F6`.

## License

OrbitRender is licensed under the GNU General Public License v3.0 (`GPL-3.0-only`) with an additional linking exception for A Dance of Fire and Ice and its associated runtime components.

See [LICENSE](./LICENSE) and [LICENSE-EXCEPTION](./LICENSE-EXCEPTION).
