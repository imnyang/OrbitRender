using System;

namespace OrbitRender
{
    public enum AudioCodec { Auto, AAC, Opus }

    internal static class AudioCodecCatalog
    {
        internal static AudioCodec Normalize(AudioCodec codec) =>
            Enum.IsDefined(typeof(AudioCodec), codec) ? codec : AudioCodec.Auto;

        internal static bool IsSupported(AudioCodec codec, string extension)
        {
            extension = (extension ?? string.Empty).ToLowerInvariant();
            if (codec == AudioCodec.Auto) return true;
            if (codec == AudioCodec.AAC)
                return extension == ".mp4" || extension == ".mkv" || extension == ".mov" || extension == ".ts";
            if (codec == AudioCodec.Opus)
                return extension == ".mp4" || extension == ".mkv" || extension == ".webm";
            return false;
        }

        internal static AudioCodec CompatibleSelection(AudioCodec codec, string extension) =>
            IsSupported(Normalize(codec), extension) ? Normalize(codec) : AudioCodec.Auto;

        internal static AudioCodec Resolve(AudioCodec codec, string extension)
        {
            codec = Normalize(codec);
            if (!IsSupported(codec, extension))
                throw new NotSupportedException(codec + " audio is not supported in " + extension + ".");
            return codec == AudioCodec.Auto
                ? (string.Equals(extension, ".webm", StringComparison.OrdinalIgnoreCase) ? AudioCodec.Opus : AudioCodec.AAC)
                : codec;
        }

        internal static string Encoder(AudioCodec codec) => codec == AudioCodec.Opus ? "libopus" : "aac";
        internal static string Bitrate(AudioCodec codec) => codec == AudioCodec.Opus ? "160k" : "320k";
        internal static string EncodingArguments(AudioCodec codec) =>
            " -c:a " + Encoder(codec) + " -b:a " + Bitrate(codec)
            + (codec == AudioCodec.Opus ? " -ar 48000 -vbr on -application audio" : string.Empty);
        internal static string IntermediateExtension(AudioCodec codec) => codec == AudioCodec.Opus ? ".opus" : ".m4a";
    }
}
