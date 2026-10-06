#if BEPINEX
using System;
using System.IO;
using BepInEx;

namespace OrbitRender.Loaders
{
    [BepInPlugin("com.imnyang.orbitrender", ModContext.Id, ModContext.Version)]
    [BepInProcess("A Dance of Fire and Ice.exe")]
    public sealed class BepInExPlugin : BaseUnityPlugin
    {
        private bool loaded;
        private void Awake()
        {
            var context = new ModContext(Path.GetDirectoryName(typeof(BepInExPlugin).Assembly.Location),
                Path.Combine(Paths.ConfigPath, "OrbitRender.xml"),
                message => Logger.LogInfo(message), message => Logger.LogError(message));
            loaded = Main.Initialize(context);
            if (!loaded) throw new InvalidOperationException("OrbitRender initialization failed; see the log.");
        }
        private void OnDestroy()
        {
            if (loaded) Main.Shutdown();
            loaded = false;
        }
        private void OnApplicationQuit() => OnDestroy();
    }
}
#endif
