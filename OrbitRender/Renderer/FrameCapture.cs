using System;
using System.Collections.Generic;
using OrbitRender.Patches;
using UnityEngine;
using UnityEngine.Rendering;

namespace OrbitRender.Renderer
{
    internal sealed class FrameCapture : IDisposable
    {
        private sealed class CameraState
        {
            public Camera Camera;
            public RenderTexture Target;
            public int CullingMask;
            public CameraClearFlags ClearFlags;
            public Color BackgroundColor;
            public float Depth;
            public float Aspect;
            public bool Enabled;
            public bool Orthographic;
            public float OrthographicSize;
            public Vector3 Position;
            public Quaternion Rotation;
        }
        private sealed class CanvasState
        {
            public Canvas Canvas;
            public bool Enabled;
            public bool Capture;
            public RenderMode RenderMode;
            public Camera WorldCamera;
            public float PlaneDistance;
        }
        private sealed class Pending
        {
            public FFmpegEncoder.Frame Frame;
            public AsyncGPUReadbackRequest Request;
            public bool Ready;
            public Exception Error;
            public long SubmittedAt;
            public long CompletedAt;
            public long CopyTicks;

            public void Reset(FFmpegEncoder.Frame frame, long submittedAt)
            {
                Frame = frame;
                Request = default(AsyncGPUReadbackRequest);
                Ready = false;
                Error = null;
                SubmittedAt = submittedAt;
                CompletedAt = 0;
                CopyTicks = 0;
            }

            public void Complete(AsyncGPUReadbackRequest request)
            {
                if (Ready) return;
                var copyStart = System.Diagnostics.Stopwatch.GetTimestamp();
                try
                {
                    if (request.hasError) throw new InvalidOperationException("GPU readback failed at frame " + Frame.Index);
                    var data = request.GetData<byte>();
                    if (data.Length != Frame.Bytes.Length) throw new InvalidOperationException("Unexpected GPU frame size.");
                    data.CopyTo(Frame.Bytes);
                }
                catch (Exception ex) { Error = ex; }
                finally
                {
                    CopyTicks = System.Diagnostics.Stopwatch.GetTimestamp() - copyStart;
                    CompletedAt = System.Diagnostics.Stopwatch.GetTimestamp();
                    Ready = true;
                }
            }
        }
        private readonly List<CameraState> cameras = new List<CameraState>();
        private readonly List<CanvasState> canvases = new List<CanvasState>();
        private readonly Queue<Pending> pending = new Queue<Pending>();
        private readonly Stack<Pending> reusable = new Stack<Pending>();
        private readonly FFmpegEncoder encoder;
        private readonly RenderTexture target;
        private RenderTexture customFrameHold;
        private bool customFrameHoldValid;
        private int customFrameRateRevision = -1;
        private double customFrameNextRefreshTime;
        private readonly int width;
        private readonly int height;
        private readonly scrCamera gameCamera;
        private readonly float originalZoomSize;
        private readonly Vector2 originalOffset;
        private readonly int originalPositionStateInt;
        private readonly PositionState originalPositionState;
        private readonly bool overlayActive, quadActive;
        private readonly int mainMask;
        private Camera hudOverlayCamera;
        private GameObject hudOverlayObject;
        private int hudLayer;
        private int hudMask;
        private float hudOverlayDepth;
        private readonly List<LayerState> hudLayers = new List<LayerState>();
        private readonly HashSet<GameObject> hudLayerObjects = new HashSet<GameObject>();
        private Texture2D fallback;
        private bool disposed;
        private long readbackWaitTicks;
        private long readbackCopyTicks;
        private long readbackLatencyTicks;
        private int peakPending;
        public double BackpressureSeconds { get; private set; }
        internal Texture PreviewTexture => target;
        public double ReadbackWaitSeconds => readbackWaitTicks / (double)System.Diagnostics.Stopwatch.Frequency;
        public double ReadbackCopySeconds => readbackCopyTicks / (double)System.Diagnostics.Stopwatch.Frequency;
        public double ReadbackLatencySeconds => readbackLatencyTicks / (double)System.Diagnostics.Stopwatch.Frequency;
        public int PendingReadbacks => pending.Count;
        public int PeakPendingReadbacks => peakPending;

        private sealed class LayerState
        {
            public GameObject Object;
            public int Layer;
        }

        public FrameCapture(FFmpegEncoder encoder, int width, int height, Canvas defaultTextCanvas = null,
            GameObject hitTextContainer = null)
        {
            this.encoder = encoder;
            this.width = width;
            this.height = height;
            gameCamera = scrCamera.instance;
            if (gameCamera == null || gameCamera.Bgcamstatic == null || gameCamera.BGcam == null || gameCamera.camobj == null)
                throw new InvalidOperationException("ADOFAI camera chain is not available.");
            originalZoomSize = gameCamera.zoomSize;
            originalOffset = gameCamera.offset;
            originalPositionStateInt = gameCamera.positionStateInt;
            originalPositionState = gameCamera.positionState;
            overlayActive = gameCamera.Overlaycam != null && gameCamera.Overlaycam.gameObject.activeSelf;
            quadActive = gameCamera.quad != null && gameCamera.quad.activeSelf;
            mainMask = gameCamera.camobj.cullingMask;
            target = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32) {
                name = "OrbitRender Frame", antiAliasing = 1, useMipMap = false, autoGenerateMips = false
            };
            try
            {
                if (!target.Create()) throw new InvalidOperationException("Cannot allocate render target.");
                var previous = RenderTexture.active;
                try { RenderTexture.active = target; GL.Clear(true, true, Color.black); }
                finally { RenderTexture.active = previous; }
                Add(gameCamera.Bgcamstatic); Add(gameCamera.BGcam); Add(gameCamera.camobj);
                // Overlaycam presents the already composited RT on a quad. Capturing
                // it again would feed our own output back into itself.
                if (gameCamera.Overlaycam != null) gameCamera.Overlaycam.gameObject.SetActive(false);
                if (gameCamera.quad != null) gameCamera.quad.SetActive(false);
                if (defaultTextCanvas != null)
                    CreateHudOverlay(defaultTextCanvas, hitTextContainer);
                foreach (var canvas in UnityEngine.Object.FindObjectsByType<Canvas>(FindObjectsSortMode.None))
                {
                    // Keep world-space level decorations; exclude editor/game HUD
                    // and third-party screen-space overlays from the three cameras.
                    // The selected default text is rendered by the separate
                    // overlay camera so filters cannot affect it.
                    if (!canvas.isRootCanvas || canvas.renderMode == RenderMode.WorldSpace) continue;
                    var captureCanvas = canvas == defaultTextCanvas;
                    canvases.Add(new CanvasState {
                        Canvas = canvas,
                        Enabled = canvas.enabled,
                        Capture = captureCanvas,
                        RenderMode = canvas.renderMode,
                        WorldCamera = canvas.worldCamera,
                        PlaneDistance = canvas.planeDistance
                    });
                    if (captureCanvas) ConfigureCaptureCanvas(canvas, hudOverlayCamera ?? gameCamera.camobj);
                    else canvas.enabled = false;
                }
                if (!SystemInfo.supportsAsyncGPUReadback)
                    fallback = new Texture2D(width, height, TextureFormat.RGBA32, false);
                Bind();
            }
            catch { Dispose(); throw; }
        }
        private void Add(Camera camera)
        {
            if (cameras.Exists(s => s.Camera == camera)) return;
            cameras.Add(new CameraState {
                Camera = camera,
                Target = camera.targetTexture,
                CullingMask = camera.cullingMask,
                ClearFlags = camera.clearFlags,
                BackgroundColor = camera.backgroundColor,
                Depth = camera.depth,
                Aspect = camera.aspect,
                Enabled = camera.enabled,
                Orthographic = camera.orthographic,
                OrthographicSize = camera.orthographicSize,
                Position = camera.transform.position,
                Rotation = camera.transform.rotation
            });
        }

        private void ConfigureCaptureCanvas(Canvas canvas, Camera renderCamera)
        {
            canvas.renderMode = RenderMode.ScreenSpaceCamera;
            canvas.worldCamera = renderCamera;
            canvas.planeDistance = Mathf.Max(renderCamera.nearClipPlane + 0.01f, 1f);
            renderCamera.cullingMask |= 1 << canvas.gameObject.layer;
            canvas.enabled = true;
        }

        private void CreateHudOverlay(Canvas defaultTextCanvas, GameObject hitTextContainer)
        {
            hudLayer = FindUnusedLayer();
            hudMask = 1 << hudLayer;
            AssignLayerRecursively(defaultTextCanvas.gameObject, hitTextContainer);

            // Keep selected default text out of the source that level filters
            // process. The hit-judgment container is deliberately excluded so
            // the normal gameplay cameras still apply zoom/distortion filters.
            foreach (var state in cameras)
                if (state.Camera != null) state.Camera.cullingMask &= ~hudMask;

            hudOverlayObject = new GameObject("OrbitRender HUD Overlay Camera");
            hudOverlayObject.hideFlags = HideFlags.HideAndDontSave;
            hudOverlayCamera = hudOverlayObject.AddComponent<Camera>();
            hudOverlayCamera.CopyFrom(gameCamera.camobj);
            hudOverlayCamera.clearFlags = CameraClearFlags.Depth;
            hudOverlayCamera.cullingMask = hudMask;
            hudOverlayDepth = MaxCameraDepth() + 1f;
            hudOverlayCamera.depth = hudOverlayDepth;
            hudOverlayCamera.targetTexture = target;
            hudOverlayCamera.enabled = true;
            Add(hudOverlayCamera);
            SyncHudOverlayCamera();
        }

        private float MaxCameraDepth()
        {
            var max = float.MinValue;
            foreach (var state in cameras)
                if (state.Camera != null && state.Camera != hudOverlayCamera)
                    max = Mathf.Max(max, state.Camera.depth);
            return max == float.MinValue ? 0f : max;
        }

        private static int FindUnusedLayer()
        {
            var used = new bool[32];
            foreach (var transform in UnityEngine.Object.FindObjectsByType<Transform>(FindObjectsSortMode.None))
                used[transform.gameObject.layer] = true;
            for (var layer = 31; layer >= 0; layer--)
                if (!used[layer]) return layer;
            throw new InvalidOperationException("No unused Unity layer is available for the HUD overlay.");
        }

        private void AssignLayerRecursively(GameObject root, GameObject excludedSubtree)
        {
            if (root == null) return;
            foreach (var child in root.GetComponentsInChildren<Transform>(true))
            {
                if (excludedSubtree != null
                    && (child == excludedSubtree.transform || child.IsChildOf(excludedSubtree.transform)))
                    continue;
                var gameObject = child.gameObject;
                if (!hudLayerObjects.Add(gameObject)) continue;
                hudLayers.Add(new LayerState { Object = gameObject, Layer = gameObject.layer });
                gameObject.layer = hudLayer;
            }
        }

        private void SyncHudOverlayCamera()
        {
            if (hudOverlayCamera == null || gameCamera == null || gameCamera.camobj == null) return;
            var source = gameCamera.camobj;
            hudOverlayCamera.CopyFrom(source);
            hudOverlayCamera.transform.SetPositionAndRotation(source.transform.position, source.transform.rotation);
            hudOverlayCamera.clearFlags = CameraClearFlags.Depth;
            hudOverlayCamera.cullingMask = hudMask;
            hudOverlayCamera.depth = hudOverlayDepth;
            hudOverlayCamera.targetTexture = target;
            hudOverlayCamera.enabled = true;
        }

        public void Bind()
        {
            if (gameCamera.Overlaycam != null && gameCamera.Overlaycam.gameObject.activeSelf)
                gameCamera.Overlaycam.gameObject.SetActive(false);
            if (gameCamera.quad != null && gameCamera.quad.activeSelf)
                gameCamera.quad.SetActive(false);
            foreach (var state in canvases)
            {
                if (state.Canvas == null) continue;
                if (state.Capture) ConfigureCaptureCanvas(state.Canvas, hudOverlayCamera ?? gameCamera.camobj);
                else if (state.Canvas.enabled) state.Canvas.enabled = false;
            }
            foreach (var state in cameras)
            {
                if (state.Camera == null) throw new InvalidOperationException("A render camera was destroyed.");
                // Own the camera output for the duration of the render. The old
                // path let Unity draw these cameras to the game window and then
                // called Camera.Render again into this texture, doubling the
                // scene-rendering work for every encoded frame. RendererController
                // calls Bind from its last LateUpdate, immediately before Unity's
                // normal camera pass, so that pass can be captured directly.
                if (state.Camera.targetTexture != target) state.Camera.targetTexture = target;
                var targetAspect = width / (float)height;
                if (!Mathf.Approximately(state.Camera.aspect, targetAspect)) state.Camera.aspect = targetAspect;
                // Camera.Render ignored the component's enabled flag on the old
                // manual path; keep that behavior while using the automatic pass.
                if (!state.Camera.enabled) state.Camera.enabled = true;
            }
            SyncHudOverlayCamera();
        }
        public void Capture(long index)
        {
            Drain(false);
            var source = SelectCaptureSource();
            if (!encoder.TryRent(out var buffer))
            {
                long waitStart = System.Diagnostics.Stopwatch.GetTimestamp();
                // Never yield a Unity frame under backpressure: that would advance
                // tweens/particles while the song clock and output frame stand still.
                Drain(true);
                buffer = encoder.Rent();
                BackpressureSeconds += (System.Diagnostics.Stopwatch.GetTimestamp() - waitStart)
                    / (double)System.Diagnostics.Stopwatch.Frequency;
            }
            buffer.Index = index;
            if (fallback != null)
            {
                var previous = RenderTexture.active;
                try
                {
                    RenderTexture.active = source;
                    fallback.ReadPixels(new Rect(0, 0, width, height), 0, 0, false);
                    fallback.GetRawTextureData<byte>().CopyTo(buffer.Bytes);
                }
                finally { RenderTexture.active = previous; }
                encoder.Submit(buffer);
                return;
            }
            var frame = reusable.Count > 0 ? reusable.Pop() : new Pending();
            frame.Reset(buffer, System.Diagnostics.Stopwatch.GetTimestamp());
            // Copy in the callback; Unity request data is only valid for one frame.
            frame.Request = AsyncGPUReadback.Request(source, 0, TextureFormat.RGBA32, frame.Complete);
            pending.Enqueue(frame);
            if (pending.Count > peakPending) peakPending = pending.Count;
        }
        private RenderTexture SelectCaptureSource()
        {
            if (!FrameRateEventPatch.Enabled || FrameRateEventPatch.FrameRate <= 0f)
            {
                customFrameHoldValid = false;
                customFrameRateRevision = FrameRateEventPatch.Revision;
                return target;
            }

            if (customFrameHold == null)
            {
                customFrameHold = new RenderTexture(width, height, 0, RenderTextureFormat.ARGB32) {
                    name = "OrbitRender Custom FPS Hold", antiAliasing = 1,
                    useMipMap = false, autoGenerateMips = false
                };
                if (!customFrameHold.Create())
                    throw new InvalidOperationException("Cannot allocate the custom frame-rate hold target.");
            }

            var clock = RendererController.Instance != null ? RendererController.Instance.Clock : null;
            var now = clock != null ? clock.Time : Time.timeAsDouble;
            if (customFrameRateRevision != FrameRateEventPatch.Revision)
            {
                customFrameRateRevision = FrameRateEventPatch.Revision;
                customFrameHoldValid = false;
                customFrameNextRefreshTime = now;
            }

            if (!customFrameHoldValid || now + 0.0000001 >= customFrameNextRefreshTime)
            {
                Graphics.Blit(target, customFrameHold);
                customFrameHoldValid = true;
                var interval = 1.0 / FrameRateEventPatch.FrameRate;
                customFrameNextRefreshTime = Math.Max(customFrameNextRefreshTime, now) + interval;
                while (customFrameNextRefreshTime <= now + 0.0000001)
                    customFrameNextRefreshTime += interval;
            }
            return customFrameHold;
        }
        public void Drain(bool wait)
        {
            while (pending.Count > 0)
            {
                var frame = pending.Peek();
                if (!frame.Ready && wait)
                {
                    var waitStart = System.Diagnostics.Stopwatch.GetTimestamp();
                    frame.Request.WaitForCompletion();
                    frame.Complete(frame.Request);
                    readbackWaitTicks += System.Diagnostics.Stopwatch.GetTimestamp() - waitStart;
                }
                if (!frame.Ready) break;
                if (frame.Error != null) throw new InvalidOperationException("Capture failed.", frame.Error);
                readbackCopyTicks += frame.CopyTicks;
                readbackLatencyTicks += frame.CompletedAt - frame.SubmittedAt;
                encoder.Submit(frame.Frame);
                pending.Dequeue();
                reusable.Push(frame);
            }
        }
        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            // Readbacks must release the texture before it can be destroyed, even
            // when encoder failure/cancellation means their frames are discarded.
            foreach (var frame in pending) if (!frame.Ready) frame.Request.WaitForCompletion();
            pending.Clear();
            foreach (var state in cameras) if (state.Camera != null) {
                state.Camera.targetTexture = state.Target;
                state.Camera.cullingMask = state.CullingMask;
                state.Camera.clearFlags = state.ClearFlags;
                state.Camera.backgroundColor = state.BackgroundColor;
                state.Camera.depth = state.Depth;
                state.Camera.aspect = state.Aspect;
                state.Camera.enabled = state.Enabled;
                state.Camera.orthographic = state.Orthographic;
                state.Camera.orthographicSize = state.OrthographicSize;
                state.Camera.transform.SetPositionAndRotation(state.Position, state.Rotation);
            }
            // PrepareRenderCamera changes scrCamera's source state as well as
            // the Camera components. Restore both so the editor resumes with
            // exactly the same view after rendering.
            if (gameCamera != null) {
                gameCamera.zoomSize = originalZoomSize;
                gameCamera.offset = originalOffset;
                gameCamera.positionStateInt = originalPositionStateInt;
                gameCamera.positionState = originalPositionState;
            }
            foreach (var state in canvases) if (state.Canvas != null) {
                state.Canvas.renderMode = state.RenderMode;
                state.Canvas.worldCamera = state.WorldCamera;
                state.Canvas.planeDistance = state.PlaneDistance;
                state.Canvas.enabled = state.Enabled;
            }
            for (var i = 0; i < hudLayers.Count; i++)
            {
                var state = hudLayers[i];
                if (state.Object != null) state.Object.layer = state.Layer;
            }
            if (gameCamera != null) {
                if (gameCamera.camobj != null) gameCamera.camobj.cullingMask = mainMask;
                if (gameCamera.Overlaycam != null) gameCamera.Overlaycam.gameObject.SetActive(overlayActive);
                if (gameCamera.quad != null) gameCamera.quad.SetActive(quadActive);
            }
            if (hudOverlayObject != null) UnityEngine.Object.Destroy(hudOverlayObject);
            hudLayers.Clear();
            hudLayerObjects.Clear();
            if (fallback != null) UnityEngine.Object.Destroy(fallback);
            if (customFrameHold != null) { customFrameHold.Release(); UnityEngine.Object.Destroy(customFrameHold); }
            if (target != null) { target.Release(); UnityEngine.Object.Destroy(target); }
        }
    }
}
