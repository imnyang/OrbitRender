using System;
using System.Linq;
using HarmonyLib;

namespace OrbitRender.Renderer
{
    internal sealed class JrpRenderGuard : IDisposable
    {
        private readonly Harmony harmony = new Harmony("OrbitRender.JrpRenderGuard");
        internal static JrpRenderGuard Begin()
        {
            var count = AppDomain.CurrentDomain.GetAssemblies()
                .Select(a => a.GetType("JipperResourcePack.OverlayContents.PlayCount", false))
                .FirstOrDefault(t => t != null);
            if (count == null) return null;
            var assembly = count.Assembly;
            var guard = new JrpRenderGuard();
            try
            {
                foreach (var name in new[] { "AddAttempts", "RemoveAttempts", "SetBest" })
                {
                    var method = AccessTools.DeclaredMethod(count, name);
                    if (method == null || !method.IsStatic || method.ReturnType != typeof(void))
                        throw new MissingMethodException(count.FullName, name);
                    guard.harmony.Patch(method, prefix: new HarmonyMethod(typeof(JrpRenderGuard), nameof(BlockMutation)) { priority = Priority.First });
                }
                Main.Entry.Logger.Log("JRP play-count protection enabled for " + assembly.GetName().Name + ".");
                return guard;
            }
            catch { guard.Dispose(); throw; }
        }

        private static bool BlockMutation() => false;

        public void Dispose()
        {
            harmony.UnpatchAll(harmony.Id);
        }
    }
}
