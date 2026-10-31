using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace TUFReplayRenderer.Replay;

// Derive queue behavior from standard managed calls, never from mod/type names.
internal sealed class OverlayQueuePlan
{
    internal readonly HashSet<Type> Retained = new();
    internal readonly HashSet<Type> Handoff = new();
    internal readonly HashSet<MethodInfo> Consumers = new();
    internal OverlayQueuePlan(IEnumerable<MethodInfo> methods)
    {
        var calls = methods.ToDictionary(m => m, m => ManagedInstructionReader.Members(m).OfType<MethodInfo>().ToArray());
        // Peek followed by conditional expiry is retained history, not work that
        // must drain at the current video time (KPS windows, caches, etc.).
        foreach (var entry in calls)
            foreach (MethodInfo peek in entry.Value.Where(m => m.Name == "TryPeek" && QueueType(m) != null)) Retained.Add(peek.DeclaringType);
        foreach (var entry in calls)
        foreach (MethodInfo dequeue in entry.Value.Where(m => m.Name == "TryDequeue" && QueueType(m) != null && !Retained.Contains(m.DeclaringType)))
        {
            Type item = dequeue.DeclaringType.GetGenericArguments()[0];
            foreach (MethodInfo consumer in entry.Value.Where(m => m.DeclaringType.Assembly == entry.Key.DeclaringType.Assembly && !m.ContainsGenericParameters))
            {
                ParameterInfo[] parameters;
                try { parameters = consumer.GetParameters(); }
                catch (System.IO.FileNotFoundException) { continue; }
                catch (TypeLoadException) { continue; }
                // A separate finite per-item handler can be observed even when
                // its long-lived caller was already executing before rendering.
                if (parameters.Length != 1 || parameters[0].ParameterType != item
                    || (consumer.GetMethodBody()?.GetILAsByteArray()?.Length ?? 0) < 64) continue;
                Handoff.Add(dequeue.DeclaringType); Consumers.Add(consumer);
            }
        }
    }
    internal static Type QueueType(MethodBase method) => method.DeclaringType?.IsGenericType == true
        && method.DeclaringType.GetGenericTypeDefinition() == typeof(ConcurrentQueue<>) ? method.DeclaringType : null;
}
