using System;
using System.Collections.Generic;
using System.IO;

namespace OrbitRender
{
    internal static class ExportFileName
    {
        internal static string LevelName
        {
            get
            {
                var level = ADOBase.editor != null ? ADOBase.editor.customLevel : ADOBase.customLevel;
                var title = level != null && level.levelData != null ? level.levelData.song : null;
                if (!string.IsNullOrWhiteSpace(title)) return title;
                if (level != null && !string.IsNullOrWhiteSpace(level.levelPath))
                    return Path.GetFileNameWithoutExtension(level.levelPath);
                return ADOBase.controller != null && !string.IsNullOrWhiteSpace(ADOBase.controller.levelName)
                    && ADOBase.controller.levelName != "scnGame" ? ADOBase.controller.levelName : "Level";
            }
        }

        internal static Dictionary<string, object> Variables(RenderProfile profile, bool bgaMode, DateTime now, string id)
        {
            var variables = OutputFormat.Variables(LevelName, now, id);
            var level = ADOBase.editor != null ? ADOBase.editor.customLevel : ADOBase.customLevel;
            if (level != null && level.levelData != null) variables["artist"] = level.levelData.artist ?? string.Empty;
            variables["width"] = profile.Width;
            variables["height"] = profile.Height;
            variables["videoFps"] = profile.VideoFps;
            variables["ingameFps"] = profile.TargetFps;
            variables["bitrate"] = profile.BitrateMbps;
            variables["codec"] = profile.VideoCodec.ToString();
            variables["bitDepth"] = profile.BitDepth == VideoBitDepth.Ten ? 10 : 8;
            variables["bgaMode"] = bgaMode;
            return variables;
        }
    }
}
