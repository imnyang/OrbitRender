using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace OrbitRender.Renderer
{
    // Encodes the captured PCM while video frames are still being generated.
    // The WAV remains the authoritative fallback if this optional process fails.
    internal sealed class ConcurrentAudioEncoder : IDisposable
    {
        private readonly Process process;
        private readonly Task<string> errors;
        private readonly Stream input;
        private const int BlockBytes = 64 * 1024;
        private readonly BlockingCollection<byte[]> pending = new BlockingCollection<byte[]>(32);
        private readonly Task writer;
        private bool inputClosed;
        private bool disposed;

        public string OutputPath { get; }

        public ConcurrentAudioEncoder(string executable, string output, int sampleRate, int channels,
            double gainDb, AudioCodec audioCodec = AudioCodec.AAC)
        {
            if (sampleRate <= 0 || channels <= 0) throw new ArgumentOutOfRangeException();
            if (double.IsNaN(gainDb) || double.IsInfinity(gainDb) || gainDb < -60.0 || gainDb > 12.0)
                throw new ArgumentOutOfRangeException(nameof(gainDb));
            OutputPath = output;
            if (audioCodec != AudioCodec.AAC && audioCodec != AudioCodec.Opus && audioCodec != AudioCodec.Flac)
                throw new ArgumentOutOfRangeException(nameof(audioCodec));
            var gain = gainDb <= -60.0 ? " -af \"volume=0\""
                : Math.Abs(gainDb) > 0.000001
                    ? " -af \"volume=" + gainDb.ToString("0.########", System.Globalization.CultureInfo.InvariantCulture) + "dB\""
                    : string.Empty;
            process = new Process { StartInfo = new ProcessStartInfo {
                FileName = executable, UseShellExecute = false, CreateNoWindow = true,
                RedirectStandardInput = true, RedirectStandardError = true,
                Arguments = "-hide_banner -loglevel error -nostdin -n -f f32le -ar " + sampleRate
                    + " -ac " + channels + " -i pipe:0" + gain
                    + AudioCodecCatalog.EncodingArguments(audioCodec)
                    + (audioCodec == AudioCodec.Opus ? " -f opus"
                        : audioCodec == AudioCodec.Flac ? " -f flac" : " -f ipod") + " \"" + output + "\""
            }};
            try
            {
                process.Start();
                errors = process.StandardError.ReadToEndAsync();
                input = process.StandardInput.BaseStream;
                writer = Task.Factory.StartNew(WritePending, CancellationToken.None,
                    TaskCreationOptions.LongRunning, TaskScheduler.Default);
            }
            catch
            {
                try { if (!process.HasExited) process.Kill(); } catch { }
                process.Dispose();
                pending.Dispose();
                throw;
            }
        }

        public void Write(byte[] bytes, int count)
        {
            if (disposed) throw new ObjectDisposedException(nameof(ConcurrentAudioEncoder));
            if (inputClosed) throw new InvalidOperationException("Audio encoder input is closed.");
            if (bytes == null) throw new ArgumentNullException(nameof(bytes));
            if (count < 0 || count > bytes.Length) throw new ArgumentOutOfRangeException(nameof(count));
            if (writer.IsFaulted) writer.GetAwaiter().GetResult();
            for (int offset = 0; offset < count;)
            {
                // Capture reuses its buffer immediately; the writer owns this copy.
                int length = Math.Min(BlockBytes, count - offset);
                var block = new byte[length];
                Buffer.BlockCopy(bytes, offset, block, 0, length);
                if (!pending.TryAdd(block))
                    throw new IOException("Concurrent audio encoder fell behind; use the WAV instead.");
                offset += length;
            }
        }

        private void WritePending()
        {
            try
            {
                foreach (var block in pending.GetConsumingEnumerable())
                    input.Write(block, 0, block.Length);
            }
            finally { input.Close(); }
        }

        public void CloseInput()
        {
            if (inputClosed) return;
            inputClosed = true;
            pending.CompleteAdding();
        }

        public void Finish()
        {
            CloseInput();
            var timeout = Stopwatch.StartNew();
            if (!writer.Wait(30000))
                throw new TimeoutException("Concurrent audio encoder did not drain its input.");
            writer.GetAwaiter().GetResult();
            int remaining = Math.Max(0, 30000 - (int)timeout.ElapsedMilliseconds);
            if (!process.WaitForExit(remaining))
            {
                process.Kill();
                throw new TimeoutException("Concurrent audio encoder did not finish.");
            }
            remaining = Math.Max(0, 30000 - (int)timeout.ElapsedMilliseconds);
            if (!errors.Wait(remaining))
                throw new TimeoutException("Concurrent audio encoder did not close its error stream.");
            if (process.ExitCode != 0)
                throw new IOException("Concurrent audio encoder failed: " + errors.GetAwaiter().GetResult());
            if (!File.Exists(OutputPath) || new FileInfo(OutputPath).Length == 0)
                throw new IOException("Concurrent audio encoder did not create an audio file.");
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            CloseInput();
            // Killing first releases a blocked pipe writer, including on cancellation.
            try { if (!process.HasExited) process.Kill(); } catch { }
            try { process.WaitForExit(5000); } catch { }
            writer.ContinueWith(completed => {
                // Observe write failures even when capture already selected the WAV fallback.
                var failure = completed.Exception;
                pending.Dispose();
                process.Dispose();
            }, TaskScheduler.Default);
        }
    }
}
