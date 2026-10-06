using System;
using System.Globalization;
using System.IO;
using OrbitRender;

internal static class FileNameTemplateTests
{
    internal static void Run()
    {
        var now = new DateTime(2026, 10, 5, 12, 34, 56);
        var variables = OutputFormat.Variables(" My/Level ", now, "abc123");
        variables["width"] = 1920;
        variables["height"] = 1080;
        variables["videoFps"] = 60;
        variables["ingameFps"] = 240;
        variables["codec"] = "H264";
        variables["bgaMode"] = true;
        Action<string, string> check = (format, expected) => {
            var actual = OutputFormat.FileName(format, variables);
            if (actual != expected) throw new Exception(format + ": expected '" + expected + "', got '" + actual + "'.");
        };
        Action<string> reject = format => {
            try { OutputFormat.FileName(format, variables); }
            catch (FormatException) { return; }
            throw new Exception("Invalid template was accepted: " + format);
        };
        if (OutputFormat.FileName(null, "Level", now, "abc123") != "Render_Level_2026-10-05_12-34-56_abc123")
            throw new Exception("Legacy default filename changed.");
        if (OutputFormat.FileName("{level}_{date}_{time}_{id}", "Test/Level", now, "abc123")
            != "Test_Level_2026-10-05_12-34-56_abc123") throw new Exception("Legacy variable behavior changed.");
        check("{date:yyyyMMdd}_{time:HHmmss}", "20261005_123456");
        check("{time:HH:mm:ss}", "12_34_56");
        check("{width}x{height}_{videoFps}fps_{ingameFps}sim_{codec|lower}", "1920x1080_60fps_240sim_h264");
        check("{level|trim|replace:\"/\",\"-\"|lower|truncate:5}", "my-le");
        check("{level|upper|trim}", "MY_LEVEL");
        check("{artist|default:\"Unknown\"}", "Unknown");
        check("{artist|default:\"../escape\"}/{id}", Path.Combine("_escape", "abc123"));
        variables["artist"] = "Artist";
        check("{artist|default:\"Unknown\"}", "Artist");
        check("{if:bgaMode,\"BGA\",\"Gameplay\"}", "BGA");
        check("{if:!bgaMode,\"BGA\",\"Gameplay\"}", "Gameplay");
        variables["bgaMode"] = false;
        check("{if:bgaMode,\"BGA\",\"Gameplay\"|lower}", "gameplay");
        check("{if:artist,\"present\",\"missing\"}", "present");
        check("{{level}}_{id}", "{level}_abc123");
        check("{artist|replace:\"Artist\",\"a|b,c:d}e\"}", "a_b,c_d}e");
        check("{artist|replace:\"Artist\",\"a\\\"b\\\\c\"}", "a_b_c");
        check("{artist|replace:\"Artist\",\"\"|default:\"Empty\"}", "Empty");
        check("{artist|truncate:0}", "Render");
        check("../NUL", "_NUL");
        check("{level}/Render_{date}_{time}_{id}.mp4",
            Path.Combine("My_Level", "Render_2026-10-05_12-34-56_abc123"));
        check("exports\\{level}\\Render_{id}", Path.Combine("exports", "My_Level", "Render_abc123"));
        check("{date:yyyy/MM/dd}/{id}", Path.Combine("2026_10_05", "abc123"));
        check("{artist|default:\"../escape\"}/{id}", Path.Combine("Artist", "abc123"));
        check("exports/../CON/NUL.mp4", Path.Combine("exports", "_CON", "_NUL"));
        check("/exports//Render.mp4", Path.Combine("exports", "Render"));
        check("Render.MP4", "Render");
        check("{artist|replace:\"Artist\",\"CON\"}", "_CON");
        variables["artist"] = "a😀b";
        check("{artist|truncate:2}", "a");
        check("{artist|truncate:3}", "a😀");
        foreach (var invalid in new[] { "{unknown}", "{level", "level}", "{level|}", "{level|missing}",
            "{level|replace:\"\",\"x\"}", "{level|replace:a,b}", "{level|lower:1}",
            "{level|truncate:-1}", "{level|truncate:161}", "{level|truncate:abc}",
            "{artist|default:unquoted}", "{date:}", "{date:%}", "{width:00}",
            "{if:missing,\"a\",\"b\"}", "{if:bgaMode,\"a\"}", "{if:bgaMode,unquoted,\"b\"}",
            "{level|replace:\"a\",\"b}", "{level|replace:\"a\",\"\\q\"}", "{level{date}}" }) reject(invalid);
        reject(new string('a', 4097));
        variables["artist"] = new string('a', 159) + "😀";
        check("{artist}", new string('a', 159));
        var originalCulture = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("ar-SA");
            check("{date:yyyyMMdd}_{videoFps}", "20261005_60");
        }
        finally { CultureInfo.CurrentCulture = originalCulture; }

        var directory = Path.Combine(Path.GetTempPath(), "orbit-filename-" + Guid.NewGuid().ToString("N"));
        var nestedName = OutputFormat.FileName("{level}/Render_{id}.mp4", "wowcoollevel", now, "abc123");
        var first = OutputFormat.UniquePath(directory, nestedName, ".mp4");
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(first));
            File.WriteAllText(first, "keep");
            var next = OutputFormat.UniquePath(directory, nestedName, ".mp4");
            if (next != Path.Combine(directory, "wowcoollevel", "Render_abc123_1.mp4")
                || File.ReadAllText(first) != "keep") throw new Exception("Nested filename collision handling failed.");
            File.Delete(first);
            File.WriteAllText(Path.ChangeExtension(first, ".partial.mp4"), "pending");
            if (OutputFormat.UniquePath(directory, nestedName, ".mp4") != next)
                throw new Exception("Nested partial render collision handling failed.");
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }
}
