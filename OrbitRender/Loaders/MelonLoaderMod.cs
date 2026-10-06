#if MELONLOADER
using System;
using System.IO;
using MelonLoader;

[assembly: MelonInfo(typeof(OrbitRender.Loaders.MelonLoaderMod), OrbitRender.ModContext.Id, OrbitRender.ModContext.Version, OrbitRender.ModContext.Author)]
[assembly: MelonGame("7th Beat Games", "A Dance of Fire and Ice")]
// Main owns patching and cleanup; prevent MelonLoader from patching the assembly twice.
[assembly: HarmonyDontPatchAll]

namespace OrbitRender.Loaders
{
    public sealed class MelonLoaderMod : MelonMod
    {
        private bool loaded;
        public override void OnInitializeMelon()
        {
            var directory = Path.GetDirectoryName(typeof(MelonLoaderMod).Assembly.Location);
            // Keep assets/settings out of the flat Mods directory scanned by MelonLoader.
            var dataDirectory = Path.Combine(directory, "OrbitRender");
            var context = new ModContext(dataDirectory, Path.Combine(dataDirectory, "Settings.xml"),
                message => LoggerInstance.Msg(message), message => LoggerInstance.Error(message));
            loaded = Main.Initialize(context);
            if (!loaded) throw new InvalidOperationException("OrbitRender initialization failed; see the log.");
        }
        public override void OnDeinitializeMelon()
        {
            if (loaded) Main.Shutdown();
            loaded = false;
        }
    }
}
#endif
