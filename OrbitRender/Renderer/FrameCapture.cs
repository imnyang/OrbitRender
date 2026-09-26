using System;
using System.Collections.Generic;
using OrbitRender.Patches;
using UnityEngine;
using UnityEngine.Rendering;

namespace OrbitRender.Renderer
{
    internal sealed class FrameCapture : IDisposable
    {
        internal static RawVideoPixelFormat SelectRawPixelFormat()
        {
            try
            {
                // RGB24 discards only the already-rendered alpha channel. The
                // encoded video has no alpha plane, so RGB values remain exact
                // while readback and pipe traffic drop from four bytes to three
                // bytes per pixel. Keep RGBA for devices that cannot expose the
                // conversion format.
                return SystemInfo.SupportsTextureFormat(TextureFormat.RGB24)
                    ? RawVideoPixelFormat.Rgb24 : RawVideoPixelFormat.Rgba32;
            }
            catch
            {
                return RawVideoPixelFormat.Rgba32;
            }
        }

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
                Frame.RepeatCount = 1;
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
        private Pending lastPending;
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
        private readonly TextureFormat readbackFormat;
        private readonly bool overlayActive, quadActive;
        private readonly int mainMask;
        private Camera hudOverlayCamera;
        private GameObject hudOverlayObject;
        private int hudLayer;
        private int hudMask;
        private float hudOverlayDepth;
        private Vector3 hudOverlayPosition;
        private readonly List<LayerState> hudLayers = new List<LayerState>();
        private readonly HashSet<GameObject> hudLayerObjects = new HashSet<GameObject>();
        private Texture2D fallback;
        private bool disposed;
        private long readbackWaitTicks;
        private long readbackCopyTicks;
        private long readbackLatencyTicks;
        private long encoderBufferWaitTicks;
        private long bindCalls;
        private long bindPropertyWrites;
        private bool measuringBind;
        private int peakPending;
        public double BackpressureSeconds { get; private set; }
        internal Texture PreviewTexture => target;
        public double EncoderBufferWaitSeconds => encoderBufferWaitTicks / (double)System.Diagnostics.Stopwatch.Frequency;
        public double ReadbackWaitSeconds => readbackWaitTicks / (double)System.Diagnostics.Stopwatch.Frequency;
        public double ReadbackCopySeconds => readbackCopyTicks / (double)System.Diagnostics.Stopwatch.Frequency;
        public double ReadbackLatencySeconds => readbackLatencyTicks / (double)System.Diagnostics.Stopwatch.Frequency;
        public int PendingReadbacks => pending.Count;
        public int PeakPendingReadbacks => peakPending;
        public long BindCalls => bindCalls;
        public long BindPropertyWrites => bindPropertyWrites;
        internal string RawFrameFormat => readbackFormat == TextureFormat.RGB24 ? "rgb24" : "rgba";

        private sealed class LayerState
        {
            public GameObject Object;
            public int Layer;
        }

        public FrameCapture(FFmpegEncoder encoder, int width, int height,
            IEnumerable<Canvas> captureCanvases = null)
        {
            this.encoder = encoder;
            this.width = width;
            this.height = height;
            readbackFormat = encoder.RawPixelFormat == RawVideoPixelFormat.Rgb24
                ? TextureFormat.RGB24 : TextureFormat.RGBA32;
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
                var captureCanvasSet = new HashSet<Canvas>();
                if (captureCanvases != null)
                    foreach (var canvas in captureCanvases)
                        if (canvas != null) captureCanvasSet.Add(canvas.rootCanvas ?? canvas);
                if (captureCanvasSet.Count > 0)
                    CreateHudOverlay(captureCanvasSet);
                foreach (var canvas in UnityEngine.Object.FindObjectsByType<Canvas>(FindObjectsSortMode.None))
                {
                    // Keep world-space level decorations; exclude editor/game HUD
                    // and third-party screen-space overlays from the three cameras.
                    // The selected HUD canvas is rendered by the separate
                    // overlay camera so filters cannot affect it.
                    if (!canvas.isRootCanvas || canvas.renderMode == RenderMode.WorldSpace) continue;
                    var captureCanvas = captureCanvasSet.Contains(canvas);
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
                    fallback = new Texture2D(width, height, readbackFormat, false);
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
            var planeDistance = Mathf.Max(renderCamera.nearClipPlane + 0.01f, 1f);
            if (canvas.renderMode != RenderMode.ScreenSpaceCamera)
            {
                canvas.renderMode = RenderMode.ScreenSpaceCamera;
                RecordBindWrite();
            }
            if (canvas.worldCamera != renderCamera)
            {
                canvas.worldCamera = renderCamera;
                RecordBindWrite();
            }
            if (!Mathf.Approximately(canvas.planeDistance, planeDistance))
            {
                canvas.planeDistance = planeDistance;
                RecordBindWrite();
            }
            var canvasLayerMask = 1 << canvas.gameObject.layer;
            if ((renderCamera.cullingMask & canvasLayerMask) == 0)
            {
                renderCamera.cullingMask |= canvasLayerMask;
                RecordBindWrite();
            }
            if (!canvas.enabled)
            {
                canvas.enabled = true;
                RecordBindWrite();
            }
        }

        private void CreateHudOverlay(IEnumerable<Canvas> captureCanvases)
        {
            hudLayer = FindUnusedLayer();
            hudMask = 1 << hudLayer;
            // Every selected root canvas belongs to the HUD. Leaving a
            // countdown canvas on a gameplay layer lets Hall of Mirrors retain
            // it through the background camera.
            foreach (var canvas in captureCanvases)
                if (canvas != null) AssignLayerRecursively(canvas.gameObject);

            // Keep every HUD graphic out of the source that level filters
            // process. The overlay camera composites it after the gameplay
            // cameras have finished rendering.
            foreach (var state in cameras)
                if (state.Camera != null) state.Camera.cullingMask &= ~hudMask;

            hudOverlayObject = new GameObject("OrbitRender HUD Overlay Camera");
            hudOverlayObject.hideFlags = HideFlags.HideAndDontSave;
            hudOverlayCamera = hudOverlayObject.AddComponent<Camera>();
            hudOverlayCamera.CopyFrom(gameCamera.camobj);
            hudOverlayPosition = gameCamera.camobj.transform.position;
            hudOverlayCamera.transform.SetPositionAndRotation(hudOverlayPosition, Quaternion.identity);
            hudOverlayCamera.aspect = width / (float)height;
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

        private void AssignLayerRecursively(GameObject root)
        {
            if (root == null) return;
            foreach (var child in root.GetComponentsInChildren<Transform>(true))
            {
                var gameObject = child.gameObject;
                if (!hudLayerObjects.Add(gameObject)) continue;
                hudLayers.Add(new LayerState { Object = gameObject, Layer = gameObject.layer });
                gameObject.layer = hudLayer;
            }
        }

        private void SyncHudOverlayCamera()
        {
            if (hudOverlayCamera == null || gameCamera == null || gameCamera.camobj == null) return;
            // Keep the HUD projection fixed. Copying the gameplay camera every
            // frame makes zoom, movement, and Hall of Mirrors state leak into
            // screen-space text even though it is on a separate layer.
            if (hudOverlayCamera.transform.position != hudOverlayPosition
                || hudOverlayCamera.transform.rotation != Quaternion.identity)
            {
                hudOverlayCamera.transform.SetPositionAndRotation(hudOverlayPosition, Quaternion.identity);
                RecordBindWrite();
            }
            var targetAspect = width / (float)height;
            if (!Mathf.Approximately(hudOverlayCamera.aspect, targetAspect))
            {
                hudOverlayCamera.aspect = targetAspect;
                RecordBindWrite();
            }
            if (hudOverlayCamera.clearFlags != CameraClearFlags.Depth)
            {
                hudOverlayCamera.clearFlags = CameraClearFlags.Depth;
                RecordBindWrite();
            }
            if (hudOverlayCamera.cullingMask != hudMask)
            {
                hudOverlayCamera.cullingMask = hudMask;
                RecordBindWrite();
            }
            if (!Mathf.Approximately(hudOverlayCamera.depth, hudOverlayDepth))
            {
                hudOverlayCamera.depth = hudOverlayDepth;
                RecordBindWrite();
            }
            if (hudOverlayCamera.targetTexture != target)
            {
                hudOverlayCamera.targetTexture = target;
                RecordBindWrite();
            }
            if (!hudOverlayCamera.enabled)
            {
                hudOverlayCamera.enabled = true;
                RecordBindWrite();
            }
        }

        public void Bind()
        {
            bindCalls++;
            measuringBind = true;
            try
            {
                if (gameCamera.Overlaycam != null && gameCamera.Overlaycam.gameObject.activeSelf)
                {
                    gameCamera.Overlaycam.gameObject.SetActive(false);
                    RecordBindWrite();
                }
                if (gameCamera.quad != null && gameCamera.quad.activeSelf)
                {
                    gameCamera.quad.SetActive(false);
                    RecordBindWrite();
                }
                foreach (var state in canvases)
                {
                    if (state.Canvas == null) continue;
                    if (state.Capture) ConfigureCaptureCanvas(state.Canvas, hudOverlayCamera ?? gameCamera.camobj);
                    else if (state.Canvas.enabled)
                    {
                        state.Canvas.enabled = false;
                        RecordBindWrite();
                    }
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
                    if (state.Camera.targetTexture != target)
                    {
                        state.Camera.targetTexture = target;
                        RecordBindWrite();
                    }
                    var targetAspect = width / (float)height;
                    if (!Mathf.Approximately(state.Camera.aspect, targetAspect))
                    {
                        state.Camera.aspect = targetAspect;
                        RecordBindWrite();
                    }
                    // Camera.Render ignored the component's enabled flag on the old
                    // manual path; keep that behavior while using the automatic pass.
                    if (!state.Camera.enabled)
                    {
                        state.Camera.enabled = true;
                        RecordBindWrite();
                    }
                }
                SyncHudOverlayCamera();
            }
            finally
            {
                measuringBind = false;
            }
        }

        private void RecordBindWrite()
        {
            if (measuringBind) bindPropertyWrites++;
        }
        public void Capture(long index)
        {
            Drain(false);
            lastPending = null;
            var source = SelectCaptureSource();
            if (!encoder.TryRent(out var buffer))
            {
                long waitStart = System.Diagnostics.Stopwatch.GetTimestamp();
                // Never yield a Unity frame under backpressure: that would advance
                // tweens/particles while the song clock and output frame stand still.
                Drain(true);
                var rentStart = System.Diagnostics.Stopwatch.GetTimestamp();
                buffer = encoder.Rent();
                encoderBufferWaitTicks += System.Diagnostics.Stopwatch.GetTimestamp() - rentStart;
                BackpressureSeconds += (System.Diagnostics.Stopwatch.GetTimestamp() - waitStart)
                    / (double)System.Diagnostics.Stopwatch.Frequency;
            }
            buffer.Index = index;
            buffer.RepeatCount = 1;
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
            frame.Request = AsyncGPUReadback.Request(source, 0, readbackFormat, frame.Complete);
            pending.Enqueue(frame);
            lastPending = frame;
            if (pending.Count > peakPending) peakPending = pending.Count;
        }
        public bool TryRepeat(long index)
        {
            var frame = lastPending?.Frame;
            if (frame == null || frame.Index + frame.RepeatCount != index) return false;
            checked { frame.RepeatCount++; }
            return true;
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
                if (lastPending == frame) lastPending = null;
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
            lastPending = null;
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
