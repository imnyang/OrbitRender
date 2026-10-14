using System;
using System.Collections;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;

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
    }

    private static double ForeignClock() => 42;
    private static void Assert(bool condition, string message) { if (!condition) throw new Exception(message); }
}
