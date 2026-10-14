using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace OrbitRender.Patches
{
    internal static class RenderPatchCompatibility
    {
        private static readonly Dictionary<MethodBase, string> failures = new Dictionary<MethodBase, string>();

        internal static void Report(MethodBase method, bool supported, string requirement)
        {
            // Harmony rebuilds the chain when another mod patches the method.
            // Update the result on every rebuild, including recovery after unpatching.
            if (supported) failures.Remove(method);
            else failures[method] = method.DeclaringType.FullName + "." + method.Name + ": " + requirement;
        }

        internal static void Validate()
        {
            if (failures.Count != 0)
                throw new InvalidOperationException("OrbitRender cannot safely render with the current game/patch combination. "
                    + string.Join("; ", failures.Values.OrderBy(value => value)));
        }
    }
}
