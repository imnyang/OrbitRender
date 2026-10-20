using System;
using System.IO;
using Newtonsoft.Json;

namespace OrbitRender.Renderer
{
    // Paths are derived from the manifest name, so moving a recovery bundle
    // to a different folder works without trusting paths inside JSON.
    internal sealed class RenderRecovery
    {
        public int Version = 1;
        public double AudioOffset;
        public float GainDb;
        public AudioCodec Codec;
        internal const string Suffix = ".recovery.json";

        internal static string VideoPath(string output) => Path.ChangeExtension(output, ".partial" + Path.GetExtension(output));
        internal static string AudioPath(string output) => Path.ChangeExtension(output, ".partial.wav");
        internal static string MuxPath(string output) => Path.ChangeExtension(output, ".mux" + Path.GetExtension(output));

        internal void Save(string output)
        {
            using (var writer = new StreamWriter(new FileStream(output + Suffix, FileMode.CreateNew, FileAccess.Write)))
                writer.Write(JsonConvert.SerializeObject(this, Formatting.Indented));
        }

        internal static string Retry(string executable, string manifest)
        {
            if (!manifest.EndsWith(Suffix, StringComparison.OrdinalIgnoreCase)) throw new IOException("Select a .recovery.json file.");
            var output = Path.GetFullPath(manifest.Substring(0, manifest.Length - Suffix.Length));
            var recovery = JsonConvert.DeserializeObject<RenderRecovery>(File.ReadAllText(manifest));
            if (recovery == null || recovery.Version != 1 || !Enum.IsDefined(typeof(AudioCodec), recovery.Codec)
                || double.IsNaN(recovery.AudioOffset) || double.IsInfinity(recovery.AudioOffset) || recovery.AudioOffset < 0
                || float.IsNaN(recovery.GainDb) || recovery.GainDb < -60 || recovery.GainDb > 12)
                throw new IOException("Invalid recovery information.");
            if (File.Exists(output)) throw new IOException("Output already exists: " + output);
            if (!File.Exists(VideoPath(output)) || !File.Exists(AudioPath(output)))
                throw new FileNotFoundException("The preserved video and WAV must be next to the recovery file.");
            // A unique destination avoids touching a previous failed mux file.
            var mux = output + ".retry-" + Guid.NewGuid().ToString("N") + Path.GetExtension(output);
            try
            {
                var available = RenderResources.FreeBytes(Path.GetDirectoryName(output));
                var needed = checked(new FileInfo(VideoPath(output)).Length + 256L * 1024 * 1024);
                if (available.HasValue && available.Value < needed) throw new IOException("Insufficient disk space for recovery.");
                FFmpegEncoder.MuxAudio(executable, VideoPath(output), AudioPath(output), mux,
                    recovery.AudioOffset, recovery.GainDb, recovery.Codec);
                File.Move(mux, output);
            }
            finally { DeleteTemporary(mux); }
            DeleteTemporary(VideoPath(output)); DeleteTemporary(AudioPath(output));
            DeleteTemporary(MuxPath(output));
            DeleteTemporary(Path.ChangeExtension(output, ".partial" + AudioCodecCatalog.IntermediateExtension(recovery.Codec)));
            DeleteTemporary(manifest);
            return output;
        }

        internal static void DeleteTemporary(string path)
        {
            try { if (File.Exists(path)) File.Delete(path); } catch { /* A completed export stays successful. */ }
        }
    }
}
