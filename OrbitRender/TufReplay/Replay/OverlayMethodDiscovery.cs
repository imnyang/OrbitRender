using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace TUFReplayRenderer.Replay;

// Start at actual Canvas scripts and shared input consumers. Follow their managed
// calls, delegates and state machines; a clock read elsewhere in a mod is not a root.
internal static class OverlayMethodDiscovery
{
    private const BindingFlags Declared = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;
    internal static MethodInfo[] Discover(IEnumerable<Type> components, Type inputEvent)
    {
        Type[] roots = components.Distinct().ToArray();
        var assemblies = new HashSet<Assembly>(roots.Select(t => t.Assembly));
        var pending = new Queue<MethodBase>();
        var seen = new HashSet<MethodBase>();
        foreach (Type type in roots) Seed(type);
        // Input listeners often own long-lived workers outside the Canvas hierarchy.
        // Their standard event parameter identifies the consumer without mod names.
        foreach (Assembly assembly in assemblies)
        {
            Type[] types;
            try { types = assembly.GetTypes(); }
            catch (ReflectionTypeLoadException error) { types = error.Types.Where(t => t != null).ToArray(); }
            foreach (Type type in types)
                if (Methods(type).Any(m => IsInputConsumer(m, inputEvent))) Seed(type);
        }
        while (pending.Count != 0)
        {
            MethodBase method = pending.Dequeue();
            if (method.ContainsGenericParameters || method.IsAbstract || !HasBody(method) || !seen.Add(method)) continue;
            foreach (MemberInfo member in ManagedInstructionReader.Members(method))
                if (member is MethodBase called && assemblies.Contains(called.DeclaringType.Assembly)) pending.Enqueue(called);
            foreach (StateMachineAttribute state in States(method))
                Seed(state.StateMachineType);
        }
        return seen.OfType<MethodInfo>().ToArray();

        void Seed(Type type)
        {
            foreach (MethodInfo method in Methods(type)) pending.Enqueue(method);
            foreach (ConstructorInfo constructor in type.GetConstructors(Declared)) pending.Enqueue(constructor);
        }
    }
    private static MethodInfo[] Methods(Type type)
    {
        try { return type.GetMethods(Declared); }
        catch (FileNotFoundException) { return Array.Empty<MethodInfo>(); }
        catch (TypeLoadException) { return Array.Empty<MethodInfo>(); }
    }
    private static bool IsInputConsumer(MethodInfo method, Type inputEvent)
    {
        try { return method.GetParameters().Any(p => p.ParameterType == inputEvent); }
        catch (FileNotFoundException) { return false; }
        catch (TypeLoadException) { return false; }
    }
    private static bool HasBody(MethodBase method)
    {
        try { return method.GetMethodBody() != null; }
        catch (FileNotFoundException) { return false; }
        catch (TypeLoadException) { return false; }
    }
    private static StateMachineAttribute[] States(MethodBase method)
    {
        try { return method.GetCustomAttributes(typeof(StateMachineAttribute), false).Cast<StateMachineAttribute>().ToArray(); }
        catch (FileNotFoundException) { return Array.Empty<StateMachineAttribute>(); }
        catch (TypeLoadException) { return Array.Empty<StateMachineAttribute>(); }
    }
}
