using System;
using System.Collections.Generic;
using System.Reflection.Emit;
using DG.Tweening.Core;
using HarmonyLib;
using OrbitRender.Renderer;

namespace OrbitRender.Patches
{
    // DOTween's independent tweens measure realtimeSinceStartup, even with
    // captureFramerate enabled. Keep the original timestamp bookkeeping, but
    // replace its elapsed interval so UI/independent effects cannot race encoding.
    internal static class TweenClockPatch
    {
        static float Delta(float actual) => RendererController.ControlsTime
            ? RendererController.DeterministicDelta : actual;
        static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions, System.Reflection.MethodBase __originalMethod)
        {
            var field = AccessTools.Field(typeof(DOTweenComponent), "_unscaledDeltaTime");
            int count = 0;
            foreach (var instruction in instructions)
            {
                if (instruction.opcode == OpCodes.Stfld && Equals(instruction.operand, field))
                {
                    var adjustment = new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(TweenClockPatch), nameof(Delta)));
                    adjustment.labels.AddRange(instruction.labels); instruction.labels.Clear();
                    yield return adjustment;
                    count++;
                }
                yield return instruction;
            }
            RenderPatchCompatibility.Report(__originalMethod, count != 0, "DOTween independent clock was not found");
        }
    }
}
