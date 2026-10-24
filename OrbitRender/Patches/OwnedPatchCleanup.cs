using System.Linq;
using HarmonyLib;

namespace OrbitRender.Patches
{
    internal static class OwnedPatchCleanup
    {
        internal static void Remove(Harmony harmony)
        {
            if (harmony == null) return;
            // UnpatchAll resolves PatchMethod even for unrelated owners. After
            // UMM reloads an assembly, those cached method tokens can be stale.
            // Filter by owner metadata and remove by owner, without resolving
            // the methods belonging to patches that are about to be removed.
            foreach (var original in Harmony.GetAllPatchedMethods().ToArray())
            {
                var info = Harmony.GetPatchInfo(original);
                if (info != null && info.Owners.Contains(harmony.Id))
                    harmony.Unpatch(original, HarmonyPatchType.All, harmony.Id);
            }
        }
    }
}
