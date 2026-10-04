using System;
using System.Collections.Generic;

namespace OrbitRender
{
    internal static class ExportFileName
    {
        internal static Dictionary<string, object> Variables(RenderProfile profile, bool bgaMode, DateTime now, string id)
        {
            var variables = OutputFormat.Variables(ADOBase.controller != null ? ADOBase.controller.levelName : "Level", now, id);
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
