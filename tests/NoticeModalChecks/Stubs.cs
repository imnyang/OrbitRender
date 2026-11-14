using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
[assembly: System.Runtime.CompilerServices.InternalsVisibleTo("Assembly-CSharp-Editor")]
namespace OrbitRender.UI { internal class OverlayDropdown : UnityEngine.UI.Dropdown { public int[] SeparatorIndices; } }
namespace UnityModManagerNet {
    public class UnityModManager {
        public class ModEntry { public Info Info = new Info(); public bool Enabled = true; }
        public class Info { public string Id, DisplayName, Version; }
    }
}
namespace OrbitRender {
    public static class Main { public static bool Enabled = true; public static object Settings = new object(); public static Entry Entry = new Entry(); }
    public class Entry { public Logger Logger = new Logger(); }
    public class Logger { public void Error(string message) { } }
    public static class Localization {
        public static string Language = "ko";
        public static string Get(string key) {
            var line = File.ReadLines(Path.Combine(UnityEngine.Application.dataPath, Language + ".ftl")).FirstOrDefault(l => l.StartsWith(key + " = "));
            return line == null ? key : line.Substring(key.Length + 3);
        }
        public static string Format(string key, object value) => string.Format(Get(key), value);
    }
}
namespace OrbitRender.Renderer {
    public class RendererController {
        public static RendererController Instance = new RendererController();
        public bool HasTimingCompatibilityFailure = true;
        public string[] ConflictModIds = new string[0];
        public string DiagnosticReport = "fixture diagnostic report";
    }
    public static class ConflictRecovery {
        public static UnityModManagerNet.UnityModManager.ModEntry[] Mods = new UnityModManagerNet.UnityModManager.ModEntry[0];
        public static bool CanRestart = true, FailDisable, FailRestart;
        public static int Disabled, Restarted;
        public static UnityModManagerNet.UnityModManager.ModEntry[] Candidates(string[] ids) => Mods;
        public static void DisableForNextLaunch(UnityModManagerNet.UnityModManager.ModEntry mod) {
            if (FailDisable) throw new IOException("fixture save denied");
            mod.Enabled = false; Disabled++;
        }
        public static void Restart() { if(FailRestart) throw new IOException("fixture launch denied"); Restarted++; }
    }
}
