using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using SkyHook;
using UnityEngine;
using UnityEngine.Events;

namespace TUFReplayRenderer.Replay;

internal sealed class SharedOverlayInput : IDisposable
{
    private static SharedOverlayInput active;
    private readonly Harmony harmony = new("KGH1113.TUFReplayRenderer.SharedInput");
    private readonly Dictionary<KeyCode, (KeyLabel label, ushort native)> mappings = new();
    private readonly HashSet<KeyCode> held = new(), down = new(), up = new();
    private readonly RecordedReplayDriver driver;
    private readonly OverlayVideoClock videoClock;
    private static readonly FieldInfo SecondsField = AccessTools.Field(typeof(SkyHookEvent), "TimeSec"), NanosField = AccessTools.Field(typeof(SkyHookEvent), "TimeSubsecNano"),
        TypeField = AccessTools.Field(typeof(SkyHookEvent), "Type"), LabelField = AccessTools.Field(typeof(SkyHookEvent), "Label"), KeyField = AccessTools.Field(typeof(SkyHookEvent), "Key");
    private readonly DateTime epoch = new(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);
    [ThreadStatic] private static bool dispatching;
    private Action<SkyHookEvent> hub;
    private bool requireFocus;
    private bool disposed;
    internal SharedOverlayInput(RecordedReplayDriver driver) { this.driver = driver; videoClock = OptionalModClock.Clock; }
    internal static bool Active => active != null;
    internal void Begin()
    {
        if (active != null) throw new InvalidOperationException("Shared replay input is already active.");
        active = this;
        try
        {
            var manager = SkyHookManager.Instance;
            requireFocus = manager.requireFocus;
            manager.requireFocus = false;
            hub = (Action<SkyHookEvent>)Delegate.CreateDelegate(typeof(Action<SkyHookEvent>), manager, AccessTools.Method(typeof(SkyHookManager), "HookCallback"));
            harmony.Patch(AccessTools.Method(typeof(SkyHookManager), "NativeHookCallback"), prefix: new HarmonyMethod(typeof(SharedOverlayInput), nameof(BlockNative)) { priority = Priority.First });
            harmony.Patch(AccessTools.Method(typeof(UnityEvent<SkyHookEvent>), "Invoke", new[] { typeof(SkyHookEvent) }), prefix: new HarmonyMethod(typeof(SharedOverlayInput), nameof(AllowSharedEvent)) { priority = Priority.First });
            harmony.Patch(AccessTools.PropertyGetter(typeof(SkyHookManager), "isHookActive"), prefix: new HarmonyMethod(typeof(SharedOverlayInput), nameof(HookActive)));
            // Reset start-held state through the shared event, including viewers
            // that maintain their own physical-held cache. No mod fields touched.
            foreach (KeyLabel label in Enum.GetValues(typeof(KeyLabel)))
            {
                if (label == KeyLabel.Unknown || SkyHookKeyMapper.SkyHookKeyToUnityKey(label) == KeyCode.None) continue;
                ushort native = SkyHookKeyMapper.KeyLabelToNativeKeyCode(label);
                Emit((label, native), false, 0);
            }
        }
        catch { Dispose(); throw; }
    }
    internal void BeforeFrame() { down.Clear(); up.Clear(); }
    internal void Apply(RecordedKeyEvent input, KeyCode code)
    {
        if (input.Down) { held.Add(code); down.Add(code); } else { held.Remove(code); up.Add(code); }
        if (!mappings.TryGetValue(code, out var mapping))
        {
            KeyLabel label = SkyHookKeyMapper.UnityKeyToSkyHookKey(code);
            ushort native = SkyHookKeyMapper.KeyLabelToNativeKeyCode(label);
            if (label == KeyLabel.Unknown) throw new InvalidOperationException("This keyboard key cannot be replayed through the game's shared input: " + code + ".");
            mappings.Add(code, mapping = (label, native));
        }
        Emit(mapping, input.Down, driver.CurrentVideoTimeUs / 1e6);
    }
    private void Emit((KeyLabel label, ushort native) mapping, bool pressed, double seconds)
    {
        long ticks = (videoClock.UtcAt(seconds) - epoch).Ticks;
        object ev = default(SkyHookEvent);
        SecondsField.SetValue(ev, ticks / TimeSpan.TicksPerSecond);
        NanosField.SetValue(ev, checked((uint)(ticks % TimeSpan.TicksPerSecond * 100)));
        TypeField.SetValue(ev, pressed ? SkyHook.EventType.KeyPressed : SkyHook.EventType.KeyReleased);
        LabelField.SetValue(ev, mapping.label); KeyField.SetValue(ev, mapping.native);
        bool previousDispatch = dispatching;
        dispatching = true;
        try { using (videoClock.Work.Dispatch(seconds)) hub((SkyHookEvent)ev); }
        finally { dispatching = previousDispatch; }
    }
    internal static bool Held(KeyCode code) => active == null ? Input.GetKey(code) : active.held.Contains(code);
    internal static bool Down(KeyCode code) => active == null ? Input.GetKeyDown(code) : active.down.Contains(code);
    internal static bool Up(KeyCode code) => active == null ? Input.GetKeyUp(code) : active.up.Contains(code);
    internal static bool Focused() => active != null || Application.isFocused;
    private static bool BlockNative() => active == null;
    private static bool AllowSharedEvent(object __instance) => active == null || !ReferenceEquals(__instance, SkyHookManager.KeyUpdated) || dispatching;
    private static bool HookActive(ref bool __result) { if (active == null) return true; __result = true; return false; }
    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        if (active != this) return;
        try { foreach (var code in held.ToArray()) if (mappings.TryGetValue(code, out var mapping)) Emit(mapping, false, driver.CurrentVideoTimeUs / 1e6); }
        finally {
            active = null;
            try { SkyHookManager.Instance.requireFocus = requireFocus; }
            finally {
                try { harmony.UnpatchAll(harmony.Id); }
                finally { held.Clear(); down.Clear(); up.Clear(); }
            }
        }
    }
}
