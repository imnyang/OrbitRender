using System;
using System.Collections;
using System.IO;
using UnityEngine;
using UnityEngine.Networking;
using OrbitRender.Renderer;

namespace OrbitRender.UI
{
    // Preview the editor song without changing the conductor's playback state.
    internal static class AudioPreview
    {
        private static GameObject previewObject;
        private static AudioSource previewSource;
        private static AudioPreviewGainFilter gainFilter;
        private static UnityWebRequest songRequest;
        private static Coroutine loadRoutine;
        private static float gainDb;

        internal static bool IsActive => previewObject != null
            && (IsLoading || (previewSource != null && previewSource.isPlaying));
        internal static bool IsLoading => loadRoutine != null;
        internal static string ErrorMessage { get; private set; }

        internal static bool Toggle(float requestedGainDb)
        {
            if (IsActive)
            {
                Stop();
                return false;
            }

            Stop();
            gainDb = RendererSettings.ClampAudioGainDb(requestedGainDb);
            var conductor = ADOBase.conductor;
            var template = conductor != null ? conductor.song : null;
            if (template != null && template.clip != null && template.clip.length > 0f)
            {
                previewObject = new GameObject("OrbitRender Audio Preview");
                UnityEngine.Object.DontDestroyOnLoad(previewObject);
                Play(template.clip, template);
                return IsActive;
            }

            var editor = ADOBase.editor;
            var level = editor != null ? editor.customLevel : null;
            var filename = level != null && level.levelData != null
                ? level.levelData.songFilename : null;
            if (string.IsNullOrEmpty(filename) || RendererController.Instance == null)
            {
                ErrorMessage = Localization.Get("audio-preview-unavailable");
                return false;
            }

            var directory = Path.GetDirectoryName(level.levelPath);
            var songPath = string.IsNullOrEmpty(directory) ? filename : Path.Combine(directory, filename);
            if (!File.Exists(songPath) || !TryGetAudioType(songPath, out var audioType))
            {
                ErrorMessage = Localization.Get("audio-preview-unavailable");
                return false;
            }

            previewObject = new GameObject("OrbitRender Audio Preview");
            UnityEngine.Object.DontDestroyOnLoad(previewObject);
            loadRoutine = RendererController.Instance.StartCoroutine(Load(songPath, audioType));
            return true;
        }

        private static IEnumerator Load(string songPath, AudioType audioType)
        {
            songRequest = UnityWebRequestMultimedia.GetAudioClip(new Uri(songPath).AbsoluteUri, audioType);
            var handler = songRequest.downloadHandler as DownloadHandlerAudioClip;
            if (handler != null) handler.streamAudio = false;
            yield return songRequest.SendWebRequest();
            loadRoutine = null;
            if (songRequest.result != UnityWebRequest.Result.Success)
            {
                Main.Entry.Logger.Log("Audio preview could not load song: " + songRequest.error);
                ErrorMessage = Localization.Get("audio-preview-load-failed");
                StopPlayback();
                yield break;
            }

            var clip = DownloadHandlerAudioClip.GetContent(songRequest);
            if (clip == null || clip.length <= 0f)
            {
                ErrorMessage = Localization.Get("audio-preview-load-failed");
                StopPlayback();
                yield break;
            }
            var conductor = ADOBase.conductor;
            Play(clip, conductor != null ? conductor.song : null);
        }

        private static void Play(AudioClip clip, AudioSource template)
        {
            try
            {
                previewSource = previewObject.AddComponent<AudioSource>();
                previewSource.playOnAwake = false;
                previewSource.clip = clip;
                // The editor can leave the stopped song source at zero volume.
                previewSource.volume = template != null && template.volume > 0f ? template.volume : 1f;
                previewSource.pitch = template != null && template.pitch > 0f ? template.pitch : 1f;
                previewSource.ignoreListenerPause = true;
                gainFilter = previewObject.AddComponent<AudioPreviewGainFilter>();
                SetGain(gainDb);
                previewSource.Play();
                if (previewSource.isPlaying) return;
            }
            catch (Exception ex)
            {
                Main.Entry.Logger.Log("Audio preview playback failed: " + ex);
            }
            ErrorMessage = Localization.Get("audio-preview-load-failed");
            if (previewObject != null)
                StopPlayback();
        }

        internal static void SetGain(float requestedGainDb)
        {
            gainDb = RendererSettings.ClampAudioGainDb(requestedGainDb);
            if (gainFilter == null) return;
            gainFilter.LinearGain = gainDb <= RendererSettings.MinAudioGainDb
                ? 0f : Mathf.Pow(10f, gainDb / 20f);
        }

        internal static void Stop()
        {
            if (loadRoutine != null && RendererController.Instance != null)
                RendererController.Instance.StopCoroutine(loadRoutine);
            loadRoutine = null;
            StopPlayback();
            ErrorMessage = null;
        }

        private static void StopPlayback()
        {
            if (previewSource != null)
            {
                previewSource.Stop();
                previewSource.clip = null;
            }
            if (previewObject != null) UnityEngine.Object.Destroy(previewObject);
            previewSource = null;
            gainFilter = null;
            previewObject = null;
            if (songRequest != null) songRequest.Dispose();
            songRequest = null;
        }

        private static bool TryGetAudioType(string path, out AudioType audioType)
        {
            switch (Path.GetExtension(path).ToLowerInvariant())
            {
                case ".ogg": audioType = AudioType.OGGVORBIS; return true;
                case ".wav": audioType = AudioType.WAV; return true;
                case ".aif":
                case ".aiff": audioType = AudioType.AIFF; return true;
                case ".mp3": audioType = AudioType.MPEG; return true;
                default: audioType = default(AudioType); return false;
            }
        }
    }
}
