using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Reflection;

internal static class InstallerProcessTests
{
    internal static void Run(Assembly assembly)
    {
        var run = assembly.GetType("OrbitRender.FfmpegInstaller", true)
            .GetMethod("RunProcess", BindingFlags.Static | BindingFlags.NonPublic);
        var extract = assembly.GetType("OrbitRender.FfmpegInstaller", true)
            .GetMethod("ExtractZip", BindingFlags.Static | BindingFlags.NonPublic);
        var root = Path.Combine(Path.GetTempPath(), "orbit-zip-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var archive = Path.Combine(root, "test.zip");
            using (var zip = ZipFile.Open(archive, ZipArchiveMode.Create)) zip.CreateEntry("../escape");
            try
            {
                extract.Invoke(null, new object[] { archive, Path.Combine(root, "extracted") });
                throw new Exception("Unsafe archive path was accepted.");
            }
            catch (TargetInvocationException ex) when (ex.InnerException is InvalidDataException) { }
            if (File.Exists(Path.Combine(root, "escape"))) throw new Exception("Archive escaped its extraction directory.");
            if (Path.DirectorySeparatorChar == '/')
            {
                var caseArchive = Path.Combine(root, "case.zip");
                using (var zip = ZipFile.Open(caseArchive, ZipArchiveMode.Create)) zip.CreateEntry("../EXTRACTED/escape");
                try
                {
                    extract.Invoke(null, new object[] { caseArchive, Path.Combine(root, "extracted") });
                    throw new Exception("Case-distinct sibling path was accepted.");
                }
                catch (TargetInvocationException ex) when (ex.InnerException is InvalidDataException) { }
            }
        }
        finally { Directory.Delete(root, true); }
        var executable = Process.GetCurrentProcess().MainModule.FileName;
        var prefix = Type.GetType("Mono.Runtime") == null ? string.Empty
            : "\"" + typeof(InstallerProcessTests).Assembly.Location.Replace("\"", "\\\"") + "\" ";
        var output = (string)run.Invoke(null, new object[] { executable, prefix + "--process-stress", 10000 });
        if (output != "done") throw new Exception("Installer process output was not captured.");
        var elapsed = Stopwatch.StartNew();
        try
        {
            run.Invoke(null, new object[] { executable, prefix + "--process-delay", 200 });
            throw new Exception("Installer process timeout did not fire.");
        }
        catch (TargetInvocationException ex) when (ex.InnerException is TimeoutException)
        {
            if (elapsed.Elapsed.TotalSeconds > 4) throw new Exception("Installer process timeout was delayed.");
        }
    }
}
