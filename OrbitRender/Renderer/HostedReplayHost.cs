using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace OrbitRender.Renderer
{
    /// <summary>
    /// The TUFReplay host's members, bound once by name and signature when a
    /// hosted replay job is created. Every later call, including the per-frame
    /// ones, goes through these delegates; nothing here uses MethodInfo.Invoke.
    /// </summary>
    /// <typeparam name="TCanvas">UnityEngine.Canvas in the mod. Tests use a stand-in.</typeparam>
    internal sealed class HostedReplayHost<TCanvas>
    {
        private const string InvalidCode = RenderErrorCodes.HostInvalid;

        private HostedReplayHost() { }

        internal object Instance { get; private set; }
        internal Action PrepareBeforeRender { get; private set; }
        internal Action<int, int, int, Func<bool>, Action<double>, Action<double>> Begin { get; private set; }
        internal Action<long, double> BeforeSimulationFrame { get; private set; }
        internal Action<long, double> AfterSimulationFrame { get; private set; }
        internal Func<long> CurrentVideoTimeUs { get; private set; }
        internal Func<bool> TerminalReached { get; private set; }
        internal Func<long?> GameplayStartVideoTimeUs { get; private set; }
        internal Func<double> GameplayRate { get; private set; }
        internal Func<IEnumerable<TCanvas>> GetCaptureCanvases { get; private set; }
        internal Action OnBeforeCapture { get; private set; }
        internal Action OnAfterCapture { get; private set; }
        internal Action<string> End { get; private set; }
        internal Action RestoreAfterRender { get; private set; }

        /// <summary>
        /// Optional (<c>RenderApi.OverlayVideoVersion</c>): the host's videos to lay over
        /// the finished video, as JSON. Null when the host has no such member.
        /// </summary>
        internal Func<string> GetOverlayVideos { get; private set; }

        /// <summary>
        /// Binds every required member or refuses the host with render_host_invalid, naming what is
        /// missing. An optional member may be absent, but one that is present must match exactly.
        /// </summary>
        internal static HostedReplayHost<TCanvas> Bind(object host)
        {
            if (host == null)
                throw new RenderRequestException(InvalidCode, "The replay host is missing. Pass the TUFReplay host object.", "host");
            var binder = new Binder(host);
            var bound = new HostedReplayHost<TCanvas>
            {
                Instance = host,
                PrepareBeforeRender = binder.Method<Action>("PrepareBeforeRender", "void PrepareBeforeRender()"),
                Begin = binder.Method<Action<int, int, int, Func<bool>, Action<double>, Action<double>>>("Begin",
                    "void Begin(int width, int height, int videoFps, Func<bool> isCancelled, Action<double> setEndTimeSeconds, Action<double> requestStopAfterSeconds)"),
                BeforeSimulationFrame = binder.Method<Action<long, double>>("BeforeSimulationFrame",
                    "void BeforeSimulationFrame(long frameIndex, double videoSeconds)"),
                AfterSimulationFrame = binder.Method<Action<long, double>>("AfterSimulationFrame",
                    "void AfterSimulationFrame(long frameIndex, double videoSeconds)"),
                CurrentVideoTimeUs = binder.Getter<Func<long>>("CurrentVideoTimeUs", "long CurrentVideoTimeUs { get; }"),
                TerminalReached = binder.Getter<Func<bool>>("TerminalReached", "bool TerminalReached { get; }"),
                GameplayStartVideoTimeUs = binder.Getter<Func<long?>>("GameplayStartVideoTimeUs",
                    "long? GameplayStartVideoTimeUs { get; }"),
                GameplayRate = binder.Getter<Func<double>>("GameplayRate", "double GameplayRate { get; }"),
                GetCaptureCanvases = binder.Method<Func<IEnumerable<TCanvas>>>("GetCaptureCanvases",
                    "IEnumerable<" + typeof(TCanvas).Name + "> GetCaptureCanvases()"),
                OnBeforeCapture = binder.Method<Action>("OnBeforeCapture", "void OnBeforeCapture()"),
                OnAfterCapture = binder.Method<Action>("OnAfterCapture", "void OnAfterCapture()"),
                End = binder.Method<Action<string>>("End", "void End(string finalState)"),
                RestoreAfterRender = binder.Method<Action>("RestoreAfterRender", "void RestoreAfterRender()"),
                GetOverlayVideos = binder.OptionalMethod<Func<string>>("GetOverlayVideos", "string GetOverlayVideos()"),
            };
            binder.ThrowIfInvalid();
            return bound;
        }

        private sealed class Binder
        {
            private const BindingFlags Members = BindingFlags.Public | BindingFlags.Instance;
            private readonly object host;
            private readonly Type type;
            private readonly List<KeyValuePair<string, string>> missing = new List<KeyValuePair<string, string>>();

            internal Binder(object host)
            {
                this.host = host;
                type = host.GetType();
            }

            /// <summary>Binds a public instance method whose parameters and return type match <typeparamref name="T"/> exactly.</summary>
            internal T Method<T>(string name, string signature) where T : Delegate
            {
                var invoke = typeof(T).GetMethod("Invoke");
                var parameters = invoke.GetParameters().Select(p => p.ParameterType).ToArray();
                // Compare types exactly: the default binder would accept an int for an enum parameter.
                var method = type.GetMethods(Members).FirstOrDefault(candidate => candidate.Name == name
                    && !candidate.ContainsGenericParameters && candidate.ReturnType == invoke.ReturnType
                    && candidate.GetParameters().Select(p => p.ParameterType).SequenceEqual(parameters));
                if (method == null)
                {
                    missing.Add(new KeyValuePair<string, string>(name, signature));
                    return null;
                }
                return Create<T>(method, name, signature);
            }

            /// <summary>
            /// Like <see cref="Method{T}"/>, but a host without any public method of that name is
            /// fine (null). A host that has one with another signature is refused, so a typo or an
            /// outdated host is named instead of silently ignored.
            /// </summary>
            internal T OptionalMethod<T>(string name, string signature) where T : Delegate
                => type.GetMethods(Members).Any(candidate => candidate.Name == name) ? Method<T>(name, signature) : null;

            /// <summary>Binds the public getter of an instance property whose type matches <typeparamref name="T"/>.</summary>
            internal T Getter<T>(string name, string signature) where T : Delegate
            {
                var returnType = typeof(T).GetMethod("Invoke").ReturnType;
                PropertyInfo property = null;
                try { property = type.GetProperty(name, Members, null, returnType, Type.EmptyTypes, null); }
                catch (AmbiguousMatchException) { }
                var getter = property?.GetGetMethod();
                if (getter == null || property.PropertyType != returnType)
                {
                    missing.Add(new KeyValuePair<string, string>(name, signature));
                    return null;
                }
                return Create<T>(getter, name, signature);
            }

            private T Create<T>(MethodInfo method, string name, string signature) where T : Delegate
            {
                var bound = Delegate.CreateDelegate(typeof(T), host, method, false) as T;
                if (bound == null) missing.Add(new KeyValuePair<string, string>(name, signature));
                return bound;
            }

            internal void ThrowIfInvalid()
            {
                if (missing.Count == 0) return;
                var list = string.Join("; ", missing.Select(member => member.Value).ToArray());
                throw new RenderRequestException(InvalidCode,
                    "The replay host " + type.FullName + " does not implement " + list
                    + ". Update TUFReplay and OrbitRender so both use RenderApi v3.",
                    missing[0].Key);
            }
        }
    }
}
