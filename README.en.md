# OrbitRender

OrbitRender is a Unity Mod Manager mod that renders custom levels in A Dance of Fire and Ice (ADOFAI) into video files.

You can configure the resolution, FPS, bitrate, encoder, and more. Instead of recording gameplay in real time, OrbitRender renders each frame individually.

## Features

- **Video Rendering** — Up to 1024 Target FPS, 240 Video FPS, and custom resolutions
- **Multiple Encoders** — H.264, HEVC, VP9, AV1, ProRes 422 HQ, FFV1 (lossless), with support for NVIDIA NVENC, Intel QSV, AMD AMF, VAAPI, and Vulkan

ProRes uses the FFmpeg `prores_ks` software encoder with the 10-bit 4:2:2 HQ profile. Select Auto (MOV), MOV, or MKV as the container. The HQ profile replaces the bitrate and encoding speed settings. Capture remains 8-bit RGB; 10-bit output does not increase source precision.

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

FFmpeg is required for rendering. If FFmpeg is not installed, OrbitRender will ask whether you want to install it automatically on the first launch. You can also specify the path to an existing FFmpeg executable in the settings.

FFmpeg is not included in the mod distribution.

## Usage

Open a custom level in the ADOFAI editor, then select `File → Export Video` or press `F6`.

Export settings show estimated video size, temporary storage requirements and available disk space. Storage is checked again once playback timing is known. ProRes estimates are conservative, not guaranteed sizes.

Advanced export settings offer FFmpeg CPU frame pool budgets of 64, 256 (default), or 512 MiB. GPU/native encoder allocations are additional; at least one frame is allocated even if it exceeds the budget.

If audio muxing fails after video encoding finishes, the video, WAV and `.recovery.json` are preserved. Use **Retry audio mux** in the error dialog or select the recovery file in advanced export settings after restarting. Keep the bundle in the same folder. Successful recovery cleans up intermediate files and never overwrites an existing final video.



Export inputs show inline errors and focus the first invalid field when submitted. FPS, bit depth, encoder, and audio settings include explanations and compatibility guidance.

Use **Add to queue** or the **Queue** tab to register full-map or selection renders with different settings for the currently open map. Settings and output folders are captured when added. Reorder, remove, run sequentially, or pause after the current render. Failure or cancellation preserves waiting jobs and pauses the queue; changing maps also pauses it. The list lasts for the current game session.

Render error dialogs offer **Copy diagnostic information** to copy the error, output settings, encoder, system/GPU details and file paths to the clipboard. Reports are not sent automatically.

## License

OrbitRender is licensed under the GNU General Public License v3.0 (`GPL-3.0-only`) with an additional linking exception for A Dance of Fire and Ice and its associated runtime components.

See [LICENSE](./LICENSE) and [LICENSE-EXCEPTION](./LICENSE-EXCEPTION).
