using System;
using System.Collections.Generic;
using System.Globalization;

namespace OrbitRender.UI
{
    internal sealed class ExportFieldError
    {
        internal readonly string Field, Message;
        internal readonly int Tab;
        internal ExportFieldError(string field, string message, int tab = 0) { Field = field; Message = message; Tab = tab; }
    }

    internal static class ExportValidation
    {
        internal static string ContainerError(VideoCodec codec, VideoContainer container)
        {
            var extension = OutputFormat.Extension(container, codec);
            if (codec == VideoCodec.ProRes && extension != ".mov" && extension != ".mkv") return "field-prores-container";
            if (codec == VideoCodec.FFV1 && extension != ".mkv") return "field-ffv1-container";
            if ((codec == VideoCodec.VP9 || codec == VideoCodec.AV1) && extension == ".ts") return "field-ts-container";
            return null;
        }
        internal static List<ExportFieldError> Check(bool custom, string width, string height, string bitrate,
            string targetFps, string videoFps, string endDelay)
        {
            var errors = new List<ExportFieldError>();
            if (custom)
            {
                Integer(errors, "width", width, 320, 3840, true);
                Integer(errors, "height", height, 180, 2160, true);
                Integer(errors, "bitrate", bitrate, 1, 200);
            }
            Integer(errors, "ingame-fps", targetFps, 15, 1024);
            Integer(errors, "video-fps", videoFps, 15, 240);
            if (!(float.TryParse(endDelay, NumberStyles.Float, CultureInfo.InvariantCulture, out var delay)
                || float.TryParse(endDelay, NumberStyles.Float, CultureInfo.CurrentCulture, out delay))
                || float.IsNaN(delay) || float.IsInfinity(delay) || delay < 0)
                errors.Add(new ExportFieldError("end-delay-seconds", "end-delay-must-be-nonnegative", 1));
            return errors;
        }

        private static void Integer(List<ExportFieldError> errors, string field, string text, int min, int max, bool even = false)
        {
            if (!int.TryParse(text, out var value) || value < min || value > max)
                errors.Add(new ExportFieldError(field, "field-range-" + field));
            else if (even && (value & 1) != 0)
                errors.Add(new ExportFieldError(field, "width-and-height-must-be-even-numbers"));
        }
    }
}
