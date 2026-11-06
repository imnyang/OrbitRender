# OrbitRender

OrbitRender is a Unity Mod Manager mod that renders custom levels in A Dance of Fire and Ice (ADOFAI) into video files.

You can configure the resolution, FPS, bitrate, encoder, and more. Instead of recording gameplay in real time, OrbitRender renders each frame individually.

## Features

- **Video Rendering** — Up to 1024 Target FPS, 240 Video FPS, and custom resolutions
- **Multiple Encoders** — H.264, HEVC, VP9, AV1, ProRes, FFV1 (lossless), with support for NVIDIA NVENC, Intel QSV, AMD AMF, VAAPI, Vulkan, and Apple VideoToolbox

ProRes supports the `prores_ks` software encoder and available `prores_videotoolbox` Apple hardware encoding. Auto on macOS prefers VideoToolbox after a successful encoding probe. The export dialog's encoding tab offers Proxy, LT, Standard, HQ, 4444, and 4444 XQ; HQ is the default. Select Auto (MOV), MOV, or MKV as the container. The profile replaces bitrate, encoding speed, and bit depth settings and is saved in defaults and user presets. The 422 profiles output 10-bit 4:2:2; 4444 profiles preserve captured alpha. Capture remains 8-bit RGB/RGBA, so the output format does not increase source precision.

FFV1 is a lossless codec that preserves every pixel exactly. It is written to MKV with software encoding; bitrate and encoding speed do not apply. When the FFmpeg build and GPU provide the Vulkan FFV1 encoder (`ffv1_vulkan`), it is offered in the encoder dropdown after an availability probe, and [Auto] prefers it — this path uploads the native RGB surface and preserves chroma resolution exactly. Aligned lossless audio (FLAC) can be selected as well. Files are very large, making it best suited as a master for editing or re-encoding.
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

FFmpeg is required for rendering. If FFmpeg is not installed, OrbitRender will ask whether you want to install it automatically on the first launch and install it into `A Dance of Fire and Ice/ffmpeg/<platform>/`. Files in an existing `Mods/OrbitRender/FFmpeg/` or `Mods/TUFReplay/FFmpeg/` folder are verified and copied into the shared location without deleting the originals. A manually configured FFmpeg executable path takes precedence.

FFmpeg is not included in the mod distribution.

## Usage

Open a custom level in the ADOFAI editor, then select `File → Export Video` or press `F6`.

For six seconds after audio capture ends, OrbitRender checks whether the live audio clock has resumed. If it stalls, OrbitRender doubles the game's audio buffer up to 2048 samples and shows the game's buffer-change notification. This setting is saved by the game, so a buffer raised for a Bluetooth device stays raised when switching back to wired audio. Pauses and frames longer than 0.2 seconds are excluded from detection.

The default output folder is `~/Videos/A Dance of Fire and Ice/OrbitRender`. Enter an **Output folder** or choose one with Browse in the F6 export dialog or OrbitRender's basic settings in Unity Mod Manager. Both use the same setting; clear the field to use the default.

Export settings show estimated video size, temporary storage requirements and available disk space. Storage is checked again once playback timing is known. ProRes estimates are conservative, not guaranteed sizes.

Advanced export settings offer FFmpeg CPU frame pool budgets of 64, 256 (default), or 512 MiB. GPU/native encoder allocations are additional; at least one frame is allocated even if it exceeds the budget.

If audio muxing fails after video encoding finishes, the video, WAV and `.recovery.json` are preserved. Use **Retry audio mux** in the error dialog or select the recovery file in advanced export settings after restarting. Keep the bundle in the same folder. Successful recovery cleans up intermediate files and never overwrites an existing final video.



Export inputs show inline errors and focus the first invalid field when submitted. FPS, bit depth, encoder, and audio settings include explanations and compatibility guidance.

Use **Add to queue** or the **Queue** tab to register full-map or selection renders with different settings for the currently open map. Settings and output folders are captured when added. Reorder, remove, run sequentially, or pause after the current render. Failure or cancellation preserves waiting jobs and pauses the queue; changing maps also pauses it. The list lasts for the current game session.

Render error dialogs offer **Copy diagnostic information** to copy the error, output settings, encoder, system/GPU details and file paths to the clipboard. Reports are not sent automatically.

To control rendering from another mod, see the [C# API documentation](docs/CSharp-API.md) (Korean). Replay renders are requested by TUFReplay, and overlay and key-viewer mods integrate through TUFReplay's `ReplayApi`, not OrbitRender ([Overlay API notice](docs/Overlay-API.md), Korean).

## License

OrbitRender is licensed under the GNU General Public License v3.0 (`GPL-3.0-only`) with an additional linking exception for A Dance of Fire and Ice and its associated runtime components.

See [LICENSE](./LICENSE) and [LICENSE-EXCEPTION](./LICENSE-EXCEPTION).
