using System;
using System.Diagnostics;
using System.IO;
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
        private bool inputClosed;
        private bool disposed;

        public string OutputPath { get; }

        public ConcurrentAudioEncoder(string executable, string output, int sampleRate, int channels,
            double gainDb)
        {
            if (sampleRate <= 0 || channels <= 0) throw new ArgumentOutOfRangeException();
            if (double.IsNaN(gainDb) || double.IsInfinity(gainDb) || gainDb < -60.0 || gainDb > 12.0)
                throw new ArgumentOutOfRangeException(nameof(gainDb));
            OutputPath = output;
            var gain = gainDb <= -60.0 ? " -af \"volume=0\""
                : Math.Abs(gainDb) > 0.000001
                    ? " -af \"volume=" + gainDb.ToString("0.########", System.Globalization.CultureInfo.InvariantCulture) + "dB\""
                    : string.Empty;
            process = new Process { StartInfo = new ProcessStartInfo {
                FileName = executable, UseShellExecute = false, CreateNoWindow = true,
                RedirectStandardInput = true, RedirectStandardError = true,
                Arguments = "-hide_banner -loglevel error -nostdin -n -f f32le -ar " + sampleRate
                    + " -ac " + channels + " -i pipe:0" + gain
                    + " -c:a aac -b:a 320k -f ipod \"" + output + "\""
            }};
            try
            {
                process.Start();
                errors = process.StandardError.ReadToEndAsync();
                input = process.StandardInput.BaseStream;
            }
            catch
            {
                try { if (!process.HasExited) process.Kill(); } catch { }
                process.Dispose();
                throw;
            }
        }

        public void Write(byte[] bytes, int count)
        {
            if (inputClosed) throw new InvalidOperationException("Audio encoder input is closed.");
            input.Write(bytes, 0, count);
        }

        public void CloseInput()
        {
            if (inputClosed) return;
            inputClosed = true;
            input.Flush();
            input.Close();
        }

        public void Finish()
        {
            CloseInput();
            if (!process.WaitForExit(30000))
            {
                process.Kill();
                throw new TimeoutException("Concurrent audio encoder did not finish.");
            }
            if (process.ExitCode != 0)
                throw new IOException("Concurrent audio encoder failed: " + errors.GetAwaiter().GetResult());
            if (!File.Exists(OutputPath) || new FileInfo(OutputPath).Length == 0)
                throw new IOException("Concurrent audio encoder did not create an audio file.");
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            try { if (!inputClosed) CloseInput(); } catch { }
            try { if (!process.HasExited) process.Kill(); } catch { }
            try { if (!process.HasExited) process.WaitForExit(5000); } catch { }
            process.Dispose();
        }
    }
}
