using System;
using System.Globalization;

namespace OrbitRender
{
    /// <summary>Parsing and formatting for "v2.8.3"-style release tags and Info.json versions.</summary>
    internal static class UpdateVersion
    {
        internal static bool TryParse(string value, out Version version)
        {
            version = null;
            if (string.IsNullOrEmpty(value)) return false;
            var text = value.Trim();
            if (text.StartsWith("v", StringComparison.OrdinalIgnoreCase)) text = text.Substring(1);

            // Pre-release / build metadata ("2.9.0-beta.1") are not auto-installable;
            // callers filter pre-releases through the GitHub flag, so only the
            // numeric core is compared here.
            var suffix = text.IndexOfAny(new[] { '-', '+' });
            if (suffix >= 0) text = text.Substring(0, suffix);

            var parts = text.Split('.');
            if (parts.Length < 2 || parts.Length > 4) return false;
            var numbers = new int[4];
            for (var i = 0; i < parts.Length; i++)
            {
                if (!int.TryParse(parts[i], NumberStyles.None, CultureInfo.InvariantCulture, out numbers[i]))
                    return false;
            }
            version = new Version(numbers[0], numbers[1], numbers[2], numbers[3]);
            return true;
        }

        internal static string Format(Version version)
        {
            return version.Revision > 0
                ? version.Major + "." + version.Minor + "." + version.Build + "." + version.Revision
                : version.Major + "." + version.Minor + "." + version.Build;
        }
    }

    /// <summary>The release asset chosen for an update.</summary>
    internal sealed class UpdateRelease
    {
        internal string Tag;
        internal Version Version;
        internal string AssetName;
        internal string DownloadUrl;
        internal long Size;
        /// <summary>Lower-case hex SHA-256 from GitHub's asset digest, or null.</summary>
        internal string Sha256;
    }
}
