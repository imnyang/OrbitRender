using System;
using System.Collections.Generic;
using System.Reflection.Emit;
using HarmonyLib;
using OrbitRender.Renderer;

namespace OrbitRender.Patches
{
    internal static class ParticleSeedPatch
    {
        internal static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            var getInt = AccessTools.Method(typeof(ADOFAI.LevelEvent), "GetInt", new[] { typeof(string) });
            var resolveSeed = AccessTools.Method(typeof(ParticleSeedPatch), nameof(ResolveSeed));
            bool readsSeed = false;
            bool patched = false;
            foreach (var instruction in instructions)
            {
                yield return instruction;
                if (readsSeed && instruction.Calls(getInt))
                {
                    yield return new CodeInstruction(OpCodes.Call, resolveSeed);
                    patched = true;
                }
                readsSeed = instruction.opcode == OpCodes.Ldstr && Equals(instruction.operand, "randomSeed");
            }

            if (!patched)
                throw new InvalidOperationException("The game’s particle seed could not be located.");
        }

        private static int ResolveSeed(int seed)
        {
            var renderSeed = RendererController.Instance?.RandomSeedForRun;
            if (seed != 0 || !renderSeed.HasValue) return seed;
            // The game uses zero as a sentinel for Unity's automatic particle seed.
            return renderSeed.Value == 0 ? 1 : renderSeed.Value;
        }
    }
}
