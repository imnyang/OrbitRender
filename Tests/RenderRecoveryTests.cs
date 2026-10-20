using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;

internal static class RenderRecoveryTests
{
    private static void Assert(bool value, string message) { if (!value) throw new Exception(message); }
    internal static void Run(string mod, string managed, string ffmpeg, string directory)
    {
        AppDomain.CurrentDomain.AssemblyResolve += (sender, args) => {
            var path = Path.Combine(managed, new AssemblyName(args.Name).Name + ".dll");
            return File.Exists(path) ? Assembly.LoadFrom(path) : null;
        };
        var assembly = Assembly.LoadFrom(Path.GetFullPath(mod));
        var type = assembly.GetType("OrbitRender.Renderer.RenderRecovery", true);
        var retry = type.GetMethod("Retry", BindingFlags.Static | BindingFlags.NonPublic);
        Directory.CreateDirectory(directory);
        var output = Path.GetFullPath(Path.Combine(directory, "recovered.mp4"));
        var manifest = output + ".recovery.json";
        var video = Path.ChangeExtension(output, ".partial.mp4");
        var audio = Path.ChangeExtension(output, ".partial.wav");
        void Reject(string executable)
        {
            try { retry.Invoke(null, new object[] { executable, manifest }); }
            catch (TargetInvocationException ex) when (ex.InnerException is IOException
                || ex.InnerException is System.ComponentModel.Win32Exception) { return; }
            throw new Exception("Recovery should have failed.");
        }
        File.WriteAllText(manifest, "{\"Version\":99}");
        Reject(ffmpeg);
        var recovery = Activator.CreateInstance(type, true);
        type.GetField("AudioOffset").SetValue(recovery, 0d);
        File.Delete(manifest);
        type.GetMethod("Save", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(recovery, new object[] { output });
        Reject(ffmpeg); // Missing sources.
        Generate(ffmpeg, "-f lavfi -i color=c=blue:s=320x180:r=30 -t 1 -an -c:v libx264 -pix_fmt yuv420p", video);
        Generate(ffmpeg, "-f lavfi -i sine=frequency=440:sample_rate=48000 -t 1 -c:a pcm_f32le", audio);
        File.WriteAllText(output, "keep");
        Reject(ffmpeg);
        Assert(File.ReadAllText(output) == "keep", "Existing output was overwritten.");
        File.Delete(output);
        File.Move(audio, audio + ".backup");
        File.WriteAllText(audio, "invalid audio");
        Reject(ffmpeg);
        Assert(File.Exists(video) && File.Exists(audio) && File.Exists(manifest), "Mux failure deleted its sources.");
        File.Delete(audio); File.Move(audio + ".backup", audio);
        Reject(Path.Combine(directory, "missing-ffmpeg.exe"));
        Assert(File.Exists(video) && File.Exists(audio) && File.Exists(manifest), "Failed recovery deleted its sources.");
        // Renaming/moving the complete bundle must not depend on stored paths.
        var moved = Path.Combine(directory, "moved"); Directory.CreateDirectory(moved);
        var newOutput = Path.Combine(moved, "recovered.mp4");
        File.Move(video, Path.ChangeExtension(newOutput, ".partial.mp4"));
        File.Move(audio, Path.ChangeExtension(newOutput, ".partial.wav"));
        File.Move(manifest, newOutput + ".recovery.json");
        var result = (string)retry.Invoke(null, new object[] { ffmpeg, newOutput + ".recovery.json" });
        Assert(File.Exists(result) && new FileInfo(result).Length > 1000, "Recovered output missing.");
        using (var decode = Process.Start(new ProcessStartInfo(ffmpeg,
            "-hide_banner -loglevel error -nostdin -i \"" + result + "\" -map 0:v:0 -map 0:a:0 -f null -") {
            UseShellExecute = false, CreateNoWindow = true, RedirectStandardError = true
        }))
        {
            var errors = decode.StandardError.ReadToEndAsync();
            if (!decode.WaitForExit(30000)) { decode.Kill(); throw new Exception("Recovery decode timed out."); }
            Assert(decode.ExitCode == 0, "Recovered audio/video did not decode: " + errors.GetAwaiter().GetResult());
        }
        Assert(!File.Exists(newOutput + ".recovery.json") && !File.Exists(Path.ChangeExtension(newOutput, ".partial.mp4")), "Successful recovery left sources.");
        Console.WriteLine("PASS: recovery validation, missing sources, no overwrite, failure preservation, relocated bundle and FFmpeg mux.");
    }

    private static void Generate(string ffmpeg, string arguments, string output)
    {
        using (var process = Process.Start(new ProcessStartInfo(ffmpeg, "-hide_banner -loglevel error -nostdin -n " + arguments + " \"" + output + "\"") {
            UseShellExecute = false, CreateNoWindow = true, RedirectStandardError = true
        }))
        {
            var errors = process.StandardError.ReadToEndAsync();
            if (!process.WaitForExit(30000)) { process.Kill(); throw new Exception("Test media generation timed out."); }
            Assert(process.ExitCode == 0, errors.GetAwaiter().GetResult());
        }
    }
}
