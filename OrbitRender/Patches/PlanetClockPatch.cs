using System;
using System.Collections.Generic;
using System.Reflection.Emit;
using HarmonyLib;
using UnityEngine;
using OrbitRender.Renderer;

namespace OrbitRender.Patches
{
    // PlanetRenderer's expo pulse uses wall-clock unscaledTime. During a
    // render the game can run faster than the output timeline, which
    // makes that pulse and the planet's visual state advance at the wrong
    // speed. Keep it on the same deterministic clock as the song.
    internal static class PlanetRendererClockPatch
    {
        private static float UnscaledTime(float actual)
        {
            return RendererController.ControlsTime
                ? (float)RendererController.Instance.Clock.Time
                : actual;
        }

        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions, System.Reflection.MethodBase __originalMethod)
        {
            var getter = AccessTools.PropertyGetter(typeof(Time), nameof(Time.unscaledTime));
            var replacement = AccessTools.Method(typeof(PlanetRendererClockPatch), nameof(UnscaledTime));
            var count = 0;
            foreach (var instruction in instructions)
            {
                yield return instruction;
                if (instruction.Calls(getter))
                {
                    yield return new CodeInstruction(OpCodes.Call, replacement);
                    count++;
                }
            }
            RenderPatchCompatibility.Report(__originalMethod, count != 0, "unscaled clock call was not found");
        }
    }

    // Keep the planet's non-tween state transitions at the output frame rate
    // as well. Time.captureFramerate normally does this, but the game can
    // replace capture timing while custom levels or filters are loading.
    internal static class PlanetUpdateClockPatch
    {
        private static readonly HashSet<int> updatedPlanetsThisFrame = new HashSet<int>();
        private static int trackedFrame = -1;

        private static float DeltaTime(float actual)
        {
            return RendererController.ControlsTime
                ? RendererController.DeterministicDelta
                : actual;
        }

        internal static bool WasUpdatedThisFrame(scrPlanet planet)
        {
            return planet != null && trackedFrame == Time.frameCount
                && updatedPlanetsThisFrame.Contains(planet.GetInstanceID());
        }

        private static void Postfix(scrPlanet __instance)
        {
            if (!RendererController.ControlsTime) return;
            if (__instance == null) return;
            if (trackedFrame != Time.frameCount)
            {
                trackedFrame = Time.frameCount;
                updatedPlanetsThisFrame.Clear();
            }
            updatedPlanetsThisFrame.Add(__instance.GetInstanceID());
        }

        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions, System.Reflection.MethodBase __originalMethod)
        {
            var getter = AccessTools.PropertyGetter(typeof(Time), nameof(Time.deltaTime));
            var replacement = AccessTools.Method(typeof(PlanetUpdateClockPatch), nameof(DeltaTime));
            var count = 0;
            foreach (var instruction in instructions)
            {
                yield return instruction;
                if (instruction.Calls(getter))
                {
                    yield return new CodeInstruction(OpCodes.Call, replacement);
                    count++;
                }
            }
            RenderPatchCompatibility.Report(__originalMethod, count != 0, "delta clock call was not found");
        }
    }

    // Otto checks the planet's current angle from the controller Update. If
    // that runs before scrPlanet.Update, it sees the previous simulation
    // frame and can trigger one frame after the tile crossing. Predict only
    // in that ordering case so autoplay remains aligned with the rendered
    // planet position without changing normal gameplay.
    internal static class AutoplayTimingPatch
    {
        private static float AdjustMargin(scrPlanet planet, float margin)
        {
            var renderer = RendererController.Instance;
            if (!RendererController.ControlsTime || renderer == null
                || renderer.State != RenderState.Rendering
                || PlanetUpdateClockPatch.WasUpdatedThisFrame(planet)
                || planet == null || planet.planetarySystem == null)
                return margin;

            var conductor = ADOBase.conductor;
            if (conductor == null || conductor.crotchetAtStart <= 0.0
                || renderer.Clock.Fps <= 0) return margin;

            var pitch = conductor.song != null ? Math.Abs(conductor.song.pitch) : 1.0;
            var frameAngle = 180.0 * Math.Abs(planet.planetarySystem.speed) * pitch
                / (conductor.crotchetAtStart * renderer.Clock.Fps);
            if (double.IsNaN(frameAngle) || double.IsInfinity(frameAngle)) return margin;
            return margin + (float)frameAngle;
        }

        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions, System.Reflection.MethodBase __originalMethod)
        {
            var adjustMargin = AccessTools.Method(typeof(AutoplayTimingPatch), nameof(AdjustMargin));
            var replaced = 0;
            foreach (var instruction in instructions)
            {
                if (instruction.opcode == OpCodes.Ldc_R4
                    && instruction.operand is float value
                    && (Math.Abs(value - 0.5f) < 0.0001f || Math.Abs(value - 10f) < 0.0001f))
                {
                    var loadPlanet = new CodeInstruction(OpCodes.Ldarg_0);
                    loadPlanet.labels.AddRange(instruction.labels);
                    loadPlanet.blocks.AddRange(instruction.blocks);
                    instruction.labels.Clear();
                    instruction.blocks.Clear();
                    yield return loadPlanet;
                    yield return instruction;
                    yield return new CodeInstruction(OpCodes.Call, adjustMargin);
                    replaced++;
                    continue;
                }
                yield return instruction;
            }
            RenderPatchCompatibility.Report(__originalMethod, replaced != 0, "default auto margin was not found");
        }
    }
}
