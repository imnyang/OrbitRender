using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace OrbitRender
{
    /// <summary>
    /// Public integration surface for overlay mods (key viewers, status HUDs).
    /// Overlay mods register a provider object from their own assembly and
    /// OrbitRender calls its methods by name, so neither mod needs a
    /// compile-time reference to the other.
    ///
    /// Provider methods (all optional):
    ///   IEnumerable&lt;Canvas&gt; GetOverlayCanvases()
    ///   void OnRenderSessionBegin(bool replay)
    ///   void OnRenderSessionEnd()
    ///   void OnBeforeOverlayCapture(bool replay)
    ///   void OnAfterOverlayCapture(bool replay)
    ///   void OnReplayClock(double videoSeconds, long ticks)
    ///   void OnReplayInput(string keyCode, bool down, long ticks)
    ///
    /// Every member must be used from Unity's main thread.
    /// </summary>
    public static class OverlayApi
    {
        /// <summary>Bumped when the provider contract changes incompatibly.</summary>
        public const int ApiVersion = 1;

        private static readonly Dictionary<string, Entry> entries =
            new Dictionary<string, Entry>(StringComparer.OrdinalIgnoreCase);
        private static readonly HashSet<Canvas> providerCanvases = new HashSet<Canvas>();

        /// <summary>True between session begin and end.</summary>
        public static bool IsRendering { get; private set; }

        /// <summary>True while OrbitRender is rendering a recorded replay bundle.</summary>
        public static bool IsReplay { get; private set; }

        public static int Width { get; private set; }
        public static int Height { get; private set; }
        public static int VideoFps { get; private set; }

        /// <summary>Replay video timeline position in seconds.</summary>
        public static double VideoSeconds { get; private set; }

        /// <summary>Replay video timeline position in Stopwatch-compatible ticks.</summary>
        public static long VideoTicks { get; private set; }

        /// <summary>
        /// Registers an overlay provider. Registering the same id again replaces
        /// the previous provider. Returns false for a missing id or provider.
        /// </summary>
        public static bool Register(string id, object provider)
        {
            if (string.IsNullOrWhiteSpace(id) || provider == null) return false;
            entries[id] = new Entry(id, provider);
            providerCanvases.Clear();
            return true;
        }

        public static void Unregister(string id)
        {
            if (string.IsNullOrEmpty(id)) return;
            if (entries.Remove(id)) providerCanvases.Clear();
        }

        public static bool IsRegistered(string id)
            => !string.IsNullOrEmpty(id) && entries.ContainsKey(id);

        /// <summary>
        /// Canvases the registered providers asked OrbitRender to composite.
        /// Rebuilds the managed-canvas cache used by the capture path.
        /// </summary>
        internal static IEnumerable<Canvas> CaptureCanvases()
        {
            providerCanvases.Clear();
            var result = new List<Canvas>();
            foreach (var entry in entries.Values)
            {
                try
                {
                    foreach (var canvas in entry.ReadCanvases())
                    {
                        if (canvas == null) continue;
                        var root = canvas.rootCanvas ?? canvas;
                        if (providerCanvases.Add(root)) result.Add(root);
                    }
                }
                catch (Exception ex) { LogError(entry.Id, "GetOverlayCanvases", ex); }
            }
            return result;
        }

        internal static bool IsProviderCanvas(Canvas canvas)
            => canvas != null && providerCanvases.Contains(canvas.rootCanvas ?? canvas);

        internal static void BeginSession(bool replay, int width, int height, int videoFps)
        {
            IsRendering = true;
            IsReplay = replay;
            Width = width;
            Height = height;
            VideoFps = videoFps;
            VideoSeconds = 0;
            VideoTicks = 0;
            // Let providers prepare before enumerating their canvases; a
            // provider may expose different elements for replay renders.
            foreach (var entry in entries.Values) entry.InvokeBool("OnRenderSessionBegin", replay);
            CaptureCanvases();
        }

        internal static void EndSession()
        {
            if (!IsRendering) return;
            IsRendering = false;
            IsReplay = false;
            VideoSeconds = 0;
            VideoTicks = 0;
            foreach (var entry in entries.Values) entry.InvokeSessionEnd();
            providerCanvases.Clear();
        }

        internal static void UpdateClock(double seconds)
        {
            if (!IsRendering || !IsReplay) return;
            if (double.IsNaN(seconds) || double.IsInfinity(seconds)) return;
            if (seconds < 0) seconds = 0;
            VideoSeconds = seconds;
            VideoTicks = checked((long)Math.Round(seconds * TimeSpan.TicksPerSecond));
            foreach (var entry in entries.Values) entry.InvokeClock(VideoSeconds, VideoTicks);
        }

        internal static void DispatchReplayInput(string key, bool down, long ticks)
        {
            if (!IsRendering || !IsReplay || string.IsNullOrEmpty(key)) return;
            foreach (var entry in entries.Values) entry.InvokeInput(key, down, ticks);
        }

        internal static void BeforeOverlayCapture(bool replay)
        {
            if (!IsRendering) return;
            foreach (var entry in entries.Values) entry.InvokeBool("OnBeforeOverlayCapture", replay);
        }

        internal static void AfterOverlayCapture(bool replay)
        {
            if (!IsRendering) return;
            foreach (var entry in entries.Values) entry.InvokeBool("OnAfterOverlayCapture", replay);
        }

        private static void LogError(string id, string member, Exception error)
        {
            try { Main.Entry?.Logger?.Error("[Overlay] " + id + "." + member + ": " + error); }
            catch (Exception) { }
        }

        private sealed class Entry
        {
            internal readonly string Id;
            private readonly object instance;
            private readonly MethodInfo canvases, begin, sessionEnd, before, after, input, clock;

            internal Entry(string id, object provider)
            {
                Id = id;
                instance = provider;
                var type = provider.GetType();
                canvases = Find(type, "GetOverlayCanvases");
                begin = Find(type, "OnRenderSessionBegin");
                sessionEnd = Find(type, "OnRenderSessionEnd");
                before = Find(type, "OnBeforeOverlayCapture");
                after = Find(type, "OnAfterOverlayCapture");
                input = Find(type, "OnReplayInput");
                clock = Find(type, "OnReplayClock");
            }

            private static MethodInfo Find(Type type, string name)
                => type.GetMethod(name, BindingFlags.Public | BindingFlags.Instance);

            internal IEnumerable<Canvas> ReadCanvases()
            {
                object value;
                try { value = canvases?.Invoke(instance, null); }
                catch (TargetInvocationException ex) { throw ex.InnerException ?? ex; }
                if (value is Canvas canvas)
                {
                    yield return canvas;
                    yield break;
                }
                if (!(value is IEnumerable list)) yield break;
                foreach (var item in list)
                    if (item is Canvas itemCanvas) yield return itemCanvas;
            }

            internal void InvokeBool(string name, bool value)
            {
                var method = name == "OnRenderSessionBegin" ? begin
                    : name == "OnBeforeOverlayCapture" ? before
                    : name == "OnAfterOverlayCapture" ? after : null;
                if (method == null) return;
                Invoke(method, name, new object[] { value });
            }

            internal void InvokeSessionEnd() => Invoke(sessionEnd, "OnRenderSessionEnd", Array.Empty<object>());

            internal void InvokeInput(string key, bool down, long ticks)
                => Invoke(input, "OnReplayInput", new object[] { key, down, ticks });

            internal void InvokeClock(double seconds, long ticks)
                => Invoke(clock, "OnReplayClock", new object[] { seconds, ticks });

            private void Invoke(MethodInfo method, string member, object[] arguments)
            {
                if (method == null) return;
                try { method.Invoke(instance, arguments); }
                catch (TargetInvocationException ex) { LogError(Id, member, ex.InnerException ?? ex); }
                catch (Exception ex) { LogError(Id, member, ex); }
            }
        }
    }
}
