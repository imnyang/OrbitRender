using OrbitRender;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using HarmonyLib;
using OrbitRender.Renderer;
using UnityEngine;

namespace TUFReplayRenderer.Replay;

// Only standard API call sites in assemblies owning overlay canvases are changed.
// Host, encoder and IPC clocks stay real. No mod-specific fields/functions are used.
internal sealed class OptionalModClock : IDisposable
{
    private static OptionalModClock active;
    // Keep the interception bodies stable for cached Mono delegates and inlined
    // callers. Only their active time source changes between jobs; outside a
    // render every replacement falls through to the original standard API.
    private static readonly Harmony harmony = new("KGH1113.TUFReplayRenderer.OptionalModClock");
    private static readonly Dictionary<MemberInfo, MethodInfo> replacements = new();
    private static OverlayQueuePlan queuePlan;
    private readonly ConditionalWeakTable<Stopwatch, VirtualWatch> watches = new();
    private readonly double scaledOrigin = Time.timeAsDouble, unscaledOrigin = Time.unscaledTimeAsDouble, realtimeOrigin = Time.realtimeSinceStartupAsDouble;
    private readonly long timestampOrigin;
    private readonly DateTime localOrigin;
    private readonly OverlayVideoClock clock;
    internal static OverlayVideoClock Clock => active?.clock ?? throw new InvalidOperationException("The shared overlay clock is not initialized.");
    internal static bool Settled => active == null || active.clock.Work.IsSettled;
    internal static long WorkRevision => active?.clock.Work.Revision ?? 0;
    internal static string PendingWork => active?.clock.Work.PendingDescription ?? "";
    private static double Seconds => active?.clock.Seconds ?? 0;

    private OptionalModClock(RecordedReplayDriver driver)
    {
        if (driver == null) throw new ArgumentNullException(nameof(driver));
        // The engine retains its last clock after a render. Preparation happens
        // before ReplayHooks.Activate, so consulting that global clock here can
        // expose the previous run's end time to warmed overlay input listeners.
        // Bind this session to its own driver from the first rewritten call.
        // Pair UTC and stopwatch origins after the other host clock reads.
        // DateTime.Now can resolve local timezone data, leaving a measurable
        // gap that persistent event converters retain on the next render.
        _ = DateTime.Now;
        timestampOrigin = Stopwatch.GetTimestamp();
        DateTime utcOrigin = DateTime.UtcNow;
        localOrigin = utcOrigin.ToLocalTime();
        clock = new OverlayVideoClock(utcOrigin, () => driver.CurrentVideoTimeUs / 1e6);
    }

    internal static OptionalModClock Begin(RecordedReplayDriver driver, Action<string> warning)
    {
        if (active != null) throw new InvalidOperationException("A shared overlay clock is already active.");
        var runtime = new OptionalModClock(driver); active = runtime;
        try
        {
            runtime.Configure();
            Type[] components = OverlayAssemblyDiscovery.DiscoverComponents();
            MethodInfo[] methods = OverlayMethodDiscovery.Discover(components, typeof(SkyHook.SkyHookEvent));
            // Rebuild tiny clock accessors at their call sites. Patching only a
            // getter cannot update copies already inlined by Unity's Mono JIT.
            Dictionary<MemberInfo, MethodInfo> standardClocks;
            lock (replacements) standardClocks = replacements.Where(pair => pair.Key.DeclaringType == typeof(Time)
                || pair.Key.DeclaringType == typeof(Stopwatch) || pair.Key.DeclaringType == typeof(DateTime)
                || pair.Value is DynamicMethod).ToDictionary(pair => pair.Key, pair => pair.Value);
            // Follow bounded inline clock-conversion chains as well: a timestamp
            // normalizer can itself be inlined into the original input listener.
            for (int depth = 0; depth < 4; depth++)
            {
                bool added = false;
                foreach (MethodInfo method in methods)
                {
                    if (standardClocks.ContainsKey(method) || Harmony.GetPatchInfo(method)?.Owners.Count > 0) continue;
                    MethodInfo copy = OverlayClockAccessor.Create(method, standardClocks);
                    if (copy == null) continue;
                    lock (replacements) replacements[method] = copy;
                    standardClocks[method] = copy;
                    added = true;
                }
                if (!added) break;
            }
            queuePlan = new OverlayQueuePlan(methods);
            runtime.clock.Work.HandoffQueues.UnionWith(queuePlan.Handoff);
            foreach (var group in methods.GroupBy(m => m.DeclaringType.Assembly))
            {
                Assembly assembly = group.Key;
                int patched = 0, reused = 0;
                foreach (MethodInfo method in group)
                {
                    if (method.ContainsGenericParameters || method.IsAbstract || method.GetMethodBody() == null) continue;
                    MemberInfo[] members = ManagedInstructionReader.Members(method).ToArray();
                    bool handoff = queuePlan.Consumers.Contains(method);
                    if (!handoff && !members.Any(m => Replacement(m) != null)) continue;
                    if (Harmony.GetPatchInfo(method)?.Owners.Contains(harmony.Id) == true) { reused++; continue; }
                    bool consumer = handoff || members.Any(m => m is MethodBase mb && mb.Name == "TryDequeue" && QueueReplacement(mb) != null);
                    try { harmony.Patch(method,
                        prefix: handoff ? new HarmonyMethod(typeof(OptionalModClock), nameof(EnterHandoff)) : consumer ? new HarmonyMethod(typeof(OptionalModClock), nameof(EnterConsumer)) : null,
                        transpiler: new HarmonyMethod(typeof(OptionalModClock), nameof(Rewrite)),
                        finalizer: consumer ? new HarmonyMethod(typeof(OptionalModClock), nameof(LeaveConsumer)) : null); }
                    catch (Exception e) {
                        Main.Entry.Logger.Error("Shared overlay patch failed: " + method.DeclaringType.FullName + "." + method.Name + "\n" + e);
                        throw new InvalidOperationException("The overlay " + assembly.GetName().Name + " could not use replay input or time. Disable its overlay and try again. " + method.DeclaringType.Name + "." + method.Name + ": " + e.GetBaseException().Message, e);
                    }
                    patched++;
                }
                // Discovery details belong in developer logs, not a compatibility
                // warning that implies named adapters or verified mod support.
                if (patched + reused != 0) Main.Entry.Logger.Log("Shared overlay runtime: " + assembly.GetName().Name + ", standard API call sites in " + (patched + reused)
                    + " methods (new=" + patched + ", reused=" + reused + ")"
                    + ", inline clock copies=" + standardClocks.Keys.Count(member => member.DeclaringType?.Assembly == assembly) + "; game visuals remain unverified.");
            }
            return runtime;
        }
        catch { runtime.Dispose(); throw; }
    }
    private void Configure()
    {
        Add(typeof(Time), "time", nameof(ScaledFloat)); Add(typeof(Time), "timeAsDouble", nameof(ScaledDouble));
        Add(typeof(Time), "unscaledTime", nameof(UnscaledFloat)); Add(typeof(Time), "unscaledTimeAsDouble", nameof(UnscaledDouble));
        Add(typeof(Time), "realtimeSinceStartup", nameof(RealtimeFloat)); Add(typeof(Time), "realtimeSinceStartupAsDouble", nameof(RealtimeDouble));
        Add(typeof(Time), "deltaTime", nameof(ScaledDeltaFloat)); Add(typeof(Time), "unscaledDeltaTime", nameof(UnscaledDeltaFloat));
        Add(typeof(Time), "frameCount", nameof(FrameCount));
        Add(typeof(Screen), "width", nameof(ScreenWidth)); Add(typeof(Screen), "height", nameof(ScreenHeight));
        Add(typeof(Stopwatch), "Elapsed", nameof(WatchElapsed)); Add(typeof(Stopwatch), "ElapsedTicks", nameof(WatchTicks)); Add(typeof(Stopwatch), "ElapsedMilliseconds", nameof(WatchMilliseconds));
        foreach (var pair in new[] { ("GetTimestamp", nameof(Timestamp)), ("StartNew", nameof(NewWatch)), ("Start", nameof(StartWatch)), ("Stop", nameof(StopWatch)), ("Reset", nameof(ResetWatch)), ("Restart", nameof(RestartWatch)) })
            Register(AccessTools.Method(typeof(Stopwatch), pair.Item1, Type.EmptyTypes), typeof(OptionalModClock), pair.Item2);
        Add(typeof(DateTime), "Now", nameof(LocalNow)); Add(typeof(DateTime), "UtcNow", nameof(UtcNow));
        Register(AccessTools.Method(typeof(Input), "GetKey", new[] { typeof(KeyCode) }), typeof(SharedOverlayInput), nameof(SharedOverlayInput.Held));
        Register(AccessTools.Method(typeof(Input), "GetKeyDown", new[] { typeof(KeyCode) }), typeof(SharedOverlayInput), nameof(SharedOverlayInput.Down));
        Register(AccessTools.Method(typeof(Input), "GetKeyUp", new[] { typeof(KeyCode) }), typeof(SharedOverlayInput), nameof(SharedOverlayInput.Up));
        Register(AccessTools.PropertyGetter(typeof(Application), "isFocused"), typeof(SharedOverlayInput), nameof(SharedOverlayInput.Focused));
        Add(typeof(scrController), "gameworld", nameof(GameWorld)); Add(typeof(scrConductor), "isGameWorld", nameof(ConductorWorld));
        Add(typeof(scnEditor), "playMode", nameof(EditorPlayback));
        Register(AccessTools.Field(typeof(scrController), "gameworld"), typeof(OptionalModClock), nameof(GameWorld));
        Register(AccessTools.Field(typeof(scnEditor), "playMode"), typeof(OptionalModClock), nameof(EditorPlayback));
        Register(AccessTools.Method(typeof(RectTransformUtility), "WorldToScreenPoint", new[] { typeof(Camera), typeof(Vector3) }), typeof(OptionalModClock), nameof(WorldToScreen));
        void Add(Type type, string property, string replacement) => Register(AccessTools.PropertyGetter(type, property), typeof(OptionalModClock), replacement);
    }
    private static void Register(MemberInfo original, Type owner, string name)
    { if (original != null) { var replacement = AccessTools.Method(owner, name); lock (replacements) replacements[original] = replacement; } }
    private static MethodInfo Replacement(MemberInfo member)
    {
        lock (replacements) if (replacements.TryGetValue(member, out var replacement)) return replacement;
        return member is MethodBase method ? QueueReplacement(method) : null;
    }
    private static MethodInfo QueueReplacement(MethodBase method)
    {
        Type owner = method.DeclaringType;
        if (owner?.IsGenericType != true || owner.GetGenericTypeDefinition() != typeof(ConcurrentQueue<>)) return null;
        if (queuePlan?.Retained.Contains(owner) == true || (method.Name == "TryDequeue" && queuePlan?.Handoff.Contains(owner) == true)) return null;
        string name = method.Name == "Enqueue" ? nameof(Enqueue) : method.Name == "TryDequeue" ? nameof(Dequeue) : null;
        return name == null ? null : AccessTools.Method(typeof(OptionalModClock), name).MakeGenericMethod(owner.GetGenericArguments());
    }
    private static IEnumerable<CodeInstruction> Rewrite(IEnumerable<CodeInstruction> instructions)
    {
        foreach (var instruction in instructions)
        {
            if ((instruction.opcode == OpCodes.Call || instruction.opcode == OpCodes.Callvirt || instruction.opcode == OpCodes.Ldfld)
                && instruction.operand is MemberInfo member && Replacement(member) is MethodInfo replacement)
            { instruction.opcode = OpCodes.Call; instruction.operand = replacement; }
            yield return instruction;
        }
    }
    private static void Enqueue<T>(ConcurrentQueue<T> queue, T item) { if (active == null) queue.Enqueue(item); else active.clock.Work.Enqueue(queue, item); }
    private static bool Dequeue<T>(ConcurrentQueue<T> queue, out T item) => active == null ? queue.TryDequeue(out item) : active.clock.Work.TryDequeue(queue, out item);
    private static void EnterConsumer(out IDisposable __state) => __state = active?.clock.Work.ConsumerScope();
    private static void EnterHandoff(object __0, out IDisposable __state) => __state = active?.clock.Work.ConsumeHandoff(__0);
    private static void LeaveConsumer(IDisposable __state) => __state?.Dispose();
    private VirtualWatch State(Stopwatch watch) => watches.GetValue(watch, value => new VirtualWatch {
        Running = value.IsRunning, Started = 0,
        Elapsed = Math.Max(0, value.Elapsed.TotalSeconds - (value.IsRunning ? (Stopwatch.GetTimestamp() - timestampOrigin) / (double)Stopwatch.Frequency : 0)) });
    private static double WatchSeconds(Stopwatch watch)
    {
        if (active == null) return watch.Elapsed.TotalSeconds;
        var state = active.State(watch); lock (state) return state.Elapsed + (state.Running ? Math.Max(0, Seconds - state.Started) : 0);
    }
    private static float ScaledFloat() => active == null ? Time.time : (float)ScaledDouble();
    private static double ScaledDouble() => active == null ? Time.timeAsDouble : active.scaledOrigin + Seconds;
    private static float UnscaledFloat() => active == null ? Time.unscaledTime : (float)UnscaledDouble();
    private static double UnscaledDouble() => active == null ? Time.unscaledTimeAsDouble : active.unscaledOrigin + Seconds;
    private static float RealtimeFloat() => active == null ? Time.realtimeSinceStartup : (float)RealtimeDouble();
    private static double RealtimeDouble() => active == null ? Time.realtimeSinceStartupAsDouble : active.realtimeOrigin + Seconds;
    private static float ScaledDeltaFloat() => active == null ? Time.deltaTime : VideoDelta();
    private static float UnscaledDeltaFloat() => active == null ? Time.unscaledDeltaTime : VideoDelta();
    private static float VideoDelta() => (float)active.clock.Delta(Math.Max(1, RendererController.Instance?.Clock.Fps ?? 240), RendererController.OverlayRefreshOnly);
    private static int FrameCount() => active == null ? Time.frameCount : checked((int)(RendererController.Instance?.Clock.FrameIndex ?? 0));
    private static TimeSpan WatchElapsed(Stopwatch watch) => active == null ? watch.Elapsed : TimeSpan.FromTicks(checked((long)Math.Round(WatchSeconds(watch) * TimeSpan.TicksPerSecond)));
    private static long WatchTicks(Stopwatch watch) => active == null ? watch.ElapsedTicks : checked((long)Math.Round(WatchSeconds(watch) * Stopwatch.Frequency));
    private static long WatchMilliseconds(Stopwatch watch) => active == null ? watch.ElapsedMilliseconds : checked((long)Math.Floor(WatchSeconds(watch) * 1000));
    private static long Timestamp() => active == null ? Stopwatch.GetTimestamp() : active.timestampOrigin + checked((long)Math.Round(Seconds * Stopwatch.Frequency));
    private static DateTime LocalNow() => active == null ? DateTime.Now : active.localOrigin.AddSeconds(Seconds);
    private static DateTime UtcNow() => active == null ? DateTime.UtcNow : active.clock.UtcNow;
    private static Stopwatch NewWatch() { var watch = Stopwatch.StartNew(); if (active != null) active.watches.Add(watch, new VirtualWatch { Running = true, Started = Seconds }); return watch; }
    private static void StartWatch(Stopwatch watch) { if (active != null) { var state = active.State(watch); lock (state) if (!state.Running) { state.Running = true; state.Started = Seconds; } } watch.Start(); }
    private static void StopWatch(Stopwatch watch) { if (active != null) { var state = active.State(watch); lock (state) { state.Elapsed = WatchSeconds(watch); state.Running = false; } } watch.Stop(); }
    private static void ResetWatch(Stopwatch watch) { if (active != null) { var state = active.State(watch); lock (state) { state.Elapsed = 0; state.Running = false; } } watch.Reset(); }
    private static void RestartWatch(Stopwatch watch) { if (active != null) { var state = active.State(watch); lock (state) { state.Elapsed = 0; state.Started = Seconds; state.Running = true; } } watch.Restart(); }
    private static bool GameWorld(scrController controller) => ReplayHooks.Current != null || controller.gameworld;
    private static bool ConductorWorld(scrConductor conductor) => ReplayHooks.Current != null || conductor.isGameWorld;
    private static bool EditorPlayback(scnEditor editor) => ReplayHooks.Current != null || editor.playMode;
    private static Vector2 WorldToScreen(Camera camera, Vector3 world) => RectTransformUtility.WorldToScreenPoint(
        active == null || camera != null ? camera : RendererController.Instance?.OverlayCamera, world);
    private static int ScreenWidth() => active == null ? Screen.width : RendererController.Instance?.OverlayWidth ?? Screen.width;
    private static int ScreenHeight() => active == null ? Screen.height : RendererController.Instance?.OverlayHeight ?? Screen.height;
    private sealed class VirtualWatch { internal bool Running; internal double Started, Elapsed; }
    public void Dispose()
    {
        // Do not unpatch/repatch the same callbacks between jobs: Mono can keep
        // already compiled delegate/caller bodies. Clear only time ownership so
        // those stable callbacks resume native input/time while rendering is idle.
        if (active == this) active = null;
    }
    internal static void Shutdown()
    {
        active = null;
        harmony.UnpatchAll(harmony.Id);
        lock (replacements) replacements.Clear();
        queuePlan = null;
    }
}
