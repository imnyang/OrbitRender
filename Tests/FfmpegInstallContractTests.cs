using System;
using System.IO;
using OrbitRender;

internal static class FfmpegInstallContractTests
{
    internal static void Run()
    {
        var game = Path.Combine(Path.GetTempPath(), "A Dance of Fire and Ice");
        var appContents = Path.Combine(game, "ADanceOfFireAndIce.app", "Contents");
        var windowsData = Path.Combine(game, "A Dance of Fire and Ice_Data");

        // TUFReplay resolves the folder that holds Mods; both mods must agree on it.
        Equal(Path.GetFullPath(game), FfmpegInstallContract.GetGameDirectory(appContents, true), "macOS game folder");
        Equal(Path.GetFullPath(game), FfmpegInstallContract.GetGameDirectory(windowsData, false), "Windows game folder");
        Equal(
            Path.GetFullPath(Path.Combine(game, "ffmpeg", "macos-arm64")),
            FfmpegInstallContract.GetInstallDirectory(
                FfmpegInstallContract.GetGameDirectory(appContents, true), "macos-arm64"),
            "macOS shared folder");

        Equal(
            Path.GetFullPath(Path.Combine(appContents, "ffmpeg", "macos-arm64")),
            FfmpegInstallContract.GetPreviousMacInstallDirectory(appContents, true, "macos-arm64"),
            "previous macOS folder");
        Equal(null, FfmpegInstallContract.GetPreviousMacInstallDirectory(windowsData, false, "windows-x64"),
            "previous folder on Windows");
    }

    private static void Equal(string expected, string actual, string name)
    {
        if (!string.Equals(expected, actual, StringComparison.Ordinal))
            throw new Exception(name + ": expected '" + expected + "', got '" + actual + "'.");
    }
}
