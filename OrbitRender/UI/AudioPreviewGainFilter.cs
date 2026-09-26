using UnityEngine;

namespace OrbitRender.UI
{
    // AudioSource.volume is limited to 1.0, so preview gain above the source's
    // current volume has to be applied to the PCM samples in the audio chain.
    internal sealed class AudioPreviewGainFilter : MonoBehaviour
    {
        internal float LinearGain = 1f;

        private void OnAudioFilterRead(float[] data, int channels)
        {
            if (data == null || data.Length == 0) return;
            var gain = LinearGain;
            if (Mathf.Abs(gain - 1f) < 0.000001f) return;
            for (var i = 0; i < data.Length; i++) data[i] *= gain;
        }
    }
}
