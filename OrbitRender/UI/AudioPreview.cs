using UnityEngine;

namespace OrbitRender.UI
{
    // Plays the currently loaded song through a temporary source so changing
    // the render gain does not disturb the editor's conductor or song source.
    internal static class AudioPreview
    {
        private static GameObject previewObject;
        private static AudioSource previewSource;

        internal static bool IsPlaying => previewSource != null && previewSource.isPlaying;

        internal static bool Toggle(float gainDb)
        {
            if (IsPlaying)
            {
                Stop();
                return false;
            }

            Stop();
            var conductor = ADOBase.conductor;
            var template = conductor != null ? conductor.song : null;
            if (template == null || template.clip == null) return false;

            previewObject = new GameObject("OrbitRender Audio Preview");
            Object.DontDestroyOnLoad(previewObject);
            previewSource = previewObject.AddComponent<AudioSource>();
            previewSource.clip = template.clip;
            previewSource.volume = template.volume;
            var gainFilter = previewObject.AddComponent<AudioPreviewGainFilter>();
            gainFilter.LinearGain = Mathf.Pow(10f, gainDb / 20f);
            previewSource.pitch = template.pitch;
            previewSource.loop = false;
            previewSource.playOnAwake = false;
            previewSource.Play();
            return true;
        }

        internal static void Stop()
        {
            if (previewSource != null) previewSource.Stop();
            if (previewObject != null) Object.Destroy(previewObject);
            previewSource = null;
            previewObject = null;
        }
    }
}
