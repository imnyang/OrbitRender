using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using HarmonyLib;

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
            {
                var error = new InvalidOperationException("OrbitRender cannot safely render with the current game/patch combination. "
                    + string.Join("; ", failures.Values.OrderBy(value => value)));
                var details = new StringBuilder();
                var candidateIds = new HashSet<string>(StringComparer.Ordinal);
                details.AppendLine("Timing compatibility failed before frame capture. Encoding settings are not the cause of this check.");
                details.AppendLine("Patch presence alone does not prove a conflict. Compare with optional mods disabled after a full restart.");
                foreach (var failure in failures.OrderBy(pair => pair.Value))
                {
                    details.AppendLine("Requirement: " + failure.Value);
                    try
                    {
                        var method = failure.Key;
                        details.AppendLine("Target: " + method + "; assembly " + method.Module.Assembly.FullName
                            + "; MVID " + method.Module.ModuleVersionId + "; token 0x" + method.MetadataToken.ToString("X8"));
                        var body = method.GetMethodBody()?.GetILAsByteArray();
                        if (body != null)
                        {
                            using (var hash = System.Security.Cryptography.SHA256.Create())
                                details.AppendLine("Original IL: " + body.Length + " bytes; SHA256 "
                                    + BitConverter.ToString(hash.ComputeHash(body)).Replace("-", ""));
                        }
                        var info = Harmony.GetPatchInfo(method);
                        if (info == null) { details.AppendLine("Harmony patches: none"); continue; }
                        foreach (var patch in info.Transpilers.Where(p => p.owner != Main.Entry?.Info.Id + ".RenderTiming"))
                            foreach (var mod in UnityModManagerNet.UnityModManager.modEntries)
                                if (mod.Info.Id != Main.Entry?.Info.Id && (mod.Info.Id == patch.owner
                                    || string.Equals(System.IO.Path.GetFileNameWithoutExtension(mod.Info.AssemblyName),
                                        patch.PatchMethod?.Module.Assembly.GetName().Name, StringComparison.OrdinalIgnoreCase)))
                                    candidateIds.Add(mod.Info.Id);
                        AppendPatches(details, "Prefix", info.Prefixes);
                        AppendPatches(details, "Transpiler", info.Transpilers);
                        AppendPatches(details, "Postfix", info.Postfixes);
                        AppendPatches(details, "Finalizer", info.Finalizers);
                    }
                    catch (Exception ex) { details.AppendLine("Patch inspection unavailable: " + ex.Message); }
                }
                // Apply removes patches before the controller sees this exception.
                error.Data["OrbitRender.TimingCompatibility"] = details.ToString();
                error.Data["OrbitRender.ConflictModIds"] = candidateIds.OrderBy(id => id).ToArray();
                throw error;
            }
        }

        private static void AppendPatches(StringBuilder report, string kind, IEnumerable<Patch> patches)
        {
            foreach (var patch in patches)
                report.AppendLine(kind + ": owner=" + patch.owner + "; method="
                    + patch.PatchMethod?.DeclaringType?.FullName + "." + patch.PatchMethod?.Name
                    + "; assembly=" + patch.PatchMethod?.Module.Assembly.FullName
                    + "; priority=" + patch.priority + "; before=" + string.Join(",", patch.before ?? new string[0])
                    + "; after=" + string.Join(",", patch.after ?? new string[0]));
        }
    }
}
