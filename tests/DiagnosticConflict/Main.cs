using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using UnityModManagerNet;

namespace OrbitRenderDiagnosticConflict
{
    public static class Main
    {
        private const string Owner = "OrbitRender.DiagnosticConflict";
        private static Harmony harmony;
        private static UnityModManager.ModEntry entry;

        public static bool Load(UnityModManager.ModEntry mod)
        {
            entry = mod;
            mod.OnToggle = Toggle;
            mod.OnUnload = unused => { Remove(); return true; };
            return true;
        }

        private static bool Toggle(UnityModManager.ModEntry mod, bool enabled)
        {
            Remove();
            if (!enabled) return true;
            try
            {
                var target = AccessTools.DeclaredMethod(AccessTools.TypeByName("PlanetRenderer"), "LateUpdate");
                if (target == null) throw new MissingMethodException("PlanetRenderer.LateUpdate");
                harmony = new Harmony(Owner);
                harmony.Patch(target, transpiler: new HarmonyMethod(typeof(Main), nameof(HideClock)) {
                    priority = Priority.First,
                    before = new[] { "OrbitRender.RenderTiming" }
                });
                mod.Logger.Log("TEST ENABLED: PlanetRenderer clock is wrapped to provoke OrbitRender timing compatibility failure. Disable this mod and restart after testing.");
                return true;
            }
            catch (Exception ex)
            {
                Remove();
                mod.Logger.Error("Test setup failed: " + ex);
                return false;
            }
        }

        private static void Remove()
        {
            harmony?.UnpatchAll(Owner);
            harmony = null;
        }

        private static float ReadClock() => Time.unscaledTime;

        private static IEnumerable<CodeInstruction> HideClock(IEnumerable<CodeInstruction> instructions)
        {
            var getter = AccessTools.PropertyGetter(typeof(Time), nameof(Time.unscaledTime));
            var wrapper = AccessTools.Method(typeof(Main), nameof(ReadClock));
            var count = 0;
            foreach (var instruction in instructions)
            {
                if (instruction.Calls(getter))
                {
                    // Preserve branch labels and exception blocks on the original instruction.
                    instruction.operand = wrapper;
                    count++;
                }
                yield return instruction;
            }
            entry?.Logger?.Log("TEST transpiler: wrapped " + count + " unscaledTime call(s)."
                + (count == 0 ? " Target already lacks this clock; it may already fail compatibility without the test mod." : ""));
        }
    }
}
