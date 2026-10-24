using System;
using System.Collections;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;

internal static class PatchCompatibilityTests
{
    internal static void Run(Assembly mod, string managedDirectory)
    {
        var harmony = Assembly.LoadFrom(System.IO.Path.Combine(managedDirectory, "UnityModManager", "0Harmony.dll"));
        var instructionType = harmony.GetType("HarmonyLib.CodeInstruction", true);
        var unity = Assembly.LoadFrom(System.IO.Path.Combine(managedDirectory, "UnityEngine.CoreModule.dll"));
        var audio = Assembly.LoadFrom(System.IO.Path.Combine(managedDirectory, "UnityEngine.AudioModule.dll"));
        var game = Assembly.LoadFrom(System.IO.Path.Combine(managedDirectory, "Assembly-CSharp.dll"));
        var guard = mod.GetType("OrbitRender.Patches.RenderPatchCompatibility", true);
        var flags = BindingFlags.Static | BindingFlags.NonPublic;
        var validate = guard.GetMethod("Validate", flags);
        foreach (var name in new[] { "ConductorPatch", "PlanetRendererClockPatch", "PlanetUpdateClockPatch", "AutoplayTimingPatch", "TweenClockPatch" })
            Assert(!mod.GetType("OrbitRender.Patches." + name, true).CustomAttributes.Any(attribute => attribute.AttributeType.FullName == "HarmonyLib.HarmonyPatch"),
                "Timing patch is still installed by startup PatchAll: " + name);
        void Check(string patchName, string originalType, string originalName, Type clockType, string property, string adjustment)
        {
            var patch = mod.GetType("OrbitRender.Patches." + patchName, true);
            var transpiler = patch.GetMethod("Transpiler", flags);
            var original = game.GetType(originalType, true).GetMethod(originalName,
                BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            var getter = clockType.GetProperty(property).GetGetMethod();
            object Instruction(OpCode opcode, object operand) => Activator.CreateInstance(instructionType, opcode, operand);
            object[] Rewrite(params object[] input)
            {
                var array = Array.CreateInstance(instructionType, input.Length);
                for (int i = 0; i < input.Length; i++) array.SetValue(input[i], i);
                return ((IEnumerable)transpiler.Invoke(null, new object[] { array, original })).Cast<object>().ToArray();
            }
            var call = Instruction(OpCodes.Call, getter);
            var ret = Instruction(OpCodes.Ret, null);
            var output = Rewrite(call, ret);
            Assert(output.Length == 3 && ReferenceEquals(output[0], call) && ReferenceEquals(output[2], ret), "Original instruction order changed.");
            Assert(Equals(instructionType.GetField("operand").GetValue(output[0]), getter), "Another mod can no longer find the getter.");
            var adjust = patch.GetMethod(adjustment, flags);
            Assert(Equals(instructionType.GetField("operand").GetValue(output[1]), adjust), "Render adjustment missing.");
            // Simulate an earlier transpiler replacing the getter entirely.
            var foreignCall = Instruction(OpCodes.Call, typeof(PatchCompatibilityTests).GetMethod("ForeignClock", flags));
            output = Rewrite(foreignCall, ret);
            Assert(output.Length == 2 && ReferenceEquals(output[0], foreignCall), "Missing getter changed foreign IL.");
            bool blocked = false;
            try { validate.Invoke(null, null); }
            catch (TargetInvocationException ex) { blocked = ex.InnerException is InvalidOperationException; }
            Assert(blocked, "Unsafe rendering was allowed after an incompatible patch.");
            Rewrite(call, ret);
            validate.Invoke(null, null);
        }
        Check("ConductorPatch", "scrConductor", "Update", audio.GetType("UnityEngine.AudioSettings", true), "dspTime", "DspTime");
        Check("PlanetRendererClockPatch", "PlanetRenderer", "LateUpdate", unity.GetType("UnityEngine.Time", true), "unscaledTime", "UnscaledTime");
        Check("PlanetUpdateClockPatch", "scrPlanet", "Update", unity.GetType("UnityEngine.Time", true), "deltaTime", "DeltaTime");
        Console.WriteLine("PASS: timing patches excluded from startup, original clock calls preserved, foreign IL retained, incompatible rendering blocked and patch recovery.");
        CheckOwnedPatchCleanup(mod, harmony);
    }

    private static void CheckOwnedPatchCleanup(Assembly mod, Assembly harmonyAssembly)
    {
        var harmonyType = harmonyAssembly.GetType("HarmonyLib.Harmony", true);
        var harmonyMethodType = harmonyAssembly.GetType("HarmonyLib.HarmonyMethod", true);
        var flags = BindingFlags.Static | BindingFlags.NonPublic;
        var target = typeof(PatchCompatibilityTests).GetMethod(nameof(CleanupTarget), flags);
        var patch = harmonyType.GetMethods().Single(method => method.Name == "Patch" && method.GetParameters().Length == 5);
        var cleanup = mod.GetType("OrbitRender.Patches.OwnedPatchCleanup", true).GetMethod("Remove", flags);
        var owned = Activator.CreateInstance(harmonyType, "OrbitRender.Tests.Cleanup");
        var foreign = Activator.CreateInstance(harmonyType, "OrbitRender.Tests.Foreign");
        object Postfix(string name) => Activator.CreateInstance(harmonyMethodType,
            new object[] { typeof(PatchCompatibilityTests).GetMethod(name, flags) });
        try
        {
            patch.Invoke(foreign, new[] { target, null, Postfix(nameof(ForeignCleanupPostfix)), null, null });
            for (int i = 0; i < 3; i++)
            {
                patch.Invoke(owned, new[] { target, null, Postfix(nameof(OwnedCleanupPostfix)), null, null });
                Assert(CleanupTarget() == 111, "Cleanup test patches were not installed.");
                if (i == 1)
                    SimulateStalePatchToken(harmonyAssembly, owned, target);
                cleanup.Invoke(null, new[] { owned });
                Assert(CleanupTarget() == 101, "Owner cleanup removed another mod's patch or retained its own.");
                cleanup.Invoke(null, new[] { owned });
                Assert(CleanupTarget() == 101, "Repeated owner cleanup changed foreign patches.");
            }
        }
        finally
        {
            cleanup.Invoke(null, new[] { owned });
            cleanup.Invoke(null, new[] { foreign });
        }
        Assert(CleanupTarget() == 1, "Cleanup left test patches installed.");
        Console.WriteLine("PASS: three reload cleanup cycles, stale token InvalidCastException recovery, foreign patches preserved and repeated cleanup.");
    }

    private static void SimulateStalePatchToken(Assembly harmonyAssembly, object owner, MethodInfo target)
    {
        var flags = BindingFlags.Static | BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
        var shared = harmonyAssembly.GetType("HarmonyLib.HarmonySharedState", true);
        var info = shared.GetMethod("GetPatchInfo", flags).Invoke(null, new object[] { target });
        var postfixes = (Array)info.GetType().GetField("postfixes", flags).GetValue(info);
        var ownerId = (string)owner.GetType().GetProperty("Id").GetValue(owner);
        var stale = postfixes.Cast<object>().Single(item => (string)item.GetType().GetField("owner").GetValue(item) == ownerId);
        // A reloaded assembly can reuse a method token for a constructor.
        // Harmony casts ResolveMethod's result to MethodInfo and throws.
        stale.GetType().GetField("patchMethod", flags).SetValue(stale, null);
        stale.GetType().GetField("methodToken", flags).SetValue(stale, typeof(StaleToken).GetConstructor(Type.EmptyTypes).MetadataToken);
        var serialization = harmonyAssembly.GetType("HarmonyLib.PatchInfoSerialization", true);
        var bytes = serialization.GetMethod("Serialize", flags).Invoke(null, new[] { info });
        var state = (IDictionary)shared.GetField("state", flags).GetValue(null);
        var previous = state[target];
        state[target] = bytes;
        bool reproduced = false;
        try
        {
            owner.GetType().GetMethod("UnpatchAll").Invoke(owner, new object[] { ownerId });
        }
        catch (TargetInvocationException ex) when (ex.InnerException is InvalidCastException)
        {
            reproduced = true;
        }
        catch
        {
            state[target] = previous;
            throw;
        }
        if (!reproduced) state[target] = previous;
        Assert(reproduced, "Stale token did not reproduce the reported UnpatchAll failure.");
    }

    private sealed class StaleToken { public StaleToken() { } }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static int CleanupTarget() => 1;
    private static void OwnedCleanupPostfix(ref int __result) { __result += 10; }
    private static void ForeignCleanupPostfix(ref int __result) { __result += 100; }

    private static double ForeignClock() => 42;
    private static void Assert(bool condition, string message) { if (!condition) throw new Exception(message); }
}
