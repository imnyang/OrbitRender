using System;
using System.IO;
using System.Threading;
using AdofaiIpc.Contracts;
using AdofaiIpc.Loader;
using UnityEngine;

internal static class Program
{
    private static void Main(string[] args)
    {
        string mods = Path.GetFullPath(args[0]);
        SynchronizationContext.SetSynchronizationContext(new UnitySynchronizationContext());
        IIpcRuntime runtime = IpcLoader.GetRuntime(mods,
            new RuntimeRequirements("2.1.0", 1, 3, "commands", "downloads", "typed-files"));
        if (runtime.Info.Version != "2.1.0") throw new Exception("Unexpected runtime version.");
        if (!ReferenceEquals(runtime, IpcLoader.GetRuntime(mods))) throw new Exception("Runtime is not shared.");
        Console.WriteLine("PASS: bundled SDK runtime selected and started by IpcLoader");
        Application.Quit();
    }
}
