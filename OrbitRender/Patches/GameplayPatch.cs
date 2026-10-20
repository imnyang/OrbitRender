using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using OrbitRender.Renderer;
using UnityEngine;

namespace OrbitRender.Patches
{
    [HarmonyPatch]
    internal static class HideJudgmentsPatch
    {
        static IEnumerable<MethodBase> TargetMethods()
        {
            // r150 added an optional nullable judgment identifier. Keep the
            // legacy target as a fallback for pre-r150 game assemblies.
            var current = AccessTools.Method(typeof(scrHitTextManager), nameof(scrHitTextManager.ShowHitText),
                new[] { typeof(HitMargin), typeof(scrPlanet), typeof(float), typeof(int?) });
            if (current != null) yield return current;

            var legacy = AccessTools.Method(typeof(scrHitTextManager), nameof(scrHitTextManager.ShowHitText),
                new[] { typeof(HitMargin), typeof(scrPlanet), typeof(float) });
            if (legacy != null) yield return legacy;
        }

        static bool Prefix() => !RendererController.ControlsTime || RendererController.ShowHitJudgments;
    }

    [HarmonyPatch(typeof(scnLevelSelect), "CheckAudioBreak")]
    internal static class AudioDevicePatch
    {
        static bool Prefix() => !RendererController.ControlsTime;
    }
    // Select the game's synchronous autoplay path, never synthesize input ticks.
    [HarmonyPatch(typeof(AsyncInputManager), "get_isActive")]
    internal static class SynchronousGameplayPatch
    {
        static bool Prefix(ref bool __result)
        {
            if (!RendererController.InputBlocked) return true;
            __result = false;
            return false;
        }
    }

    [HarmonyPatch(typeof(scrController), "UpdateInput")]
    internal static class GameplayInputPatch
    {
        static bool Prefix() => !RendererController.InputBlocked;
    }

    // The editor and a few menu components read these directly instead of
    // going through scrController.UpdateInput. Return an empty input state so
    // keyboard, controller, and back/menu actions cannot modify the level
    // while the render clock owns the frame.
    [HarmonyPatch(typeof(RDInput), "GetMain")]
    internal static class RendererMainInputPatch
    {
        static bool Prefix(ref int __result)
        {
            if (!RendererController.InputBlocked) return true;
            __result = 0;
            return false;
        }
    }

    // The editor polls this property directly to zoom its camera with the
    // mouse wheel. That camera also feeds the render target while exporting.
    [HarmonyPatch(typeof(RDInput), "get_mouseScrollDelta")]
    internal static class RendererMouseWheelPatch
    {
        static bool Prefix(ref Vector2 __result)
        {
            if (!RendererController.InputBlocked) return true;
            __result = Vector2.zero;
            return false;
        }
    }

    // Newer game builds changed GetMainPressKeys/GetMainHeldKeys from
    // `List<AnyKeyCode> Get...()` to `void Get...(List<AnyKeyCode> keys)`.
    // Patch the two shapes separately so either signature loads.
    [HarmonyPatch]
    internal static class RendererMainKeyListPatch
    {
        static IEnumerable<MethodBase> TargetMethods()
            => FindKeyListMethods(method => method.ReturnType == typeof(void));

        static bool Prefix(object[] __args)
        {
            if (!RendererController.InputBlocked) return true;
            foreach (var argument in __args)
                if (argument is List<AnyKeyCode> keys)
                    keys.Clear();
            return false;
        }

        internal static IEnumerable<MethodBase> FindKeyListMethods(Func<MethodInfo, bool> predicate)
        {
            foreach (var method in typeof(RDInput).GetMethods(
                         BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static))
            {
                if (method.Name != "GetMainPressKeys" && method.Name != "GetMainHeldKeys") continue;
                if (!predicate(method)) continue;
                if (method.GetParameters().Any(parameter => parameter.ParameterType == typeof(List<AnyKeyCode>)))
                    yield return method;
            }
        }
    }

    [HarmonyPatch]
    internal static class RendererMainKeyListResultPatch
    {
        static IEnumerable<MethodBase> TargetMethods()
            => RendererMainKeyListPatch.FindKeyListMethods(
                method => method.ReturnType == typeof(List<AnyKeyCode>));

        static bool Prefix(ref List<AnyKeyCode> __result)
        {
            if (!RendererController.InputBlocked) return true;
            __result = new List<AnyKeyCode>();
            return false;
        }
    }

    [HarmonyPatch]
    internal static class RendererBackInputPatch
    {
        static IEnumerable<MethodBase> TargetMethods()
        {
            yield return AccessTools.PropertyGetter(typeof(RDInput), "backPress");
            yield return AccessTools.PropertyGetter(typeof(RDInput), "backIsPressed");
            // scrController.Update reads cancelPress directly for Escape's
            // quit/back action, independently of UpdateInput and backPress.
            yield return AccessTools.PropertyGetter(typeof(RDInput), "cancelPress");
        }

        static bool Prefix(ref bool __result)
        {
            if (!RendererController.InputBlocked) return true;
            __result = false;
            return false;
        }
    }

    // AsyncInput is used by editor shortcuts and some UI objects without
    // passing through RDInput. Block all query overloads during rendering.
    [HarmonyPatch]
    internal static class RendererAsyncInputPatch
    {
        static IEnumerable<MethodBase> TargetMethods()
        {
            foreach (var method in typeof(AsyncInput).GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static))
                if (method.Name == "GetKey" || method.Name == "GetKeyDown" || method.Name == "GetKeyUp")
                    yield return method;
        }

        static bool Prefix(ref bool __result)
        {
            if (!RendererController.InputBlocked) return true;
            __result = false;
            return false;
        }
    }

    [HarmonyPatch]
    internal static class RendererInputDevicePatch
    {
        static IEnumerable<MethodBase> TargetMethods()
        {
            foreach (var type in new[] {
                typeof(RDInputType_Keyboard), typeof(RDInputType_AsyncKeyboard),
                typeof(RDInputType_Joystick), typeof(RDInputType_Mouse) })
            {
                foreach (var method in type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static))
                    if (method.ReturnType == typeof(bool) && (method.Name == "CheckKeyState" || method.Name == "Back"))
                        yield return method;
            }
        }

        static bool Prefix(ref bool __result)
        {
            if (!RendererController.InputBlocked) return true;
            __result = false;
            return false;
        }
    }

    [HarmonyPatch(typeof(scrController), "ProcessKeyInputs")]
    internal static class ProcessKeyInputPatch
    {
        static bool Prefix() => !RendererController.InputBlocked;
    }

    [HarmonyPatch(typeof(scrController), "DebugUpdate")]
    internal static class DebugInputPatch
    {
        static bool Prefix() => !RendererController.InputBlocked;
    }

    [HarmonyPatch(typeof(scnEditor), "HandleKeyboardActions")]
    internal static class EditorKeyboardInputPatch
    {
        static bool Prefix() => !RendererController.InputBlocked;
    }

    // Update also checks Unity input directly to leave play mode. Keep the
    // rest of Update running: it advances the scene being rendered.
    [HarmonyPatch(typeof(scnEditor), "Update")]
    internal static class EditorEscapeInputPatch
    {
        static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            var original = AccessTools.Method(typeof(Input), nameof(Input.GetKeyDown), new[] { typeof(KeyCode) });
            var guarded = AccessTools.Method(typeof(EditorEscapeInputPatch), nameof(GetKeyDown));
            foreach (var instruction in instructions)
            {
                if (instruction.Calls(original))
                {
                    instruction.opcode = OpCodes.Call;
                    instruction.operand = guarded;
                }
                yield return instruction;
            }
        }

        private static bool GetKeyDown(KeyCode key)
            => !(key == KeyCode.Escape && RendererController.InputBlocked) && Input.GetKeyDown(key);
    }

    [HarmonyPatch(typeof(scnEditor), "TryQuitToMenu")]
    internal static class EditorQuitInputPatch
    {
        static bool Prefix() => !RendererController.InputBlocked;
    }

    [HarmonyPatch(typeof(RDEditorUtils), "CheckForKeyCombo")]
    internal static class EditorKeyComboPatch
    {
        static bool Prefix(ref bool __result)
        {
            if (!RendererController.InputBlocked) return true;
            __result = false;
            return false;
        }
    }

    [HarmonyPatch(typeof(scrTempEscToQuit), "Update")]
    internal static class TemporaryEscapeInputPatch
    {
        static bool Prefix() => !RendererController.InputBlocked;
    }

    [HarmonyPatch(typeof(scrTempEscToQuit), "LateUpdate")]
    internal static class TemporaryEscapeLateInputPatch
    {
        static bool Prefix() => !RendererController.InputBlocked;
    }

    [HarmonyPatch(typeof(scrPlayerManager), "AnyValidInputWasTriggered")]
    internal static class StartAndExitInputPatch
    {
        static bool Prefix(ref bool __result)
        {
            if (!RendererController.InputBlocked) return true;
            __result = false;
            return false;
        }
    }

    [HarmonyPatch(typeof(scrController), "TogglePauseGame")]
    internal static class RenderPausePatch
    {
        static bool Prefix(scrController __instance, ref bool __result)
        {
            if (!OrbitRender.UI.CompletionScreen.BlocksInput
                && (RendererController.Instance == null || RendererController.Instance.State != RenderState.Rendering)) return true;
            __result = __instance.paused;
            return false;
        }
    }

}
