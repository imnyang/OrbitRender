using System;
using System.Runtime.CompilerServices;
using OrbitRender.TufReplay;

namespace OrbitRender { internal static class Main { internal static bool Enabled = true; internal static TestEntry Entry = new TestEntry(); } }
namespace OrbitRender { internal sealed class TestEntry { internal TestLogger Logger = new TestLogger(); } internal sealed class TestLogger { internal void Log(string text) {} } }
namespace TUFReplay.Shared.NativeInput
{
    internal struct Input { public int Key, NativeCode; public ulong NativeFlags; }
    internal static class NativeInputKeyCodeMapper
    {
        [MethodImpl(MethodImplOptions.NoInlining)]
        public static bool TryGetUnityKeyName(string platform, Input input, out string name) { name = null; return false; }
    }
}
internal static class KoreanReplayKeyTests
{
    internal static void Run()
    {
        if (!TufReplayKoreanKeyCompatibility.TryInstallWhenAvailable()) throw new Exception("Available TUFReplay mapper was not patched.");
        try
        {
            string name;
            var input = new TUFReplay.Shared.NativeInput.Input { Key = 21, NativeCode = 56, NativeFlags = 1 };
            if (!TUFReplay.Shared.NativeInput.NativeInputKeyCodeMapper.TryGetUnityKeyName("windows", input, out name) || name != "RightAlt") throw new Exception("Korean key press was not mapped.");
            input.NativeFlags = 129;
            if (!TUFReplay.Shared.NativeInput.NativeInputKeyCodeMapper.TryGetUnityKeyName("windows", input, out name) || name != "RightAlt") throw new Exception("Korean key release was not mapped.");
            input.NativeCode = 0;
            if (TUFReplay.Shared.NativeInput.NativeInputKeyCodeMapper.TryGetUnityKeyName("windows", input, out name)) throw new Exception("Unknown key was incorrectly mapped.");
            input.NativeCode = 56;
            if (TUFReplay.Shared.NativeInput.NativeInputKeyCodeMapper.TryGetUnityKeyName("macos", input, out name)) throw new Exception("Other platforms must not be remapped.");
            OrbitRender.Main.Enabled = false;
            if (TUFReplay.Shared.NativeInput.NativeInputKeyCodeMapper.TryGetUnityKeyName("windows", input, out name)) throw new Exception("Disabled mod must not alter export.");
        }
        finally { TufReplayKoreanKeyCompatibility.Remove(); OrbitRender.Main.Enabled = true; }
        Console.WriteLine("TUF replay Korean key mapping tests passed.");
    }
}
