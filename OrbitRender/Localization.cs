using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using UnityEngine;

namespace OrbitRender
{
    internal static class Localization
    {
        private static readonly object Sync = new object();
        private static readonly Dictionary<string, Dictionary<string, string>> Catalogs =
            new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);
        private static string catalogDirectory;

        // ADOFAI stores the selected game language in RDString. Do not use
        // Application.systemLanguage here: the game language can be changed
        // independently from the operating system language.
        internal static bool IsKorean
        {
            get
            {
                try { return RDString.language == SystemLanguage.Korean; }
                catch { return false; }
            }
        }

        // Resolve the catalog directory explicitly once the mod entry exists.
        // The assembly-location fallback also makes early static initializers
        // safe before Main.Load has assigned Main.Entry.
        internal static void Initialize(string modDirectory)
        {
            lock (Sync)
            {
                catalogDirectory = string.IsNullOrEmpty(modDirectory)
                    ? ResolveDefaultCatalogDirectory()
                    : Path.Combine(modDirectory, "Localization");
                Catalogs.Clear();
            }
        }

        internal static string Get(string id)
        {
            if (string.IsNullOrEmpty(id)) return string.Empty;

            var culture = IsKorean ? "ko" : "en";
            var catalog = GetCatalog(culture);
            string value;
            if (catalog.TryGetValue(id, out value)) return value;

            // Returning the ID keeps a missing catalog entry visible instead of
            // silently displaying an empty label. English is the source locale,
            // so a missing English entry is a packaging or authoring error.
            return id;
        }

        internal static string Format(string id, params object[] args)
        {
            return string.Format(CultureInfo.InvariantCulture, Get(id), args);
        }

        internal static string FormatWithCurrentCulture(string id, params object[] args)
        {
            return string.Format(Get(id), args);
        }

        private static Dictionary<string, string> GetCatalog(string culture)
        {
            lock (Sync)
            {
                Dictionary<string, string> catalog;
                if (Catalogs.TryGetValue(culture, out catalog)) return catalog;

                catalog = LoadCatalog(culture);
                Catalogs[culture] = catalog;
                return catalog;
            }
        }

        private static Dictionary<string, string> LoadCatalog(string culture)
        {
            var catalog = new Dictionary<string, string>(StringComparer.Ordinal);
            try
            {
                var path = FindExternalCatalog(culture);
                if (!string.IsNullOrEmpty(path))
                {
                    using (var reader = new StreamReader(path, new UTF8Encoding(false), true))
                    {
                        ReadCatalog(catalog, reader);
                    }
                    return catalog;
                }

                var resource = FindEmbeddedCatalog(culture);
                if (resource == null) return catalog;
                using (resource)
                using (var reader = new StreamReader(resource, new UTF8Encoding(false), true))
                {
                    ReadCatalog(catalog, reader);
                }
            }
            catch (Exception ex)
            {
                try { Main.Entry?.Logger.Error("Could not load localization catalog for " + culture + ": " + ex); }
                catch { }
            }

            return catalog;
        }

        private static void ReadCatalog(Dictionary<string, string> catalog, TextReader reader)
        {
            string rawLine;
            while ((rawLine = reader.ReadLine()) != null)
            {
                var line = rawLine.Trim();
                if (line.Length == 0 || line.StartsWith("#", StringComparison.Ordinal)) continue;

                var separator = line.IndexOf('=');
                if (separator <= 0) continue;

                var id = line.Substring(0, separator).Trim();
                var value = line.Substring(separator + 1);
                // FTL uses the first space after '=' as a separator. Keep
                // any additional leading spaces because some UI messages
                // intentionally begin with one for string concatenation.
                if (value.StartsWith(" ", StringComparison.Ordinal))
                    value = value.Substring(1);
                catalog[id] = value;
            }
        }

        private static string FindExternalCatalog(string culture)
        {
            var directories = new List<string>();
            AddDirectoryCandidate(directories, GetCatalogDirectory());

            try
            {
                var assemblyDirectory = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
                AddDirectoryCandidate(directories, Path.Combine(assemblyDirectory ?? string.Empty, "Localization"));
                AddDirectoryCandidate(directories, assemblyDirectory);
            }
            catch { }

            foreach (var directory in directories)
            {
                var path = Path.Combine(directory, culture + ".ftl");
                if (File.Exists(path)) return path;
            }

            return null;
        }

        private static void AddDirectoryCandidate(List<string> directories, string directory)
        {
            if (string.IsNullOrEmpty(directory)) return;
            foreach (var existing in directories)
            {
                if (string.Equals(existing, directory, StringComparison.OrdinalIgnoreCase)) return;
            }
            directories.Add(directory);
        }

        private static Stream FindEmbeddedCatalog(string culture)
        {
            var assembly = Assembly.GetExecutingAssembly();
            foreach (var name in assembly.GetManifestResourceNames())
            {
                if (!name.EndsWith(".Localization." + culture + ".ftl", StringComparison.OrdinalIgnoreCase))
                    continue;
                return assembly.GetManifestResourceStream(name);
            }
            return null;
        }

        private static string GetCatalogDirectory()
        {
            if (!string.IsNullOrEmpty(catalogDirectory)) return catalogDirectory;

            lock (Sync)
            {
                if (!string.IsNullOrEmpty(catalogDirectory)) return catalogDirectory;
                catalogDirectory = ResolveDefaultCatalogDirectory();
                return catalogDirectory;
            }
        }

        private static string ResolveDefaultCatalogDirectory()
        {
            try
            {
                var location = Assembly.GetExecutingAssembly().Location;
                var directory = Path.GetDirectoryName(location);
                if (!string.IsNullOrEmpty(directory))
                    return Path.Combine(directory, "Localization");
            }
            catch { }

            return Path.Combine(Environment.CurrentDirectory, "Localization");
        }
    }
}
