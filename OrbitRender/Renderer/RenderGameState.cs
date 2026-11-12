using System.Linq;
using UnityEngine;
using UnityEngine.Video;

namespace OrbitRender.Renderer
{
    internal sealed class RenderGameState
    {
        private readonly int captureRate = Time.captureFramerate, targetRate = Application.targetFrameRate, vsync = QualitySettings.vSyncCount, checkpoint = GCS.checkpointNum;
        private readonly float timeScale = Time.timeScale, volume = AudioListener.volume;
        private readonly bool auto = RDC.auto, noFail = ADOBase.controller.noFail, pauseAudio = AudioListener.pause, background = Application.runInBackground;
        private readonly bool smoothTweens = DG.Tweening.DOTween.useSmoothDeltaTime;
        private readonly bool noHud = RDC.noHud, noAutoHud = RDC.noAutoHud;
        private readonly int renderInterval = UnityEngine.Rendering.OnDemandRendering.renderFrameInterval;
        private readonly bool wasPaused = ADOBase.controller.paused, controllerEnabled = ADOBase.controller.enabled;
        private readonly SkipIntroBehavior intro = Persistence.skipIntroBehavior;
        private readonly bool strictlyEditing = ADOBase.editor != null && ADOBase.editor.inStrictlyEditingMode;
        private readonly int[] selection = ADOBase.editor != null ? ADOBase.editor.selectedFloors.Select(f => f.seqID).ToArray() : new int[0];
        private readonly CameraViewState cameraView = CameraViewState.Capture();
        private readonly AudioConfiguration audioConfiguration = AudioSettings.GetConfiguration();
        private readonly AudioSource song = ADOBase.conductor != null ? ADOBase.conductor.song : null;
        private readonly AudioSource song2 = ADOBase.conductor != null ? ADOBase.conductor.song2 : null;
        private readonly AudioSource song3 = ADOBase.conductor != null ? ADOBase.conductor.song3 : null;
        private readonly float songVolume = ADOBase.conductor != null && ADOBase.conductor.song != null
            ? ADOBase.conductor.song.volume : 0f;
        private readonly float song2Volume = ADOBase.conductor != null && ADOBase.conductor.song2 != null
            ? ADOBase.conductor.song2.volume : 0f;
        private readonly float song3Volume = ADOBase.conductor != null && ADOBase.conductor.song3 != null
            ? ADOBase.conductor.song3.volume : 0f;
        private readonly float songPitch = ADOBase.conductor != null && ADOBase.conductor.song != null
            ? ADOBase.conductor.song.pitch : 1f;
        private readonly float song2Pitch = ADOBase.conductor != null && ADOBase.conductor.song2 != null
            ? ADOBase.conductor.song2.pitch : 1f;
        private readonly float song3Pitch = ADOBase.conductor != null && ADOBase.conductor.song3 != null
            ? ADOBase.conductor.song3.pitch : 1f;
        private readonly VideoPlayer videoBG = ADOBase.customLevel != null ? ADOBase.customLevel.videoBG : null;
        private readonly VideoTimeUpdateMode videoTimeUpdateMode = ADOBase.customLevel != null && ADOBase.customLevel.videoBG != null
            ? ADOBase.customLevel.videoBG.timeUpdateMode : VideoTimeUpdateMode.DSPTime;
        public void Restore()
        {
            RestoreTiming();
            // Rendering always returns to editing, even if it was requested
            // during playback. Restoring the old unpaused flag here would
            // incorrectly turn editor.playMode back on with its conductor off.
            if (ADOBase.controller != null && ADOBase.editor == null)
            {
                ADOBase.controller.paused = wasPaused;
                ADOBase.controller.enabled = controllerEnabled;
            }
            RestoreEditorState();
        }
        public void RestoreEditorState()
        {
            var editor = ADOBase.editor;
            if (editor != null)
            {
                // SwitchToEditMode() used during cleanup puts the editor
                // into strict-editing mode. Restore the exact flag from
                // before the render; otherwise the next render starts on
                // a different editor path and level camera filters can
                // retain a stale effect state.
                editor.inStrictlyEditingMode = strictlyEditing;
                if (selection.Length > 0 && editor.floors.Count > selection.Max())
                {
                    if (selection.Length == 1) editor.SelectFloor(editor.floors[selection[0]], cameraJump: false);
                    else editor.MultiSelectFloors(editor.floors[selection.Min()], editor.floors[selection.Max()], setSelectPoint: true);
                }
                cameraView.Restore();
            }
        }
        public void RestoreTiming()
        {
            Time.captureFramerate = captureRate; Time.timeScale = timeScale;
            DG.Tweening.DOTween.useSmoothDeltaTime = smoothTweens;
            UnityEngine.Rendering.OnDemandRendering.renderFrameInterval = renderInterval;
            Application.targetFrameRate = targetRate; QualitySettings.vSyncCount = vsync;
            Application.runInBackground = background;
            var currentAudio = AudioSettings.GetConfiguration();
            if (currentAudio.sampleRate != audioConfiguration.sampleRate
                || currentAudio.dspBufferSize != audioConfiguration.dspBufferSize
                || currentAudio.numRealVoices != audioConfiguration.numRealVoices
                || currentAudio.numVirtualVoices != audioConfiguration.numVirtualVoices
                || currentAudio.speakerMode != audioConfiguration.speakerMode)
                AudioSettings.Reset(audioConfiguration);
            // AudioSettings.Reset can recreate the Unity audio device, so
            // apply the listener state after the reset rather than before
            // it. Otherwise the editor may remain paused after rendering.
            AudioListener.volume = volume; AudioListener.pause = pauseAudio;
            RDC.auto = auto; GCS.checkpointNum = checkpoint; Persistence.skipIntroBehavior = intro;
            RDC.noHud = noHud; RDC.noAutoHud = noAutoHud;
            if (ADOBase.controller != null) ADOBase.controller.noFail = noFail;
            if (videoBG != null) videoBG.timeUpdateMode = videoTimeUpdateMode;
        }
        public void RestoreAudioSources()
        {
            RestoreAudioSource(song, songVolume, songPitch);
            RestoreAudioSource(song2, song2Volume, song2Pitch);
            RestoreAudioSource(song3, song3Volume, song3Pitch);
        }
        private static void RestoreAudioSource(AudioSource source, float volume, float pitch)
        {
            if (source == null) return;
            source.Stop();
            source.volume = volume;
            source.pitch = pitch;
            if (source.clip != null) source.time = 0f;
            Main.Entry.Logger.Log(string.Format(
                "[Render] Restored audio source: clip={0}, length={1:F2}s, volume={2:F3}, pitch={3:F3}.",
                source.clip != null ? source.clip.name : "<none>",
                source.clip != null ? source.clip.length : 0.0f, volume, pitch));
        }

        private sealed class CameraViewState
        {
            private readonly scrCamera owner;
            private readonly CameraState main;
            private readonly CameraState background;
            private readonly CameraState staticBackground;
            private readonly float zoomSize;

            private CameraViewState(scrCamera owner)
            {
                this.owner = owner;
                main = CameraState.Capture(owner != null ? owner.camobj : null);
                background = CameraState.Capture(owner != null ? owner.BGcam : null);
                staticBackground = CameraState.Capture(owner != null ? owner.Bgcamstatic : null);
                zoomSize = owner != null ? owner.zoomSize : 0f;
            }

            public static CameraViewState Capture()
            {
                return new CameraViewState(scrCamera.instance);
            }

            public void Restore()
            {
                if (owner == null || scrCamera.instance != owner) return;

                // Playback moves the shared level camera all the way to the
                // final tile. Restore the editor's pre-render framing after
                // selection so the view stays on the tile the user was on.
                owner.zoomSize = zoomSize;
                main.Restore(owner.camobj);
                background.Restore(owner.BGcam);
                staticBackground.Restore(owner.Bgcamstatic);
            }

            private struct CameraState
            {
                private readonly bool valid;
                private readonly Vector3 position;
                private readonly Quaternion rotation;
                private readonly float orthographicSize;

                private CameraState(Camera camera)
                {
                    valid = camera != null;
                    position = valid ? camera.transform.position : Vector3.zero;
                    rotation = valid ? camera.transform.rotation : Quaternion.identity;
                    orthographicSize = valid && camera.orthographic ? camera.orthographicSize : 0f;
                }

                public static CameraState Capture(Camera camera)
                {
                    return new CameraState(camera);
                }

                public void Restore(Camera camera)
                {
                    if (!valid || camera == null) return;
                    camera.transform.SetPositionAndRotation(position, rotation);
                    if (camera.orthographic && orthographicSize > 0f)
                        camera.orthographicSize = orthographicSize;
                }
            }
        }
    }
}
