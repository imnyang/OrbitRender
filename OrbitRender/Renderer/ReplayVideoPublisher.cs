using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;

namespace OrbitRender.Renderer
{
    internal sealed class ReplayVideoPublisher
    {
        private readonly string ffmpegPath;
        private readonly HostedReplayRequest request;
        private readonly HostedReplayDriver driver;
        private readonly RenderProfile profile;
        private readonly double audioDelaySeconds;

        internal ReplayVideoPublisher(string ffmpegPath, HostedReplayRequest request,
            HostedReplayDriver driver, RenderProfile profile, double audioDelaySeconds)
        {
            this.ffmpegPath = ffmpegPath;
            this.request = request;
            this.driver = driver;
            this.profile = profile;
            this.audioDelaySeconds = audioDelaySeconds;
        }

        // The finished video becomes the output as it is, or composed with the replay's webcam
        // and microphone and the host's overlay videos. Runs off the game thread (BackgroundFinish):
        // no Unity calls here, and the host's GetOverlayVideos is called from this thread too.
        internal void Publish(string input, string output, long capturedFrames, bool hasGameAudio,
            BackgroundFinish finish = null)
        {
            var cancellation = finish?.Token ?? CancellationToken.None;
            finish?.Report(FinishStage.Overlays);
            var overlays = driver != null ? driver.OverlayVideos()
                : new System.Collections.Generic.List<TUFReplayRenderer.Media.MediaOverlay>();
            // The host may take minutes to hand its videos over; a render cancelled meanwhile stops here.
            cancellation.ThrowIfCancellationRequested();
            if (overlays.Count > 0)
            {
                try
                {
                    ComposeReplayMedia(input, output, capturedFrames, hasGameAudio, overlays, finish);
                    return;
                }
                catch (IOException exception)
                {
                    // A broken host video must not cost the whole render.
                    Main.Entry.Logger.Error("[Replay] The overlay videos could not be composed; finishing without them: "
                        + exception.Message);
                }
            }
            if (request?.HasMedia == true)
                ComposeReplayMedia(input, output, capturedFrames, hasGameAudio,
                    new System.Collections.Generic.List<TUFReplayRenderer.Media.MediaOverlay>(), finish);
            else File.Move(input, output);
        }

        private void ComposeReplayMedia(string input, string output, long capturedFrames, bool hasGameAudio,
            System.Collections.Generic.IReadOnlyList<TUFReplayRenderer.Media.MediaOverlay> overlays,
            BackgroundFinish finish)
        {
            var webcam = request.MediaEntry("webcam");
            var microphone = request.MediaEntry("microphone");
            string cameraPath = request.MediaPath("webcam");
            string microphonePath = request.MediaPath("microphone");
            Main.Entry.Logger.Log("[Replay] Media settings: webcam offset=" + (webcam?["offsetMs"]?.ToString() ?? "n/a")
                + " ms, microphone latency=" + (microphone?["latencyUs"]?.ToString() ?? "n/a")
                + " us, microphone gain=" + (microphone?["volume"]?.ToString() ?? "n/a")
                + ", overlay videos=" + overlays.Count + ".");
            var timeline = new TUFReplayRenderer.Media.RenderTimeline(driver.GameplayStartVideoTimeUs ?? 0,
                driver.GameplayRate, request.WonTimeUs);
            var compositionCodec = profile.FfmpegCodec.EndsWith("_vaapi", StringComparison.OrdinalIgnoreCase)
                || profile.FfmpegCodec.EndsWith("_vulkan", StringComparison.OrdinalIgnoreCase)
                ? VideoCodecCatalog.Get(profile.VideoCodec).SoftwareEncoder : profile.FfmpegCodec;
            // Only a microphone-only composition keeps the encoded picture.
            bool reencode = webcam != null || overlays.Count > 0;
            var encoding = !reencode ? new[] { "-c:v", "copy" }
                : new[] { "-c:v", compositionCodec,
                    "-b:v", profile.BitrateKbps + "k", "-pix_fmt", profile.VideoCodec == VideoCodec.ProRes ? "yuv422p10le" : profile.PixelFormat };
            if (reencode && profile.VideoCodec == VideoCodec.ProRes)
                encoding = new[] { "-c:v", compositionCodec, "-profile:v", ((int)profile.ProResProfile).ToString(),
                    compositionCodec == "prores_videotoolbox" ? "-allow_sw" : "-threads",
                    "0", "-pix_fmt", profile.PixelFormat };
            var composedPath = Path.ChangeExtension(output, ".composed" + profile.ContainerExtension);
            var plan = TUFReplayRenderer.Media.MediaCompositionPlan.Create(input, composedPath, profile.Width, profile.Height,
                profile.VideoFps, capturedFrames * 1000000d / profile.VideoFps, timeline,
                webcam, cameraPath, microphone, microphonePath, videoEncoding: encoding, captureGameAudio: hasGameAudio,
                overlays: overlays, audioDelayUs: audioDelaySeconds * 1000000d);
            var audioOption = plan.Arguments.IndexOf("-c:a");
            if (audioOption >= 0)
            {
                var audioMap = plan.Arguments.FindIndex(argument => argument == "0:a:0");
                bool copyGameAudio = audioMap > 0 && plan.Arguments[audioMap - 1] == "-map";
                plan.Arguments.RemoveRange(audioOption, 4);
                var codec = AudioCodecCatalog.Resolve(profile.AudioCodec, profile.ContainerExtension);
                // Webcam and HUD overlays only change the picture. The input
                // already uses the requested audio codec; preserve its packets.
                plan.Arguments.InsertRange(audioOption, copyGameAudio
                    ? new[] { "-c:a", "copy" }
                    : codec == AudioCodec.Flac
                    ? new[] { "-c:a", "flac" }
                    : new[] { "-c:a", AudioCodecCatalog.Encoder(codec), "-b:a", AudioCodecCatalog.Bitrate(codec) });
            }
            var cancellation = finish?.Token ?? CancellationToken.None;
            double durationUs = capturedFrames * 1000000d / profile.VideoFps;
            finish?.Report(FinishStage.Composing, 0);
            using (var process = new System.Diagnostics.Process { StartInfo = new System.Diagnostics.ProcessStartInfo {
                FileName = ffmpegPath, UseShellExecute = false, CreateNoWindow = true,
                RedirectStandardError = true, RedirectStandardOutput = true,
                Arguments = string.Join(" ", plan.Arguments.Select(QuoteFfmpegArgument))
            } })
            {
                var errors = new System.Text.StringBuilder();
                process.ErrorDataReceived += (sender, args) =>
                {
                    if (args.Data == null) return;
                    lock (errors)
                    {
                        errors.AppendLine(args.Data);
                        if (errors.Length > 16384) errors.Remove(0, errors.Length - 16384);
                    }
                };
                // -progress pipe:1 writes out_time_us as it goes: the share of the video composed.
                process.OutputDataReceived += (sender, args) =>
                {
                    var fraction = CompositionProgress(args.Data, durationUs);
                    if (fraction.HasValue) finish?.Report(FinishStage.Composing, fraction);
                };
                process.Start();
                process.BeginErrorReadLine();
                process.BeginOutputReadLine();
                while (!process.WaitForExit(200))
                {
                    if (!cancellation.IsCancellationRequested) continue;
                    try { process.Kill(); } catch (InvalidOperationException) { }
                    process.WaitForExit();
                    RenderRecovery.DeleteTemporary(composedPath);
                    cancellation.ThrowIfCancellationRequested();
                }
                process.WaitForExit(); // Drain redirected output before reading the errors.
                if (process.ExitCode != 0)
                {
                    RenderRecovery.DeleteTemporary(composedPath);
                    string message;
                    lock (errors) message = errors.ToString();
                    throw new IOException("Replay media composition failed: " + message);
                }
                cancellation.ThrowIfCancellationRequested();
                File.Move(composedPath, output);
            }
        }

        /// <summary>The share of the video composed, from one line of FFmpeg's -progress output.</summary>
        internal static double? CompositionProgress(string line, double durationUs)
        {
            const string Key = "out_time_us=";
            if (line == null || durationUs <= 0 || !line.StartsWith(Key, StringComparison.Ordinal)) return null;
            if (!long.TryParse(line.Substring(Key.Length), NumberStyles.Integer, CultureInfo.InvariantCulture, out var us)
                || us < 0) return null;
            return Math.Min(1, us / durationUs);
        }

        private static string QuoteFfmpegArgument(string value)
        {
            if (string.IsNullOrEmpty(value)) return "\"\"";
            var result = new System.Text.StringBuilder("\"");
            int slashes = 0;
            foreach (char c in value)
            {
                if (c == '\\') { slashes++; continue; }
                if (c == '"') result.Append('\\', slashes * 2 + 1);
                else result.Append('\\', slashes);
                slashes = 0;
                result.Append(c);
            }
            result.Append('\\', slashes * 2);
            return result.Append('"').ToString();
        }
    }
}
