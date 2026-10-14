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
                Patch(typeof(PlanetRenderer), "LateUpdate", typeof(PlanetRendererClockPatch));
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

        private static void Patch(Type target, string method, Type patch, bool postfix = false)
        {
            var original = AccessTools.DeclaredMethod(target, method);
            if (original == null) throw new MissingMethodException(target.FullName, method);
            harmony.Patch(original,
                postfix: postfix ? new HarmonyMethod(AccessTools.Method(patch, "Postfix")) : null,
                transpiler: new HarmonyMethod(AccessTools.Method(patch, "Transpiler")));
        }

        internal static void Remove()
        {
            if (harmony == null) return;
            harmony.UnpatchAll(harmony.Id);
            harmony = null;
        }
    }
}
