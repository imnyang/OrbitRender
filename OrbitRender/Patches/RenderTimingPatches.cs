using System;
using DG.Tweening.Core;
using HarmonyLib;

namespace OrbitRender.Patches
{
    // Timing transpilers are deliberately absent from Main.PatchAll. Installing
    // them only for an export avoids changing shared IL during game startup.
    internal static class RenderTimingPatches
    {
        private static Harmony harmony;

        internal static void Apply()
        {
            if (harmony != null) throw new InvalidOperationException("Render timing patches are already installed.");
            harmony = new Harmony(Main.Entry.Info.Id + ".RenderTiming");
            try
            {
                Patch(typeof(scrConductor), "Update", typeof(ConductorPatch));
                Patch(typeof(scrCountdown), "Update", typeof(ConductorPatch));
                // Wrap the standard clock before other transpilers replace its getter.
                // Their clock value still passes through unchanged outside our render.
                Patch(typeof(PlanetRenderer), "LateUpdate", typeof(PlanetRendererClockPatch), priority: Priority.First);
                Patch(typeof(scrPlanet), "Update", typeof(PlanetUpdateClockPatch), true);
                Patch(typeof(scrPlanet), "AutoShouldHitNow", typeof(AutoplayTimingPatch));
                Patch(typeof(DOTweenComponent), "Update", typeof(TweenClockPatch));
                RenderPatchCompatibility.Validate();
            }
            catch
            {
                Remove();
                throw;
            }
        }

        private static void Patch(Type target, string method, Type patch, bool postfix = false, int priority = Priority.Normal)
        {
            var original = AccessTools.DeclaredMethod(target, method);
            if (original == null) throw new MissingMethodException(target.FullName, method);
            harmony.Patch(original,
                postfix: postfix ? new HarmonyMethod(AccessTools.Method(patch, "Postfix")) : null,
                transpiler: new HarmonyMethod(AccessTools.Method(patch, "Transpiler")) { priority = priority });
        }

        internal static void Remove()
        {
            if (harmony == null) return;
            harmony.UnpatchAll(harmony.Id);
            harmony = null;
        }
    }
}
