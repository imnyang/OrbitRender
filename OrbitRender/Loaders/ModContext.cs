using System;

namespace OrbitRender
{
    // Runtime services shared by all loader entry points.
    internal sealed class ModContext
    {
        internal const string Id = "OrbitRender";
        internal const string Version = "2.1.0";
        internal const string Author = "imnyang";
        internal string Path { get; }
        internal string SettingsPath { get; }
        internal ModLog Logger { get; }
#if UMM
        internal UnityModManagerNet.UnityModManager.ModEntry UmmEntry { get; }
        internal ModContext(UnityModManagerNet.UnityModManager.ModEntry entry)
            : this(entry.Path, null, entry.Logger.Log, entry.Logger.Error)
        {
            UmmEntry = entry;
        }
#endif
        internal ModContext(string path, string settingsPath, Action<string> log, Action<string> error)
        {
            Path = path;
            SettingsPath = settingsPath;
            Logger = new ModLog(log, error);
        }
    }

    internal sealed class ModLog
    {
        private readonly Action<string> log;
        private readonly Action<string> error;
        internal ModLog(Action<string> log, Action<string> error) { this.log = log; this.error = error; }
        internal void Log(string message) => log(message);
        internal void Error(string message) => error(message);
    }
}
