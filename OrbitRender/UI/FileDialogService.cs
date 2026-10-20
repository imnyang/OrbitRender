using System;
using System.IO;
using UnityFileDialog;
using UnityEngine;

namespace OrbitRender.UI
{
    internal static class FileDialogService
    {
        private static readonly string[] NoFilters = new string[0];

        internal static string PickFolder(string initialDirectory)
        {
            try
            {
                return FileBrowser.PickFolder(
                    FindExistingDirectory(initialDirectory),
                    string.Empty,
                    NoFilters,
                    Localization.Get("select-render-output-folder"));
            }
            catch (Exception ex)
            {
                Main.Entry.Logger.Error("[UI] Could not open folder picker: " + ex);
                return string.Empty;
            }
        }

        internal static string PickFile(string initialDirectory, string titleKey = "select-ffmpeg-executable")
        {
            try
            {
                // Do not restrict the extension: Windows uses .exe, while
                // macOS/Linux commonly use an extensionless executable.
                return FileBrowser.PickFile(
                    FindExistingDirectory(initialDirectory),
                    string.Empty,
                    NoFilters,
                    Localization.Get(titleKey));
            }
            catch (Exception ex)
            {
                Main.Entry.Logger.Error("[UI] Could not open file picker: " + ex);
                return string.Empty;
            }
        }

        internal static string FindExistingDirectory(string path)
        {
            try
            {
                if (!string.IsNullOrEmpty(path))
                {
                    var candidate = Path.GetFullPath(path);
                    if (Directory.Exists(candidate)) return candidate;

                    var parent = Directory.GetParent(candidate);
                    while (parent != null)
                    {
                        if (Directory.Exists(parent.FullName)) return parent.FullName;
                        parent = parent.Parent;
                    }
                }
            }
            catch (Exception ex)
            {
                Main.Entry.Logger.Log("[UI] Could not resolve file picker directory: " + ex.Message);
            }
            return GetGameDirectory();
        }

        internal static string GetGameDirectory()
        {
            try
            {
                var dataPath = Path.GetFullPath(Application.dataPath);
                var dataDirectory = new DirectoryInfo(dataPath);
                if ((Application.platform == RuntimePlatform.OSXPlayer
                    || Application.platform == RuntimePlatform.OSXEditor)
                    && string.Equals(dataDirectory.Name, "Contents", StringComparison.OrdinalIgnoreCase))
                    return dataDirectory.FullName;

                return dataDirectory.Parent == null
                    ? dataDirectory.FullName
                    : dataDirectory.Parent.FullName;
            }
            catch
            {
                return Directory.GetCurrentDirectory();
            }
        }
    }
}
