using System;
using System.Linq;
using System.Reflection;
using System.Collections;
using System.Threading;
using HarmonyLib;

namespace OrbitRender.Renderer
{
    internal sealed class JrpRenderGuard : IDisposable
    {
        private readonly Harmony harmony = new Harmony("OrbitRender.JrpRenderGuard");
        private object viewer;
        private FieldInfo eventOffset;
        private object originalOffset;
        private static JrpRenderGuard current;
        private int inputEvents, rainReports;
        private int lastReportedInput;

        internal static JrpRenderGuard Begin(bool replay)
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
                if (replay)
                {
                    var type = assembly.GetType("JipperResourcePack.KeyViewerContents.KeyViewer", false);
                    guard.viewer = type == null ? null : AccessTools.Field(type, "Instance")?.GetValue(null);
                    guard.eventOffset = type == null ? null : AccessTools.Field(type, "_eventOffsetTicks");
                    if (guard.viewer != null && guard.eventOffset != null && guard.eventOffset.FieldType == typeof(long))
                    {
                        guard.originalOffset = guard.eventOffset.GetValue(guard.viewer);
                        guard.eventOffset.SetValue(guard.viewer, long.MinValue >> 1);
                    }
                    var inputMethod = type == null ? null : AccessTools.DeclaredMethod(type, "ProcessKeyEvent");
                    var rainType = assembly.GetType("JipperResourcePack.KeyViewerContents.RainManager", false);
                    var rainUpdate = rainType == null ? null : AccessTools.DeclaredMethod(rainType, "Update");
                    current = guard;
                    if (inputMethod != null) guard.harmony.Patch(inputMethod, postfix: new HarmonyMethod(typeof(JrpRenderGuard), nameof(CountInput)));
                    if (rainUpdate != null) guard.harmony.Patch(rainUpdate, postfix: new HarmonyMethod(typeof(JrpRenderGuard), nameof(InspectRain)));
                    Main.Entry.Logger.Log("[Replay] JRP rain diagnostics: input=" + (inputMethod != null) + ", update=" + (rainUpdate != null));
                }
                Main.Entry.Logger.Log("[Replay] JRP play-count protection enabled for " + assembly.GetName().Name + ".");
                return guard;
            }
            catch { guard.Dispose(); throw; }
        }

        private static bool BlockMutation() => false;

        private static void CountInput() { var guard = current; if (guard != null) Interlocked.Increment(ref guard.inputEvents); }

        private static void InspectRain(object __instance)
        {
            var guard = current;
            if (guard == null || guard.rainReports >= 12) return;
            int inputCount = Volatile.Read(ref guard.inputEvents);
            if (inputCount == 0 || inputCount == guard.lastReportedInput) return;
            try
            {
                var list = AccessTools.Field(__instance.GetType(), "RainList")?.GetValue(__instance) as IList;
                guard.lastReportedInput = inputCount;
                guard.rainReports++;
                string detail = "";
                if (list != null && list.Count > 0 && list[0] != null)
                {
                    var rain = list[0];
                    var rainType = rain.GetType();
                    var rect = AccessTools.Field(rainType, "Transform")?.GetValue(rain);
                    var raw = AccessTools.Field(rainType, "RawRain")?.GetValue(rain);
                    var start = raw == null ? null : AccessTools.Field(raw.GetType(), "StartTime")?.GetValue(raw);
                    var gameObject = rect == null ? null : AccessTools.Property(rect.GetType(), "gameObject")?.GetValue(rect, null);
                    detail = ", startTicks=" + start + ", size=" + (rect == null ? null : AccessTools.Property(rect.GetType(), "sizeDelta")?.GetValue(rect, null))
                        + ", position=" + (rect == null ? null : AccessTools.Property(rect.GetType(), "anchoredPosition")?.GetValue(rect, null))
                        + ", layer=" + (gameObject == null ? null : AccessTools.Property(gameObject.GetType(), "layer")?.GetValue(gameObject, null))
                        + ", active=" + (gameObject == null ? null : AccessTools.Property(gameObject.GetType(), "activeInHierarchy")?.GetValue(gameObject, null));
                }
                Main.Entry.Logger.Log("[Replay] JRP rain: inputs=" + inputCount + ", activeRains=" + (list == null ? -1 : list.Count) + detail);
            }
            catch (Exception error) { guard.rainReports = 12; Main.Entry.Logger.Log("[Replay] JRP rain diagnostics unavailable: " + error.Message); }
        }

        internal void RestoreInputClock()
        {
            if (viewer != null && eventOffset != null && originalOffset != null) eventOffset.SetValue(viewer, originalOffset);
            viewer = null; eventOffset = null; originalOffset = null;
        }

        public void Dispose()
        {
            try { RestoreInputClock(); }
            finally
            {
                if (current == this)
                {
                    Main.Entry.Logger.Log("[Replay] JRP rain diagnostic summary: inputs=" + Volatile.Read(ref inputEvents) + ", samples=" + rainReports);
                    current = null;
                }
                harmony.UnpatchAll(harmony.Id);
            }
        }
    }
}
