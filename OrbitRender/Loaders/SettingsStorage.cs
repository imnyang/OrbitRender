#if !UMM
using System;
using System.IO;
using System.Xml.Serialization;

namespace OrbitRender
{
    internal static class SettingsStorage
    {
        internal static RendererSettings Load(string path, ModLog log)
        {
            if (!File.Exists(path)) return new RendererSettings();
            try
            {
                using (var stream = File.OpenRead(path))
                    return (RendererSettings)new XmlSerializer(typeof(RendererSettings)).Deserialize(stream);
            }
            catch (Exception ex)
            {
                log.Error("Could not read renderer settings; using defaults: " + ex);
                return new RendererSettings();
            }
        }

        internal static void Save(string path, RendererSettings settings)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            var temporary = path + ".tmp";
            try
            {
                using (var stream = File.Create(temporary))
                    new XmlSerializer(typeof(RendererSettings)).Serialize(stream, settings);
                if (File.Exists(path)) File.Replace(temporary, path, null);
                else File.Move(temporary, path);
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }
    }
}
#endif
