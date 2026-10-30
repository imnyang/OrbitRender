using System;
using System.Reflection;
using HarmonyLib;

namespace OrbitRender.TufReplay
{
    // Windows Korean keyboards report the physical right-Alt key as VK_HANGUL.
    internal static class TufReplayKoreanKeyCompatibility
    {
        private static Harmony harmony;

        internal static bool TryMap(string platform, int key, int scanCode, ulong flags, out string name)
        {
            name = null;
            if (!string.Equals(platform, "windows", StringComparison.OrdinalIgnoreCase)
                || key != 0x15 || scanCode != 0x38 || (flags & 1) == 0) return false;
            name = "RightAlt";
            return true;
        }

        internal static void EnsureInstalled()
        {
            if (harmony != null) return;
            Type type = AccessTools.TypeByName("TUFReplay.Shared.NativeInput.NativeInputKeyCodeMapper");
            MethodInfo target = type == null ? null : AccessTools.Method(type, "TryGetUnityKeyName");
            if (target == null) throw new InvalidOperationException("This TUFReplay version does not expose its render key conversion.");
            var patcher = new Harmony("OrbitRender.TufReplayKoreanKeyCompatibility");
            try { patcher.Patch(target, prefix: new HarmonyMethod(typeof(TufReplayKoreanKeyCompatibility), nameof(Prefix))); harmony = patcher; }
            catch { patcher.UnpatchAll(patcher.Id); throw; }
        }

        private static bool Prefix(object[] __args, ref bool __result)
        {
            if (!Main.Enabled || __args == null || __args.Length != 3 || __args[1] == null) return true;
            object input = __args[1];
            Type type = input.GetType();
            var key = AccessTools.Field(type, "Key");
            var code = AccessTools.Field(type, "NativeCode");
            var flags = AccessTools.Field(type, "NativeFlags");
            if (key == null || code == null || flags == null) return true;
            if (!TryMap(__args[0] as string, (int)key.GetValue(input), (int)code.GetValue(input), (ulong)flags.GetValue(input), out var name)) return true;
            __args[2] = name;
            __result = true;
            return false;
        }

        internal static void Remove() { if (harmony == null) return; harmony.UnpatchAll(harmony.Id); harmony = null; }
    }
}
