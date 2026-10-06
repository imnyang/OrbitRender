# OrbitRender

OrbitRender is a Unity Mod Manager mod that renders ADOFAI custom levels to video at a selected resolution and frame rate.

## Features

- Use the editor's `Export Video` file-menu action or press `F6` to render the currently opened custom level.
- Configure resolution, FPS, bitrate, audio, BGA, codec, and encoder in a dialog before each export.
- Centered progress window with FPS, realtime multiplier, ETA, and finish time.
- Preview, FullHD, QHD, UHD 4K, and Custom profiles.
- Configurable resolution, Target FPS 15–1024 / Video FPS 15–240, 1–200 Mbps CBR bitrate, end delay, audio, and output directory.
- Selectable H.264/AVC, H.265/HEVC, VP9, and AV1 codecs (Auto uses WebM for VP9 and MP4 for the others; MP4/TS/MKV/MOV can be selected explicitly).
- Audio codec selection: `Auto / AAC / Opus`. Auto uses Opus at 160 kbps for WebM and AAC at 320 kbps otherwise. Opus is selectable for MP4/MKV/WebM and supports concurrent encoding. Choose AAC when MP4 playback compatibility matters. RPC requests accept `audioCodec: "Auto"`, `"AAC"`, or `"Opus"`.
- Selectable NVIDIA NVENC, Intel Quick Sync, AMD AMF, and software backends with GPU auto-detection.
- Optional game-audio capture and final audio/video mux.
- BGA Mode hides tiles, holds, tile effects, planets, planet particles, and gameplay hit sounds while preserving the background, camera, decorations, and music timing.
- Localhost RPC API with per-job `bgaMode` override.

## Installation

After downloading the mod, place it neatly in the game's `Mods` directory:

```text
A Dance of Fire and Ice/Mods/OrbitRender/
```

Install `OrbitRender.dll` and `Info.json` in the mod folder. If FFmpeg is missing, the mod asks for confirmation on first launch and downloads the current platform's binary into `FFmpeg/<platform>` only after approval; existing installations or an explicit `FFmpeg executable` setting are respected without prompting. Enable the mod in Unity Mod Manager, open a custom level, configure the settings, and press `F6`.

Runtime support is intended for Windows, macOS, and Linux when the platform has a compatible ADOFAI and Unity Mod Manager environment. Windows uses `ffmpeg.exe`; macOS/Linux use an executable `ffmpeg` available on PATH or selected in the `FFmpeg executable` setting.

The build output does not contain FFmpeg. After you approve the install, the mod automatically selects and downloads one binary: `windows-x64`, `linux-x64`, `macos-x64`, or `macos-arm64` for Apple Silicon.

## Automatic updates

On startup, the mod checks GitHub's latest stable release. Drafts and pre-releases are excluded through `releases/latest`; the downloaded ZIP's version and SHA-256 are verified. When no render is active, the mod hot-reloads through Unity Mod Manager without restarting the game; if hot reload is unavailable, it falls back to applying the update after the game exits.

## Settings

UMM settings and the in-game export dialog use **Basic / Game Settings / Advanced** tabs. Preset, encoding speed, and bit depth use radio options. The filename preview, filename template, and output format remain below the tabs; variable insertion and syntax help appear when opening **Variables**. UMM-specific paths, FFmpeg installation, diagnostics, and reset controls are under Files & Troubleshooting in Advanced.

| Setting | Default | Description |
| --- | --- | --- |
| Preset | FullHD | Preview / FullHD / QHD / UHD 4K / Custom |
| Width / Height | 1920 × 1080 | Custom resolution, normalized to even values |
| Target FPS | 60 | In-game/game-simulation update FPS, 15–1024 |
| Video FPS | 60 | Final output video FPS, 15–240; independent of Target FPS |
| Video bitrate | 18 Mbps | 1–200 Mbps CBR |
| End delay | 2 seconds | Delay after the later of music or final tile |
| Capture audio | On | Capture game audio |
| Audio volume adjustment | 0 dB | Adjust captured audio during final muxing, -60 to +12 dB |
| Show preview while rendering | On | Show output frames on the game screen during rendering |
| BGA mode | Off | Render without tiles, planets, or hit sounds |
| Show planet rings | On | Include planet orbit rings |
| Show song title | On | Include the level's default title text |
| Show countdown | On | Include Get Ready, countdown numbers, and Go |
| Show result text | On | Include only the completion/Pure Perfect message; judgment details stay hidden |
| Show hit judgments | Off | Include hit judgment text when tiles are hit |
| Encoding speed | Quality | Maximum / Balanced / Quality |
| Video encoder | Auto (H.264) | Choose encoder and codec together (NVENC H.264, x265 HEVC, AOM AV1, etc.) |
| Video bit depth | 8-bit | 8-bit / 10-bit (`yuv420p10le`) |
| Output format | Auto | Auto / .mp4 / .ts / .mkv / .mov |
| Filename format | `Render_{level}_{date}_{time}_{id}` | Level name, date, time, random ID; extension added automatically |
| Output folder | `Renders` | Relative to the game folder or absolute |
| FFmpeg executable | Automatic | User-approved install, PATH lookup, or an explicit path |

The software AV1 encoder does not support strict CBR. Audio renders use target-bitrate VBR, while video-only renders use capped CRF.

VAAPI supports H.264, HEVC, VP9, and AV1 when the FFmpeg build and GPU driver support them. The renderer initializes the default VAAPI device and uploads NV12 (8-bit) or P010 (10-bit) frames. Only encoders passing the hardware smoke test appear in the dropdown; failures use the existing software fallback prompt. Driver quality/rate-control selection remains automatic. See [FFmpeg VAAPI documentation](https://ffmpeg.org/ffmpeg-codecs.html#VAAPI-encoders).

Duplicate filenames receive `_1`, `_2`, etc.; invalid filename characters are sanitized. The encoder dropdown lists only encoders that pass an actual FFmpeg encode with the selected bit depth and container. Use **Refresh encoders** after changing FFmpeg or drivers. TS supports H.264/H.265; other codec/container combinations are checked with FFmpeg.

Before rendering, the selected encoder is verified with a real one-frame smoke test. If a hardware encoder fails, the renderer asks for consent before using Software for that render; declining leaves the saved setting unchanged and cancels the render.

### Custom presets

Use the **Basic → Preset** dropdown in the export dialog or UMM settings to select built-in and saved custom presets. **Preset Save** opens a modal for the new preset's name. Select **Save** to add the current settings to the dropdown, or **Cancel** / Esc to close without saving. Presets store resolution, FPS, bitrate, audio, game visibility, encoding and filename options. Names must contain 1–64 characters and are unique regardless of case.

Presets persist in mod settings across game restarts. Output folders, FFmpeg paths and selected tile ranges remain specific to the current environment. Editing loaded settings keeps the saved preset intact; save changes under a new name. Resetting render settings preserves custom presets.

### Filename templates

Existing `Render_{level}_{date}_{time}_{id}` templates still work. Use **Insert variable** in the export dialog to insert variables and examples. The filename preview updates as you edit; invalid syntax must be corrected before exporting.

`{level}` uses the song title from the level settings, falling back to the level filename when blank. A literal `/` or `\` creates subfolders beneath the output directory. For example, `{level}/Render_{date:yyyy-MM-dd_HH-mm-ss}_{id}.mp4` produces `wowcoollevel/Render_2026-10-05_15-05-42_8e5bb0.mp4`. Slashes in expression results become underscores; `.` and `..` path components are ignored. Each component is limited to 160 characters. Explicit `.mp4`, `.ts`, `.mkv`, `.mov`, or `.webm` suffixes are replaced with the selected container extension so they are not duplicated.

Available variables: `{level}`, `{artist}` (empty when missing), `{date}`, `{time}`, `{id}`, `{width}`, `{height}`, `{bitrate}` (Mbps), `{videoFps}`, `{ingameFps}`, `{codec}` (H264/H265/VP9/AV1), `{bitDepth}` (8/10), and `{bgaMode}` (true/false). Names are case sensitive.

```text
{level}_{date:yyyyMMdd}_{width}x{height}_{videoFps}fps
{artist|default:"Unknown"} - {level|replace:"/","_"|truncate:40}
{level}_{if:bgaMode,"BGA","Gameplay"}
```

Date/time variables accept .NET date formats, such as `{date:yyyy-MM-dd}` or `{time:HHmmss}`. Chain `|lower`, `|upper`, `|trim`, `|replace:"old","new"`, `|truncate:40` (0–160), and `|default:"fallback"` to transform values. String arguments must be double quoted; escape double quotes and backslashes as `\"` and `\\`.

`{if:bgaMode,"yes","no"}` selects a string based on a boolean; `!bgaMode` negates the condition. String conditions such as `{if:artist,"present","missing"}` check for nonempty text. Use `{{` and `}}` for literal braces. The extension is added automatically. Filename sanitization, the 160-character limit, and collision numbering apply after transformations. Preview timestamps and IDs are examples; actual values are generated during export. Templates are limited to 4096 characters.

### Diagnostics

Use `Run diagnostics` in the settings screen before rendering to check:

- whether FFmpeg starts and which version is installed;
- whether the selected video encoder is available;
- whether the selected video/audio encoders and container are available;
- whether the selected encoder passes an actual one-frame smoke test, plus GPU driver details;
- whether the output folder can be created and written to; and
- Unity audio output and GPU readback status.

Use `Copy report` to copy the result when reporting a problem. Diagnostics create a temporary file in the output folder and delete it immediately.

### BGA Mode

BGA Mode saves the original renderer state, hides gameplay-only visuals immediately before camera rendering, skips hit-time sound scheduling, and restores the original state after completion, cancellation, or failure. Music, background, camera motion, and decorations remain active. Disable `Capture audio` as well if the music should also be excluded.

## RPC

Start ADOFAI with:

```text
--renderer-rpc
```

The default base URL is `http://127.0.0.1:1108/`. See the complete [RPC API specification](docs/RPC_API.md) for endpoints, schemas, status codes, and JavaScript examples.

```js
const job = await fetch('http://127.0.0.1:1108/render', {
  method: 'POST',
  headers: { 'content-type': 'application/json' },
  body: JSON.stringify({
    levelPath: 'C:/Levels/MyLevel.adofai',
    preset: 'FullHD',
    bitrateMbps: 30,
    captureAudio: true,
    bgaMode: true
  })
}).then(response => response.json());

console.log(job);
```

## Performance

GPU readback and FFmpeg encoding are pipelined without spooling raw frames to temporary disk files. Completion logs separate game-frame time, readback wait/copy time, encoder backpressure, audio capture, and final mux time. Bitrate and quality are not silently reduced.

When the frame pool is full, capture completes only the oldest readback and waits for an encoder buffer, keeping later requests in flight. All requests are drained at completion.

Hardware encoders use two FFmpeg input-filter threads to leave CPU capacity for the game. The render progress window caches display text and draws it only on Repaint while its cancel button continues processing input events. Rendering skips unused GUILayout work and restores it on completion, cancellation, or failure. Bitrate, encoder presets, output resolution, and FPS settings are preserved.

Normal gameplay FPS and render completion speed are different measurements because every output frame still needs GPU readback, CPU copying, encoding input, and optional audio muxing. Target FPS drives the game simulation; Video FPS drives the final video stream.

## Build and test

```powershell
.\build.ps1 -Test
```

On macOS/Linux, use the portable build entry point after installing the local ADOFAI managed assemblies and MSBuild:

```bash
GAME_DIR="$HOME/.steam/steam/steamapps/common/A Dance of Fire and Ice" bash ./build.sh
```

To run the FFmpeg integration tests on macOS/Linux, install Mono and FFmpeg and add `TEST=1`. If `TMPDIR` points to a stale `/var/folders/...` path, the test runner automatically falls back to `/tmp`.

```bash
GAME_DIR="$HOME/.steam/steam/steamapps/common/A Dance of Fire and Ice" TEST=1 FFMPEG_PATH=ffmpeg bash ./build.sh
```

Use `-GameDir`/`GAME_DIR` for a non-default installation and `-MSBuildPath`/`MSBUILD_PATH` for a specific MSBuild executable.

## License

OrbitRender is licensed under the GNU General Public License v3.0
(`GPL-3.0-only`) with an additional linking exception for
A Dance of Fire and Ice and its associated runtime components.

See [LICENSE](./LICENSE) and [LICENSE-EXCEPTION](./LICENSE-EXCEPTION).
