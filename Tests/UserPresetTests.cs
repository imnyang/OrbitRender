using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Xml.Serialization;

// Runs against the built mod without invoking Unity's native graphics/audio APIs.
internal static class UserPresetTests
{
    internal static void Run(string modPath, string managedDirectory)
    {
        var root = Path.GetDirectoryName(Path.GetFullPath(modPath));
        ResolveEventHandler resolve = (sender, args) => {
            var name = new AssemblyName(args.Name).Name + ".dll";
            foreach (var directory in new[] { root, managedDirectory,
                Path.Combine(managedDirectory, "UnityModManager"),
                Path.GetFullPath(Path.Combine(root, "../../../packages/UnityModManager/lib/net35")) })
            {
                var path = Path.Combine(directory, name);
                if (File.Exists(path)) return Assembly.LoadFrom(path);
            }
            return null;
        };
        AppDomain.CurrentDomain.AssemblyResolve += resolve;
        try
        {
            var assembly = Assembly.LoadFrom(modPath);
            InstallerProcessTests.Run(assembly);
            ExportWorkflowTests.Run(assembly);
            PatchCompatibilityTests.Run(assembly, managedDirectory);
            var settingsType = assembly.GetType("OrbitRender.RendererSettings", true);
            var presetType = assembly.GetType("OrbitRender.UserRenderPreset", true);
            var storeType = assembly.GetType("OrbitRender.UserRenderPresets", true);
            object Settings() => Activator.CreateInstance(settingsType);
            void Set(object obj, string field, object value) => obj.GetType().GetField(field).SetValue(obj, value);
            object Get(object obj, string field) => obj.GetType().GetField(field).GetValue(obj);
            object Call(Type type, string method, object target, params object[] args)
                => type.GetMethod(method, BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static)
                    .Invoke(target, args);
            var source = Settings();
            foreach (var option in new[] { "DiagnosticTiming", "SettleFastPath", "SampleGameplayAtOutputFrames" })
            {
                Assert(!(bool)Get(source, option), option + " must be disabled by default.");
                Set(source, option, true);
            }
            CheckPerformanceOptions(assembly);
            Set(source, "BufferPolicy", Enum.Parse(settingsType.GetField("BufferPolicy").FieldType, "LowMemory"));
            foreach (var delay in new[] { 0f, 31f, 3600f, float.MaxValue })
                Assert((float)Call(settingsType, "NormalizeEndDelay", null, delay) == delay,
                    "Valid end delay was capped.");
            foreach (var delay in new[] { float.NaN, float.PositiveInfinity, float.NegativeInfinity })
                Assert((float)Call(settingsType, "NormalizeEndDelay", null, delay) == 2f,
                    "Non-finite end delay was accepted.");
            Assert((float)Call(settingsType, "NormalizeEndDelay", null, -1f) == 0f,
                "Negative end delay was accepted.");
            // Deliberately leave stale custom dimensions under a built-in preset:
            // capture must save the effective built-in output, without GPU queries.
            Set(source, "Preset", Enum.Parse(settingsType.GetField("Preset").FieldType, "QHD"));
            Set(source, "Width", 320);
            Set(source, "Height", 180);
            Set(source, "Fps", 120);
            Set(source, "VideoFps", 30);
            Set(source, "AudioGainDb", -6f);
            Set(source, "EndDelaySeconds", 3600f);
            Set(source, "BgaMode", true);
            Set(source, "CaptureAudio", false);
            Set(source, "FileNameFormat", "{level}/BGA_{id}");
            Set(source, "Encoder", Enum.Parse(settingsType.GetField("Encoder").FieldType, "Software"));
            Set(source, "Codec", Enum.Parse(settingsType.GetField("Codec").FieldType, "ProRes"));
            Set(source, "ProResProfile", Enum.Parse(settingsType.GetField("ProResProfile").FieldType, "FourFourFourFourXQ"));
            var resolveProfile = settingsType.GetMethods(BindingFlags.NonPublic | BindingFlags.Instance)
                .Single(method => method.Name == "ResolveProfile" && method.GetParameters().Length == 0);
            var resolvedProfile = resolveProfile.Invoke(source, null);
            Assert((float)resolvedProfile.GetType().GetProperty("EndDelaySeconds").GetValue(resolvedProfile) == 3600f,
                "Built-in preset ignored the saved end delay.");
            var resolveOverrides = settingsType.GetMethods(BindingFlags.NonPublic | BindingFlags.Instance)
                .Single(method => method.Name == "ResolveProfile" && method.GetParameters().Length == 14);
            var overrides = new object[14];
            overrides[0] = Enum.Parse(settingsType.GetField("Preset").FieldType, "Preview");
            overrides[6] = 7f;
            resolvedProfile = resolveOverrides.Invoke(source, overrides);
            Assert((float)resolvedProfile.GetType().GetProperty("EndDelaySeconds").GetValue(resolvedProfile) == 7f,
                "Explicit end delay did not override the saved setting.");
            overrides[6] = null;
            resolvedProfile = resolveOverrides.Invoke(source, overrides);
            Assert((float)resolvedProfile.GetType().GetProperty("EndDelaySeconds").GetValue(resolvedProfile) == 3600f,
                "Explicit video preset replaced the saved end delay.");
            var library = Settings();
            Assert(Call(storeType, "Save", null, library, "  BGA  ", source, null) == null, "Save failed.");
            var items = (System.Collections.IList)Get(library, "UserPresets");
            var preset = items[0];
            Assert(Get(preset, "ProResProfile").ToString() == "FourFourFourFourXQ", "Preset lost ProRes profile.");
            Assert((string)Get(preset, "Name") == "BGA", "Name was not trimmed.");
            Assert((int)Get(preset, "Width") == 2560 && (int)Get(preset, "Height") == 1440,
                "Built-in output dimensions were not captured.");
            Assert((int)Get(preset, "BitrateMbps") == 30, "Built-in bitrate was not captured.");
            Assert((float)Get(preset, "EndDelaySeconds") == 3600f, "Preset capped end delay.");
            Assert(Get(preset, "BufferPolicy").ToString() == "LowMemory", "Preset lost buffer policy.");
            Assert((string)Call(storeType, "Save", null, library, "bga", source, null)
                == "user-preset-duplicate-name", "Case-insensitive duplicate accepted.");
            Assert((string)Call(storeType, "Save", null, library, " ", source, null)
                == "user-preset-invalid-name", "Empty name accepted.");
            Assert((string)Call(storeType, "Save", null, library, new string('a', 65), source, null)
                == "user-preset-invalid-name", "Long name accepted.");
            Assert((string)Call(storeType, "Save", null, library, "a\nb", source, null)
                == "user-preset-invalid-name", "Control characters accepted.");
            Set(source, "Fps", 240);
            Assert((int)Get(preset, "Fps") == 120, "Preset shares live settings.");

            var serializer = new XmlSerializer(settingsType);
            object restored;
            using (var xml = new StringWriter())
            {
                serializer.Serialize(xml, library);
                using (var reader = new StringReader(xml.ToString())) restored = serializer.Deserialize(reader);
            }
            var roundTrip = ((System.Collections.IList)Get(restored, "UserPresets"))[0];
            foreach (var field in presetType.GetFields())
                Assert(Equals(field.GetValue(preset), field.GetValue(roundTrip)), "Persistence lost " + field.Name);
            var target = Settings();
            Set(target, "OutputDirectory", "keep-output");
            Set(target, "FfmpegExecutable", "keep-ffmpeg");
            Call(presetType, "ApplyTo", roundTrip, target);
            Assert(Get(target, "Preset").ToString() == "Custom", "Loading did not select Custom.");
            foreach (var field in presetType.GetFields().Where(f => f.Name != "Name"))
                Assert(Equals(field.GetValue(roundTrip), Get(target, field.Name)), "Loading lost " + field.Name);
            Assert((string)Get(target, "OutputDirectory") == "keep-output"
                && (string)Get(target, "FfmpegExecutable") == "keep-ffmpeg", "Machine paths changed.");
            Assert(Call(storeType, "Rename", null, library, preset, "Renamed") == null, "Rename failed.");
            Assert(items.Count == 1 && (string)Get(preset, "Name") == "Renamed", "Rename duplicated item.");
            Assert(Call(storeType, "Save", null, library, "Renamed", source, preset) == null, "Overwrite failed.");
            Assert(items.Count == 1 && (int)Get(items[0], "Fps") == 240, "Overwrite did not replace snapshot.");
            foreach (var name in new[] { "DiagnosticTiming", "SettleFastPath", "SampleGameplayAtOutputFrames" })
                Set(library, name, true);
            Call(settingsType, "ResetToDefaults", library);
            foreach (var name in new[] { "DiagnosticTiming", "SettleFastPath", "SampleGameplayAtOutputFrames" })
                Assert(!(bool)Get(library, name), name + " must reset to disabled.");
            Assert(Get(library, "BufferPolicy").ToString() == "Balanced", "Reset lost default buffer policy.");
            Assert(items.Count == 1, "Reset deleted presets.");
            // A settings file from before custom presets must still load.
            using (var reader = new StringReader("<RendererSettings><Fps>90</Fps></RendererSettings>"))
            {
                var legacy = serializer.Deserialize(reader);
                Assert(((System.Collections.IList)Get(legacy, "UserPresets")).Count == 0,
                    "Legacy settings did not get an empty preset library.");
            }
            items.RemoveAt(0);
            Assert(items.Count == 0, "Delete failed.");
            Console.WriteLine("PASS: custom preset snapshots, built-in values, loading, XML persistence, legacy settings, names, overwrite, rename and reset.");
        }
        finally { AppDomain.CurrentDomain.AssemblyResolve -= resolve; }
    }

    private static void CheckPerformanceOptions(Assembly assembly)
    {
        var api = assembly.GetType("OrbitRender.RenderApi", true);
        var parse = api.GetMethod("TryReadOptions", BindingFlags.NonPublic | BindingFlags.Static);
        var create = assembly.GetType("OrbitRender.Renderer.RenderJobService", true)
            .GetMethod("ParseOptions", BindingFlags.NonPublic | BindingFlags.Static);
        foreach (bool enabled in new[] { false, true })
        {
            var json = Newtonsoft.Json.Linq.JObject.Parse("{\"diagnosticTiming\":" + enabled.ToString().ToLowerInvariant()
                + ",\"settleFastPath\":" + enabled.ToString().ToLowerInvariant()
                + ",\"sampleGameplayAtOutputFrames\":" + enabled.ToString().ToLowerInvariant() + "}");
            var args = new object[] { json, null, null, null };
            Assert((bool)parse.Invoke(null, args), "Performance options failed to parse.");
            foreach (var name in new[] { "DiagnosticTiming", "SettleFastPath", "SampleGameplayAtOutputFrames" })
                Assert(Equals(args[1].GetType().GetProperty(name).GetValue(args[1]), enabled), name + " lost its explicit value.");
            var converted = new object[] { args[1], null };
            var options = create.Invoke(null, converted);
            Assert(options != null && converted[1] == null, "Performance-only request lost its options.");
            foreach (var name in new[] { "DiagnosticTiming", "SettleFastPath", "SampleGameplayAtOutputFrames" })
                Assert(Equals(options.GetType().GetField(name).GetValue(options), enabled), name + " lost its RPC value.");
        }
        var absent = new object[] { new Newtonsoft.Json.Linq.JObject(), null, null, null };
        Assert((bool)parse.Invoke(null, absent), "Older request without performance options was rejected.");
        var unchanged = new object[] { absent[1], null };
        Assert(create.Invoke(null, unchanged) == null && unchanged[1] == null, "Absent flags must preserve saved settings.");
        var malformed = new object[] { Newtonsoft.Json.Linq.JObject.Parse("{\"diagnosticTiming\":{}}"), null, null, null };
        Assert(!(bool)parse.Invoke(null, malformed), "Invalid timing option was accepted.");
        var jobType = assembly.GetType("OrbitRender.Renderer.RpcRenderJob", true);
        var job = Activator.CreateInstance(jobType, "test", "level", false, null);
        var progress = jobType.GetMethod("SetProgress");
        var stamp = jobType.GetField("updatedUtc", BindingFlags.NonPublic | BindingFlags.Instance);
        progress.Invoke(job, new object[] { 3L, 10L, "out.mp4" });
        var originalStamp = new DateTime(2000, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        stamp.SetValue(job, originalStamp);
        progress.Invoke(job, new object[] { 3L, 10L, "out.mp4" });
        Assert((DateTime)stamp.GetValue(job) == originalStamp, "Unchanged progress rewrote the RPC timestamp.");
        progress.Invoke(job, new object[] { 4L, 10L, "out.mp4" });
        Assert((DateTime)stamp.GetValue(job) != originalStamp, "Changed progress failed to update the RPC timestamp.");
        Console.WriteLine("PASS: performance options default off, preserve explicit booleans and reject malformed input.");
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
    }
}
