using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using OrbitRender.Renderer;

namespace OrbitRender.Patches
{
    // ffxPlaySound calls AudioSource.PlayScheduled through AudioManager.Play.
    // During an accelerated render, the simulation can reach the event after
    // AudioRenderer has already consumed that point on the offline DSP
    // timeline. Unity cannot insert a scheduled source into a past block, so
    // the event becomes silent. Keep future schedules sample-accurate and move
    // only late gameplay-effect schedules to the next available sample.
    [HarmonyPatch(typeof(AudioManager), "Play")]
    internal static class AudioSchedulePatch
    {
        private static readonly HashSet<ffxPlaySound> preScheduled = new HashSet<ffxPlaySound>();

        static void Prefix(ref double time, int priority)
        {
            if (!RendererController.ControlsTime || priority != 128) return;
            RendererController.Instance?.ClampLatePlaySoundSchedule(ref time);
        }

        [HarmonyPatch]
        private static class PlaySoundEffectDispatchPatch
        {
            static IEnumerable<MethodBase> TargetMethods()
            {
                var noArgument = AccessTools.Method(typeof(ffxPlaySound), "StartEffect", Type.EmptyTypes);
                if (noArgument != null) yield return noArgument;
                var withPlanet = AccessTools.Method(typeof(ffxPlaySound), "StartEffect",
                    new[] { typeof(scrPlanet) });
                if (withPlanet != null) yield return withPlanet;
            }

            static bool Prefix(ffxPlaySound __instance)
            {
                return !RendererController.ControlsTime || __instance == null
                    || !preScheduled.Contains(__instance);
            }
        }

        internal static void PreSchedulePlaySoundEffects()
        {
            preScheduled.Clear();
            if (!RendererController.ControlsTime || ADOBase.lm == null
                || ADOBase.lm.listFloors == null) return;

            var effectsField = AccessTools.Field(typeof(scrFloor), "plusEffects");
            var startEffect = AccessTools.Method(typeof(ffxPlaySound), "StartEffect", Type.EmptyTypes);
            var ready = AccessTools.Method(typeof(ffxPlaySound), "get_ready");
            if (effectsField == null || startEffect == null || ready == null)
                throw new MissingMethodException("ADOFAI Play Sound Effect scheduling API changed.");

            var scheduled = 0;
            var skipped = 0;
            foreach (var floor in ADOBase.lm.listFloors)
            {
                if (floor == null) continue;
                var effects = effectsField.GetValue(floor) as IEnumerable;
                if (effects == null) continue;
                foreach (var effect in effects)
                {
                    var playSound = effect as ffxPlaySound;
                    if (playSound == null) continue;
                    if (!(bool)ready.Invoke(playSound, null))
                    {
                        skipped++;
                        continue;
                    }

                    // StartEffect() computes the exact same authored DSP time
                    // as the normal gameplay path, but this call happens
                    // before AudioRenderer consumes any output block.
                    startEffect.Invoke(playSound, null);
                    preScheduled.Add(playSound);
                    scheduled++;
                }
            }
            Main.Entry.Logger.Log("Pre-scheduled " + scheduled
                + " Play Sound Effect event(s) before audio capture."
                + (skipped > 0 ? " Skipped " + skipped + " event(s) whose clip was not ready." : ""));
        }

        internal static void ResetRuntimeState()
        {
            preScheduled.Clear();
        }
    }
}
