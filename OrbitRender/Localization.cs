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
            var path = Path.Combine(GetCatalogDirectory(), culture + ".ftl");
            if (!File.Exists(path)) return catalog;

            try
            {
                foreach (var rawLine in File.ReadAllLines(path, new UTF8Encoding(false)))
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
            catch (Exception ex)
            {
                try { Main.Entry?.Logger.Error("Could not load localization catalog " + path + ": " + ex); }
                catch { }
            }

            return catalog;
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
