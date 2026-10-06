using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

namespace OrbitRender.Renderer
{
    internal sealed class NativeNvencEncoder : IDisposable
    {
        [StructLayout(LayoutKind.Sequential)]
        private struct Config
        {
            public uint Size, Abi, Width, Height, Fps, BitrateMbps, Preset, Reserved;
            public IntPtr Texture, OutputUtf8;
        }
        [StructLayout(LayoutKind.Sequential)]
        private struct Stats
        {
            public ulong Submitted, Copied, Encoded;
            public uint Pending, PeakPending;
            public ulong EncodeNanoseconds;
        }
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate uint AbiFn();
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int CreateFn(ref Config config, out ulong handle, [Out] byte[] error, UIntPtr capacity);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int QueueFn(ulong handle, ulong index, out int eventId, [Out] byte[] error, UIntPtr capacity);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int StatsFn(ulong handle, out Stats stats, [Out] byte[] error, UIntPtr capacity);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int FinishFn(ulong handle, ulong expected, [Out] byte[] error, UIntPtr capacity);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate void DestroyFn(ulong handle);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate IntPtr EventFn();
        private sealed class Api
        {
            public CreateFn Create;
            public QueueFn Queue;
            public StatsFn Stats;
            public FinishFn Finish;
            public DestroyFn Destroy;
            public IntPtr RenderEvent;
            public Api(string path)
            {
                var module = LoadLibraryEx(path, IntPtr.Zero, 0x100 | 0x1000);
                if (module == IntPtr.Zero) throw new InvalidOperationException("Cannot load orbitrender_rs.dll: Windows error " + Marshal.GetLastWin32Error());
                // Keep the DLL loaded for the process lifetime: queued Unity
                // callbacks and a timed-out driver worker can outlive disposal.
                if (Export<AbiFn>(module, "orbit_abi_version")() != 1) throw new InvalidOperationException("Unsupported orbitrender_rs ABI.");
                Create = Export<CreateFn>(module, "orbit_create");
                Queue = Export<QueueFn>(module, "orbit_queue");
                Stats = Export<StatsFn>(module, "orbit_stats");
                Finish = Export<FinishFn>(module, "orbit_finish");
                Destroy = Export<DestroyFn>(module, "orbit_destroy");
                RenderEvent = Export<EventFn>(module, "orbit_render_event")();
                if (RenderEvent == IntPtr.Zero) throw new InvalidOperationException("Missing native render callback.");
            }
            private static T Export<T>(IntPtr module, string name) where T : class
            {
                var pointer = GetProcAddress(module, name);
                if (pointer == IntPtr.Zero) throw new EntryPointNotFoundException(name);
                return Marshal.GetDelegateForFunctionPointer(pointer, typeof(T)) as T;
            }
        }
        [DllImport("kernel32.dll", EntryPoint = "LoadLibraryExW", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr LoadLibraryEx(string path, IntPtr file, uint flags);
        [DllImport("kernel32.dll", CharSet = CharSet.Ansi, ExactSpelling = true)]
        private static extern IntPtr GetProcAddress(IntPtr module, string name);
        private static readonly object apiLock = new object();
        private static Api loadedApi;
        private readonly Api api;
        private readonly byte[] error = new byte[4096];
        private readonly Action<IntPtr, int> issueEvent;
        private readonly Action flush;
        private readonly string elementaryPath;
        private ulong handle;
        private Stats stats;
        private bool finished;
        public long RepeatedFrames { get; private set; }
        public double BufferWaitSeconds { get; private set; }
        public double RenderEventWaitSeconds { get; private set; }
        public long WrittenFrames { get { Refresh(); return checked((long)stats.Encoded); } }
        public double WriteSeconds { get { Refresh(); return stats.EncodeNanoseconds / 1000000000.0; } }
        public int QueueDepth { get { Refresh(); return checked((int)stats.Pending); } }
        public int PeakQueueDepth { get { Refresh(); return checked((int)stats.PeakPending); } }

        internal static string LibraryPath => Path.Combine(Path.GetDirectoryName(typeof(NativeNvencEncoder).Assembly.Location), "Native", "windows-x64", "orbitrender_rs.dll");
        internal static bool IsDisabled => string.Equals(Environment.GetEnvironmentVariable("ORBITRENDER_DISABLE_NATIVE"), "1", StringComparison.Ordinal);
        internal static bool IsInstalled => Environment.OSVersion.Platform == PlatformID.Win32NT && IntPtr.Size == 8 && File.Exists(LibraryPath);

        private NativeNvencEncoder(Api api, IntPtr texture, string path, int width, int height, int fps, int bitrateMbps, string preset,
            Action<IntPtr, int> issueEvent, Action flush)
        {
            this.api = api;
            this.issueEvent = issueEvent;
            this.flush = flush;
            elementaryPath = path;
            var bytes = Encoding.UTF8.GetBytes(path + "\0");
            var output = Marshal.AllocHGlobal(bytes.Length);
            try
            {
                Marshal.Copy(bytes, 0, output, bytes.Length);
                var config = new Config { Size = (uint)Marshal.SizeOf(typeof(Config)), Abi = 1, Width = (uint)width, Height = (uint)height,
                    Fps = (uint)fps, BitrateMbps = (uint)bitrateMbps, Preset = preset == "ultrafast" ? 1u : preset == "fast" ? 6u : 4u,
                    Texture = texture, OutputUtf8 = output };
                Check(api.Create(ref config, out handle, error, (UIntPtr)error.Length));
            }
            finally { Marshal.FreeHGlobal(output); }
        }

        public static NativeNvencEncoder TryCreate(IntPtr texture, string path, int width, int height, int fps, int bitrateMbps, string preset,
            Action<IntPtr, int> issueEvent, Action flush, out string failure)
        {
            failure = null;
            if (IsDisabled || !IsInstalled) return null;
            try
            {
                Api api;
                lock (apiLock) { api = loadedApi ?? (loadedApi = new Api(LibraryPath)); }
                return new NativeNvencEncoder(api, texture, path, width, height, fps, bitrateMbps, preset, issueEvent, flush);
            }
            catch (Exception ex) { failure = ex.Message; return null; }
        }

        private void Check(int status)
        {
            if (status == 0) return;
            var end = Array.IndexOf(error, (byte)0);
            throw new IOException(end > 0 ? Encoding.UTF8.GetString(error, 0, end) : "Native video encoder failed (" + status + ").");
        }
        private void Refresh()
        {
            if (handle != 0) Check(api.Stats(handle, out stats, error, (UIntPtr)error.Length));
        }
        public void Submit(long index, bool repeated)
        {
            if (handle == 0 || finished) throw new ObjectDisposedException(nameof(NativeNvencEncoder));
            var wait = Stopwatch.StartNew();
            int eventId;
            while (true)
            {
                var status = api.Queue(handle, checked((ulong)index), out eventId, error, (UIntPtr)error.Length);
                if (status == 0) break;
                if (status != 1) Check(status);
                if (wait.Elapsed.TotalSeconds > 30) throw new TimeoutException("Native encoder stopped consuming GPU textures.");
                Thread.Sleep(1);
            }
            BufferWaitSeconds += wait.Elapsed.TotalSeconds;
            issueEvent(api.RenderEvent, eventId);
            flush();
            // Unity's command stream orders this copy callback between this
            // frame's Blit and the next one; the source need not be CPU-fenced.
            if (repeated) RepeatedFrames++;
        }

        private void WaitForCopies(long expectedFrames)
        {
            flush();
            var wait = Stopwatch.StartNew();
            while (true)
            {
                Refresh();
                if (stats.Copied == checked((ulong)expectedFrames)) break;
                if (wait.Elapsed.TotalSeconds > 30) throw new TimeoutException("Unity did not execute the native render callback.");
                Thread.Sleep(1);
            }
            RenderEventWaitSeconds += wait.Elapsed.TotalSeconds;
        }

        public void Finish(long expectedFrames, string executable, string output, int fps)
        {
            WaitForCopies(expectedFrames);
            Check(api.Finish(handle, checked((ulong)expectedFrames), error, (UIntPtr)error.Length));
            finished = true;
            Refresh();
            using (var process = new Process { StartInfo = new ProcessStartInfo {
                FileName = executable, UseShellExecute = false, CreateNoWindow = true, RedirectStandardError = true,
                Arguments = "-hide_banner -loglevel error -nostdin -n -fflags +genpts -r " + fps + " -f h264 -i \"" + elementaryPath
                    + "\" -map 0:v:0 -c:v copy -an" + FFmpegEncoder.ContainerOptions(output) + " \"" + output + "\""
            } })
            {
                process.Start();
                var stderr = process.StandardError.ReadToEndAsync();
                if (!process.WaitForExit(30000)) { try { process.Kill(); } catch { } throw new TimeoutException("Native video remux did not finish."); }
                process.WaitForExit();
                if (process.ExitCode != 0) throw new IOException("Native video remux failed: " + stderr.GetAwaiter().GetResult());
            }
            File.Delete(elementaryPath);
        }
        public void Dispose()
        {
            if (handle == 0) return;
            var owned = handle;
            handle = 0;
            api.Destroy(owned);
            try { if (File.Exists(elementaryPath)) File.Delete(elementaryPath); } catch { }
        }
    }
}
