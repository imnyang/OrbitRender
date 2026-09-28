using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using System.Reflection;
using UnityEngine;

namespace OrbitRender.Renderer
{
    // PlanetRenderer.Revive and SetRingColor can turn the ring back on while a
    // render is running. Keep the renderer's enabled state and the scrRing
    // color at zero so both paths are covered. This mirrors the game's own
    // ring-color API instead of relying on LineRenderer.enabled alone.
    internal sealed class PlanetRingRenderState : IDisposable
    {
        private sealed class RendererSnapshot
        {
            internal UnityEngine.Renderer Renderer;
            internal bool Enabled;
            internal LineRenderer Line;
            internal Color StartColor;
            internal Color EndColor;
            internal bool HasLineColors;
        }

        private sealed class RingComponentSnapshot
        {
            internal object Component;
            internal PropertyInfo ColorProperty;
            internal Color Color;
            internal bool HasColor;
        }

        private static readonly BindingFlags InstanceMembers =
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        private static readonly FieldInfo RingField =
            typeof(PlanetRenderer).GetField("ring", InstanceMembers);
        private static readonly FieldInfo RingComponentField =
            typeof(PlanetRenderer).GetField("ringComp", InstanceMembers);
        private static readonly PropertyInfo RingColorProperty =
            RingComponentField == null
                ? null
                : RingComponentField.FieldType.GetProperty("color", InstanceMembers);
        private static readonly FieldInfo RingLineField =
            RingComponentField == null
                ? null
                : RingComponentField.FieldType.GetField("line", InstanceMembers);

        // Apply() re-reads these members every rendered frame (LateUpdate) so
        // a game-side Revive() cannot flash a ring back into view for even a
        // single frame. FieldInfo/PropertyInfo GetValue/SetValue are
        // comparatively slow (boxing + type checks on every call); compiling
        // them once into delegates keeps the exact same behavior while
        // removing that reflection overhead from the per-frame path, which
        // matters most on levels with many planets and long renders.
        private static readonly Func<object, object> GetRing = CreateFieldGetter(RingField);
        private static readonly Func<object, object> GetRingComponent = CreateFieldGetter(RingComponentField);
        private static readonly Func<object, object> GetRingLine = CreateFieldGetter(RingLineField);
        private static readonly Func<object, object> GetRingColor = CreatePropertyGetter(RingColorProperty);
        private static readonly Action<object, object> SetRingColor = CreatePropertySetter(RingColorProperty);

        private static Func<object, object> CreateFieldGetter(FieldInfo field)
        {
            if (field == null) return null;
            var target = Expression.Parameter(typeof(object), "target");
            var instance = Expression.Convert(target, field.DeclaringType);
            var access = Expression.Field(instance, field);
            var body = Expression.Convert(access, typeof(object));
            return Expression.Lambda<Func<object, object>>(body, target).Compile();
        }

        private static Func<object, object> CreatePropertyGetter(PropertyInfo property)
        {
            if (property == null || !property.CanRead) return null;
            var target = Expression.Parameter(typeof(object), "target");
            var instance = Expression.Convert(target, property.DeclaringType);
            var access = Expression.Property(instance, property);
            var body = Expression.Convert(access, typeof(object));
            return Expression.Lambda<Func<object, object>>(body, target).Compile();
        }

        private static Action<object, object> CreatePropertySetter(PropertyInfo property)
        {
            if (property == null || !property.CanWrite) return null;
            var target = Expression.Parameter(typeof(object), "target");
            var value = Expression.Parameter(typeof(object), "value");
            var instance = Expression.Convert(target, property.DeclaringType);
            var access = Expression.Property(instance, property);
            var assign = Expression.Assign(access, Expression.Convert(value, property.PropertyType));
            return Expression.Lambda<Action<object, object>>(assign, target, value).Compile();
        }

        private readonly List<PlanetRenderer> planets = new List<PlanetRenderer>();
        private readonly HashSet<PlanetRenderer> knownPlanets = new HashSet<PlanetRenderer>();
        private readonly List<RendererSnapshot> renderers = new List<RendererSnapshot>();
        private readonly List<RingComponentSnapshot> ringComponents =
            new List<RingComponentSnapshot>();
        private readonly HashSet<UnityEngine.Renderer> seenRenderers =
            new HashSet<UnityEngine.Renderer>();
        private readonly HashSet<object> seenRingComponents = new HashSet<object>();
        private bool disposed;
        private int refreshFrame;

        private PlanetRingRenderState() { }

        internal static PlanetRingRenderState Capture()
        {
            var state = new PlanetRingRenderState();
            state.RefreshPlanets();
            state.CaptureCurrentRings();
            state.Apply();
            return state;
        }

        private void RefreshPlanets()
        {
            foreach (var planet in Resources.FindObjectsOfTypeAll<PlanetRenderer>())
            {
                if (planet == null || planet.gameObject == null || !planet.gameObject.scene.IsValid())
                    continue;
                if (knownPlanets.Add(planet)) planets.Add(planet);
            }
        }

        private void CaptureCurrentRings()
        {
            foreach (var planet in planets)
            {
                try
                {
                    if (GetRing != null)
                        AddRenderer(GetRing(planet) as UnityEngine.Renderer);

                    if (GetRingComponent == null) continue;
                    var component = GetRingComponent(planet);
                    AddRingComponent(component);
                    if (component != null && GetRingLine != null)
                        AddRenderer(GetRingLine(component) as UnityEngine.Renderer);
                }
                catch
                {
                    // A missing optional ring should not abort a render.
                }
            }
        }

        private void AddRenderer(UnityEngine.Renderer renderer)
        {
            if (renderer == null || !seenRenderers.Add(renderer)) return;

            var snapshot = new RendererSnapshot {
                Renderer = renderer,
                Enabled = renderer.enabled
            };
            var line = renderer as LineRenderer;
            if (line != null)
            {
                snapshot.Line = line;
                snapshot.StartColor = line.startColor;
                snapshot.EndColor = line.endColor;
                snapshot.HasLineColors = true;
            }
            renderers.Add(snapshot);
        }

        private void AddRingComponent(object component)
        {
            if (component == null || !seenRingComponents.Add(component)) return;

            var snapshot = new RingComponentSnapshot {
                Component = component,
                ColorProperty = RingColorProperty
            };
            if (GetRingColor != null)
            {
                try
                {
                    snapshot.Color = (Color)GetRingColor(component);
                    snapshot.HasColor = SetRingColor != null;
                }
                catch
                {
                    snapshot.HasColor = false;
                }
            }
            ringComponents.Add(snapshot);
        }

        private static Color Transparent(Color color)
        {
            color.a = 0f;
            return color;
        }

        internal void Apply()
        {
            if (disposed) return;

            // New planet instances can be created during level transitions.
            // Refresh occasionally, while avoiding a Resources scan for every
            // encoded frame once the scene is stable.
            if (planets.Count == 0 || Time.frameCount >= refreshFrame)
            {
                RefreshPlanets();
                refreshFrame = Time.frameCount + 15;
            }
            CaptureCurrentRings();

            // Set the scrRing color first; its setter updates the underlying
            // LineRenderer, then force the renderer and line colors as a final
            // guard against game-side LateUpdate/revive code.
            for (var i = 0; i < ringComponents.Count; i++)
            {
                var snapshot = ringComponents[i];
                if (!snapshot.HasColor || snapshot.Component == null) continue;
                try
                {
                    var color = (Color)GetRingColor(snapshot.Component);
                    SetRingColor(snapshot.Component, Transparent(color));
                }
                catch { /* An optional ring implementation changed or vanished. */ }
            }

            for (var i = 0; i < renderers.Count; i++)
            {
                var snapshot = renderers[i];
                var renderer = snapshot.Renderer;
                if (renderer != null && renderer.enabled) renderer.enabled = false;
                if (!snapshot.HasLineColors || snapshot.Line == null) continue;
                var start = snapshot.Line.startColor;
                start.a = 0f;
                snapshot.Line.startColor = start;
                var end = snapshot.Line.endColor;
                end.a = 0f;
                snapshot.Line.endColor = end;
            }
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;

            for (var i = 0; i < ringComponents.Count; i++)
            {
                var snapshot = ringComponents[i];
                if (!snapshot.HasColor || snapshot.Component == null) continue;
                try { SetRingColor(snapshot.Component, snapshot.Color); }
                catch { /* The component may have been destroyed with the level. */ }
            }

            for (var i = 0; i < renderers.Count; i++)
            {
                var snapshot = renderers[i];
                if (snapshot.Renderer != null) snapshot.Renderer.enabled = snapshot.Enabled;
                if (snapshot.HasLineColors && snapshot.Line != null)
                {
                    snapshot.Line.startColor = snapshot.StartColor;
                    snapshot.Line.endColor = snapshot.EndColor;
                }
            }

            renderers.Clear();
            ringComponents.Clear();
            seenRenderers.Clear();
            seenRingComponents.Clear();
            knownPlanets.Clear();
            planets.Clear();
        }
    }
}
