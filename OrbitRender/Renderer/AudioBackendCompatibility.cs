using System;
using System.Linq;
using HarmonyLib;
using UnityEngine;

namespace OrbitRender.Renderer
{
    internal static class AudioBackendCompatibility
    {
        internal static void EnsureCaptureSupported()
        {
            var fmod = AppDomain.CurrentDomain.GetAssemblies()
                .FirstOrDefault(assembly => assembly.GetType("ADOFAI_fmod.Main", false) != null);
            if (fmod == null) return;

            foreach (var method in Harmony.GetAllPatchedMethods().ToArray())
            {
                if (method.DeclaringType != typeof(AudioSource) && method.DeclaringType != typeof(AudioSettings)
                    && method.DeclaringType != typeof(AudioListener)) continue;
                var patches = Harmony.GetPatchInfo(method);
                if (patches == null || !patches.Prefixes.Concat(patches.Postfixes)
                    .Concat(patches.Transpilers).Concat(patches.Finalizers)
                    .Any(patch => patch.PatchMethod.Module.Assembly == fmod)) continue;

                // FMOD replaces native Unity bindings. Removing its Harmony patches
                // cannot recover those bindings or make AudioRenderer capture FMOD.
                throw new NotSupportedException("Audio capture is unavailable while ADOFAI_fmod is active. "
                    + "Disable ADOFAI_fmod and restart the game before exporting with audio, "
                    + "or turn off audio capture for a video-only export.");
            }
        }
    }
}
