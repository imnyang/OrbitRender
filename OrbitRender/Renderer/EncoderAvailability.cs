using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace OrbitRender.Renderer
{
    // Probe on a worker: FFmpeg's build flags alone do not establish that a
    // hardware encoder can actually open on this machine and driver.
    internal static class EncoderAvailability
    {
        internal sealed class Result
        {
            internal volatile bool Complete;
            internal VideoEncoder[] Encoders = new VideoEncoder[0];
        }

        internal sealed class Choice
        {
            internal readonly VideoCodec Codec;
            internal readonly VideoEncoder Encoder;
            internal Choice(VideoCodec codec, VideoEncoder encoder) { Codec = codec; Encoder = encoder; }
        }

        internal sealed class CombinedResult
        {
            private readonly Result[] results;
            internal CombinedResult(Result[] results) { this.results = results; }
            internal bool Complete => results.All(r => r.Complete);
            internal Choice[] Choices => results.SelectMany((r, i) => !r.Complete || r.Encoders.Length == 0
                ? new Choice[0] : new[] { new Choice((VideoCodec)i, VideoEncoder.Auto) }
                    .Concat(r.Encoders.Select(e => new Choice((VideoCodec)i, e)))).ToArray();
        }

        internal static CombinedResult GetCombined(string executable, VideoBitDepth depth, VideoContainer container)
        {
            return new CombinedResult(new[] { VideoCodec.H264, VideoCodec.H265, VideoCodec.VP9, VideoCodec.AV1 }
                .Select(codec => Get(executable, codec, depth, container)).ToArray());
        }

        internal static int SelectionIndex(Choice[] choices, VideoCodec codec, VideoEncoder encoder)
        {
            var index = Array.FindIndex(choices, c => c.Codec == codec && c.Encoder == encoder);
            if (index < 0) index = Array.FindIndex(choices, c => c.Codec == codec && c.Encoder == VideoEncoder.Auto);
            return index < 0 ? 0 : index;
        }

        private static readonly Dictionary<string, Result> Cache = new Dictionary<string, Result>();
        private static readonly SemaphoreSlim ProbeSlot = new SemaphoreSlim(1, 1);
        internal static void Refresh() { lock (Cache) Cache.Clear(); }
        internal static Result Get(string executable, VideoCodec codec, VideoBitDepth depth, VideoContainer container)
        {
            var extension = OutputFormat.Extension(container, codec);
            var key = executable + "|" + codec + "|" + depth + "|" + extension;
            lock (Cache)
            {
                if (Cache.TryGetValue(key, out var cached)) return cached;
                var result = new Result();
                Cache.Add(key, result);
                ThreadPool.QueueUserWorkItem(state => {
                    ProbeSlot.Wait();
                    try
                    {
                    var available = new List<VideoEncoder>();
                    var definition = VideoCodecCatalog.Get(codec);
                    foreach (var backend in new[] { VideoEncoder.NvidiaNvenc, VideoEncoder.IntelQsv,
                        VideoEncoder.AmdAmf, VideoEncoder.Vaapi, VideoEncoder.Software })
                    {
                        var encoder = definition.ResolveEncoder(backend, false, false, false);
                        if (backend != VideoEncoder.Software && !VideoCodecCatalog.IsHardwareEncoder(encoder)) continue;
                        try
                        {
                            if (FFmpegEncoder.TryValidateVideo(executable, 2, "ultrafast", encoder,
                                depth == VideoBitDepth.Ten ? "yuv420p10le" : "yuv420p", extension, false, out _))
                                available.Add(backend);
                        }
                        catch { /* A failed probe must never advertise an encoder. */ }
                    }
                    result.Encoders = available.ToArray();
                    result.Complete = true;
                    }
                    finally { ProbeSlot.Release(); }
                });
                return result;
            }
        }

        internal static string Label(VideoEncoder backend, VideoCodec codec)
        {
            var definition = VideoCodecCatalog.Get(codec);
            var name = backend == VideoEncoder.Auto ? "Auto" : backend == VideoEncoder.NvidiaNvenc ? "NVIDIA NVENC"
                : backend == VideoEncoder.IntelQsv ? "Intel QuickSync"
                : backend == VideoEncoder.AmdAmf ? "AMD AMF"
                : backend == VideoEncoder.Vaapi ? "VAAPI"
                : "Software (fallback)";
            return name + " (" + definition.DisplayName + ")";
        }
    }
}
