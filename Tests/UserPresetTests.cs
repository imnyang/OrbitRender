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
            // Deliberately leave stale custom dimensions under a built-in preset:
            // capture must save the effective built-in output, without GPU queries.
            Set(source, "Preset", Enum.Parse(settingsType.GetField("Preset").FieldType, "QHD"));
            Set(source, "Width", 320);
            Set(source, "Height", 180);
            Set(source, "Fps", 120);
            Set(source, "VideoFps", 30);
            Set(source, "AudioGainDb", -6f);
            Set(source, "BgaMode", true);
            Set(source, "CaptureAudio", false);
            Set(source, "FileNameFormat", "{level}/BGA_{id}");
            var library = Settings();
            Assert(Call(storeType, "Save", null, library, "  BGA  ", source, null) == null, "Save failed.");
            var items = (System.Collections.IList)Get(library, "UserPresets");
            var preset = items[0];
            Assert((string)Get(preset, "Name") == "BGA", "Name was not trimmed.");
            Assert((int)Get(preset, "Width") == 2560 && (int)Get(preset, "Height") == 1440,
                "Built-in output dimensions were not captured.");
            Assert((int)Get(preset, "BitrateMbps") == 30, "Built-in bitrate was not captured.");
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
            Call(settingsType, "ResetToDefaults", library);
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
            var storageType = assembly.GetType("OrbitRender.SettingsStorage");
            if (storageType != null)
            {
                Assert(!assembly.GetReferencedAssemblies().Any(reference => reference.Name == "UnityModManager"),
                    "Alternate loader build still requires Unity Mod Manager.");
                var directory = Path.Combine(Path.GetTempPath(), "orbitrender-settings-" + Guid.NewGuid().ToString("N"));
                var settingsPath = Path.Combine(directory, "config", "OrbitRender.xml");
                try
                {
                    var logType = assembly.GetType("OrbitRender.ModLog", true);
                    var errors = 0;
                    var log = Activator.CreateInstance(logType, BindingFlags.Instance | BindingFlags.NonPublic,
                        null, new object[] { new Action<string>(_ => { }), new Action<string>(_ => errors++) }, null);
                    var fresh = Call(storageType, "Load", null, settingsPath, log);
                    Assert((int)Get(fresh, "Fps") == 60, "Missing settings did not use defaults.");
                    Set(fresh, "Fps", 144);
                    Set(fresh, "AudioGainDb", -4f);
                    Assert(Call(storeType, "Save", null, fresh, "Persisted", source, null) == null, "Preset persistence setup failed.");
                    Call(storageType, "Save", null, settingsPath, fresh);
                    var saved = Call(storageType, "Load", null, settingsPath, log);
                    Assert((int)Get(saved, "Fps") == 144 && (float)Get(saved, "AudioGainDb") == -4f,
                        "Loader settings values were lost.");
                    Assert(((System.Collections.IList)Get(saved, "UserPresets")).Count == 1,
                        "Loader settings lost the custom preset library.");
                    Set(saved, "Fps", 90);
                    Call(storageType, "Save", null, settingsPath, saved);
                    Assert((int)Get(Call(storageType, "Load", null, settingsPath, log), "Fps") == 90,
                        "Existing loader settings were not replaced.");
                    Assert(!File.Exists(settingsPath + ".tmp"), "Temporary settings file was left behind.");
                    File.WriteAllText(settingsPath, "invalid XML");
                    var recovered = Call(storageType, "Load", null, settingsPath, log);
                    Assert((int)Get(recovered, "Fps") == 60 && errors == 1,
                        "Corrupt loader settings did not recover with a diagnostic.");
                    Console.WriteLine("PASS: loader settings creation, replacement, preset persistence and corrupt-file recovery.");
                }
                finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
            }
            Console.WriteLine("PASS: custom preset snapshots, built-in values, loading, XML persistence, legacy settings, names, overwrite, rename and reset.");
        }
        finally { AppDomain.CurrentDomain.AssemblyResolve -= resolve; }
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
    }
}
