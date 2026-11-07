using System;
using System.IO;
using System.Text;
using Unity.Collections;
using UnityEngine;

namespace OrbitRender.Renderer
{
    internal sealed class GameAudioCapture : IDisposable
    {
        // AudioListener.GetOutputData is documented to require a power-of-two
        // buffer. Unity 6 macOS players can return an empty history for large
        // requests, so keep this at one 60 fps frame plus headroom.
        private const int ListenerHistorySamples = 1024;
        private FileStream stream;
        private ConcurrentAudioEncoder concurrentEncoder;
        private NativeArray<float> samples;
        private float[] managed;
        private byte[] bytes;
        private float[] listenerLeft;
        private float[] listenerRight;
        private AudioFilterCapture filter;
        private bool started;
        private bool useFilterFallback;
        private bool useListenerOutputFallback;
        private bool listenerOutputAvailable;
        private long filterSamplesToDiscard;
        private int zeroSampleReads;
        private long captureTicks;
        private long concurrentWriteTicks;
        private long concurrentFinishTicks;
        private string captureMode = "not-started";
        public int SampleRate { get; private set; }
        public int Channels { get; private set; }
        public long SampleFrames { get; private set; }
        public float Peak { get; private set; }
        public bool NeedsRealtimePacing => useListenerOutputFallback;
        internal string CaptureMode => captureMode;
        // Late sound effects must be scheduled after the captured DSP boundary.
        public double RenderedUntilDsp(double dspOrigin)
        {
            if (SampleRate <= 0) return dspOrigin;
            return dspOrigin + SampleFrames / (double)SampleRate;
        }
        public double CaptureSeconds => System.Threading.Interlocked.Read(ref captureTicks)
            / (double)System.Diagnostics.Stopwatch.Frequency;
        public double ConcurrentWriteSeconds => concurrentWriteTicks / (double)System.Diagnostics.Stopwatch.Frequency;
        public double ConcurrentFinishSeconds => concurrentFinishTicks / (double)System.Diagnostics.Stopwatch.Frequency;

        public void BeginConcurrentEncoding(string executable, string output, double gainDb, AudioCodec audioCodec = AudioCodec.AAC)
        {
            try { concurrentEncoder = new ConcurrentAudioEncoder(executable, output, SampleRate, Channels, gainDb, audioCodec); }
            catch (Exception ex)
            {
                Main.Entry.Logger.Log("[Audio] Concurrent audio encoding unavailable; using final mux: " + ex.Message);
            }
        }

        private void WriteConcurrent(byte[] data, int count)
        {
            if (concurrentEncoder == null) return;
            var start = System.Diagnostics.Stopwatch.GetTimestamp();
            try { concurrentEncoder.Write(data, count); }
            catch (Exception ex)
            {
                Main.Entry.Logger.Log("[Audio] Concurrent audio encoding stopped; using final mux: " + ex.Message);
                concurrentEncoder.Dispose();
                concurrentEncoder = null;
            }
            finally { concurrentWriteTicks += System.Diagnostics.Stopwatch.GetTimestamp() - start; }
        }

        public bool FinishConcurrentEncoding()
        {
            var encoder = concurrentEncoder;
            concurrentEncoder = null;
            if (encoder == null) return false;
            var start = System.Diagnostics.Stopwatch.GetTimestamp();
            try { encoder.Finish(); return true; }
            catch (Exception ex)
            {
                Main.Entry.Logger.Log("[Audio] Concurrent audio encoding failed; using final mux: " + ex.Message);
                return false;
            }
            finally
            {
                var elapsed = System.Diagnostics.Stopwatch.GetTimestamp() - start;
                concurrentFinishTicks += elapsed;
                var diagnostics = RenderPerformanceDiagnostics.Current;
                if (diagnostics != null) diagnostics.RecordTicks(RenderPerformanceStage.AudioDrain, elapsed);
                encoder.Dispose();
            }
        }

        public void Begin(string path)
        {
            SampleRate = AudioSettings.outputSampleRate;
            switch (AudioSettings.speakerMode)
            {
                case AudioSpeakerMode.Mono: Channels = 1; break;
                case AudioSpeakerMode.Stereo: case AudioSpeakerMode.Prologic: Channels = 2; break;
                case AudioSpeakerMode.Quad: Channels = 4; break;
                case AudioSpeakerMode.Surround: Channels = 5; break;
                case AudioSpeakerMode.Mode5point1: Channels = 6; break;
                case AudioSpeakerMode.Mode7point1: Channels = 8; break;
                default: throw new InvalidOperationException("Unsupported audio speaker layout.");
            }
            if (SampleRate <= 0) throw new InvalidOperationException("Audio device has no sample rate.");
            try
            {
                stream = new FileStream(path, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.Read);
                WriteHeader(0);
                filter = AudioFilterCapture.Attach();
                started = AudioRenderer.Start();
                if (!started)
                {
                    Main.Entry.Logger.Log("[Audio] Unity AudioRenderer could not start; trying the listener output fallback.");
                }
                filter?.StartCapture(SampleRate, Channels);
                InitializeListenerOutputCapture();
                useFilterFallback = false;
                // Live listener polling is only safe when AudioRenderer never started.
                useListenerOutputFallback = !started && listenerOutputAvailable;
                captureMode = started ? "AudioRenderer" : useListenerOutputFallback
                    ? "AudioListener.GetOutputData (AudioRenderer.Start returned false)"
                    : "unavailable (AudioRenderer.Start returned false; listener output unavailable)";
                Main.Entry.Logger.Log(filter == null
                    ? "[Audio] AudioListener filter unavailable on the mixed listener."
                    : "[Audio] AudioListener filter fallback attached and waiting for mixer samples.");
            }
            catch { Dispose(); throw; }
        }

        public void CaptureFrame(long videoFrameIndex, int fps)
        {
            var diagnostics = RenderPerformanceDiagnostics.Current;
            var captureStart = System.Diagnostics.Stopwatch.GetTimestamp();
            var previousCaptureFramerate = Time.captureFramerate;
            try
            {
                // The game clock uses Target FPS, but AudioRenderer's capture
                // block size must follow the output timeline. Temporarily
                // expose Video FPS only while querying/rendering this audio
                // block, then restore Target FPS before the next game update.
                if (previousCaptureFramerate != fps) Time.captureFramerate = fps;
                long expectedSamples = checked((videoFrameIndex + 1) * (long)SampleRate / fps);
                if (useListenerOutputFallback)
                {
                    CaptureListenerOutput(expectedSamples - SampleFrames);
                    return;
                }
                if (useFilterFallback)
                {
                    CaptureFilterSamples(expectedSamples - SampleFrames);
                    return;
                }

                if (!started) return;
                var diagnosticStart = diagnostics != null ? RenderPerformanceDiagnostics.Timestamp() : 0;
                int count = AudioRenderer.GetSampleCountForCaptureFrame();
                if (diagnostics != null) diagnostics.Record(RenderPerformanceStage.AudioQuery, diagnosticStart);
                // A zero read may be temporary; a later block can include those samples.
                if (count == 0)
                {
                    zeroSampleReads++;
                    if (diagnostics != null) diagnostics.Increment(RenderPerformanceCounter.AudioZeroReads);
                    if (TryUseFilterFallback()) CaptureFilterSamples(expectedSamples - SampleFrames);
                    return;
                }
                if (count < 0) throw new InvalidOperationException("Unity AudioRenderer returned an invalid sample count: " + count + ".");
                // Discard the captured prefix when switching to the listener filter.
                if (zeroSampleReads > 0 && TryUseFilterFallback())
                {
                    CaptureFilterSamples(expectedSamples - SampleFrames);
                    return;
                }
                int length = checked(count * Channels);
                if (!samples.IsCreated || samples.Length != length)
                {
                    if (samples.IsCreated) samples.Dispose();
                    samples = new NativeArray<float>(length, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
                    if (diagnostics != null) diagnostics.Increment(RenderPerformanceCounter.AudioBufferAllocations);
                    EnsureManagedBuffers(length);
                }
                diagnosticStart = diagnostics != null ? RenderPerformanceDiagnostics.Timestamp() : 0;
                bool rendered = AudioRenderer.Render(samples);
                if (diagnostics != null) diagnostics.Record(RenderPerformanceStage.AudioRender, diagnosticStart);
                if (!rendered)
                {
                    zeroSampleReads++;
                    if (diagnostics != null) diagnostics.Increment(RenderPerformanceCounter.AudioRenderFailures);
                    if (TryUseFilterFallback()) CaptureFilterSamples(expectedSamples - SampleFrames);
                    return;
                }
                diagnosticStart = diagnostics != null ? RenderPerformanceDiagnostics.Timestamp() : 0;
                NativeArray<float>.Copy(samples, managed, length);
                if (diagnostics != null) diagnostics.Record(RenderPerformanceStage.AudioCopy, diagnosticStart);
                WriteManagedSamples(count * Channels);
            }
            finally
            {
                if (Time.captureFramerate != previousCaptureFramerate)
                    Time.captureFramerate = previousCaptureFramerate;
                var elapsed = System.Diagnostics.Stopwatch.GetTimestamp() - captureStart;
                System.Threading.Interlocked.Add(ref captureTicks, elapsed);
                if (diagnostics != null) diagnostics.RecordTicks(RenderPerformanceStage.AudioCapture, elapsed);
            }
        }

        public void Complete(long videoFrames, int fps)
        {
            long targetSamples = checked(videoFrames * (long)SampleRate / fps);
            long dataLength = checked(targetSamples * Channels * sizeof(float));
            ValidateWavDataSize(dataLength);
            if (useListenerOutputFallback)
            {
                CaptureListenerOutput(targetSamples - SampleFrames);
                Main.Entry.Logger.Log("[Audio] Game audio capture used AudioListener.GetOutputData fallback. Peak: "
                    + Peak.ToString("F6") + ".");
            }
            if (useFilterFallback) CaptureFilterSamples(targetSamples - SampleFrames);
            if (useFilterFallback)
                Main.Entry.Logger.Log("[Audio] Game audio capture used the AudioListener filter fallback. Dropped interleaved samples: "
                    + (filter != null ? filter.DroppedSamples.ToString() : "0") + ".");
            else if (filter != null && zeroSampleReads > 0)
                Main.Entry.Logger.Log("[Audio] AudioListener filter fallback received no samples; unavailable Unity AudioRenderer reads will be padded with silence.");
            // Unity's audio mixer can round the final block. Correct only that
            // boundary; reject drift instead of silently stretching the song.
            int tolerance = Math.Max(AudioSettings.GetConfiguration().dspBufferSize * 2, SampleRate / fps * 2);
            // Missing frames are recoverable: AudioRenderer can return zero
            // until its first positive capture window, and the final shortfall
            // is padded below. Only reject audio that ran ahead materially.
            if (SampleFrames > targetSamples + tolerance)
                throw new InvalidOperationException("Audio drift: captured " + SampleFrames + " sample frames, expected " + targetSamples + ".");
            if (SampleFrames < targetSamples)
            {
                long missing = targetSamples - SampleFrames;
                AppendSilence(missing);
                Main.Entry.Logger.Log("[Audio] Unity AudioRenderer audio was short by " + missing
                    + " sample frames (unavailable reads: " + zeroSampleReads
                    + "); padded the WAV with silence.");
            }
            else if (SampleFrames > targetSamples && concurrentEncoder != null)
            {
                // WAV can trim a rounded final block; a streamed encoder input
                // cannot. Use the WAV mux for this uncommon boundary.
                concurrentEncoder.Dispose();
                concurrentEncoder = null;
            }
            stream.SetLength(44 + dataLength);
            stream.Position = 0; WriteHeader(dataLength); stream.Flush();
            stream.Dispose(); stream = null;
            if (concurrentEncoder != null)
            {
                try { concurrentEncoder.CloseInput(); }
                catch (Exception ex)
                {
                    Main.Entry.Logger.Log("[Audio] Concurrent audio input failed; using final mux: " + ex.Message);
                    concurrentEncoder.Dispose();
                    concurrentEncoder = null;
                }
            }
        }
        internal static void ValidateWavDataSize(long size)
        {
            if (size < 0 || size > uint.MaxValue - 36L) throw new IOException("WAV exceeded its 4 GB size limit.");
        }
        private void WriteHeader(long size)
        {
            using (var writer = new BinaryWriter(stream, Encoding.ASCII, true))
            {
                writer.Write(Encoding.ASCII.GetBytes("RIFF")); writer.Write(checked((uint)(36 + size)));
                writer.Write(Encoding.ASCII.GetBytes("WAVEfmt ")); writer.Write(16u);
                writer.Write((ushort)3); writer.Write((ushort)Channels); writer.Write(SampleRate);
                writer.Write(SampleRate * Channels * 4); writer.Write((ushort)(Channels * 4)); writer.Write((ushort)32);
                writer.Write(Encoding.ASCII.GetBytes("data")); writer.Write(checked((uint)size));
            }
        }

        private void AppendSilence(long sampleFrames)
        {
            if (sampleFrames <= 0) return;
            var diagnostics = RenderPerformanceDiagnostics.Current;
            if (diagnostics != null) diagnostics.Increment(RenderPerformanceCounter.AudioSilenceFrames, sampleFrames);
            int chunkFrames = Math.Max(1, Math.Min(16384, SampleRate));
            int chunkBytes = checked(chunkFrames * Channels * sizeof(float));
            if (bytes == null || bytes.Length < chunkBytes) bytes = new byte[chunkBytes];
            else Array.Clear(bytes, 0, chunkBytes);
            while (sampleFrames > 0)
            {
                int frames = (int)Math.Min(chunkFrames, sampleFrames);
                var diagnosticStart = diagnostics != null ? RenderPerformanceDiagnostics.Timestamp() : 0;
                stream.Write(bytes, 0, checked(frames * Channels * sizeof(float)));
                if (diagnostics != null) diagnostics.Record(RenderPerformanceStage.AudioWavWrite, diagnosticStart);
                WriteConcurrent(bytes, checked(frames * Channels * sizeof(float)));
                sampleFrames -= frames;
            }
        }

        private void InitializeListenerOutputCapture()
        {
            listenerLeft = new float[ListenerHistorySamples];
            listenerRight = Channels > 1 ? new float[ListenerHistorySamples] : null;
            try
            {
                AudioListener.GetOutputData(listenerLeft, 0);
                if (listenerRight != null) AudioListener.GetOutputData(listenerRight, 1);
                listenerOutputAvailable = true;
            }
            catch (Exception ex)
            {
                listenerOutputAvailable = false;
                Main.Entry.Logger.Log("[Audio] AudioListener.GetOutputData fallback unavailable: " + ex.Message);
            }
        }

        private void CaptureListenerOutput(long sampleFrames)
        {
            if (!listenerOutputAvailable || sampleFrames <= 0) return;
            int frames = (int)Math.Min(sampleFrames, ListenerHistorySamples);
            try
            {
                AudioListener.GetOutputData(listenerLeft, 0);
                if (listenerRight != null) AudioListener.GetOutputData(listenerRight, 1);
                EnsureManagedBuffers(checked(frames * Channels));
                int start = ListenerHistorySamples - frames;
                for (int frame = 0; frame < frames; frame++)
                {
                    int source = start + frame;
                    int destination = frame * Channels;
                    managed[destination] = listenerLeft[source];
                    for (int channel = 1; channel < Channels; channel++)
                        managed[destination + channel] = listenerRight != null ? listenerRight[source] : listenerLeft[source];
                }
            }
            catch (Exception ex)
            {
                listenerOutputAvailable = false;
                Main.Entry.Logger.Log("[Audio] AudioListener.GetOutputData failed during capture: " + ex.Message);
                return;
            }
            WriteManagedSamples(checked(frames * Channels));
        }

        private bool TryUseFilterFallback()
        {
            if (filter == null || filter.AvailableSamples < Channels) return false;
            useFilterFallback = true;
            captureMode = "AudioListener filter (AudioRenderer unavailable or returned empty/failed blocks)";
            filterSamplesToDiscard = checked(SampleFrames * Channels);
            return true;
        }

        private void CaptureFilterSamples(long sampleFrames)
        {
            if (filter == null || sampleFrames <= 0) return;
            while (sampleFrames > 0)
            {
                if (filterSamplesToDiscard > 0)
                {
                    int skip = (int)Math.Min(filterSamplesToDiscard, Math.Min(int.MaxValue, filter.AvailableSamples));
                    if (skip <= 0) return;
                    filter.SkipSamples(skip);
                    filterSamplesToDiscard -= skip;
                    continue;
                }

                int available = filter.AvailableSamples;
                int values = (int)Math.Min((long)available, checked(sampleFrames * Channels));
                values -= values % Channels;
                if (values <= 0) return;
                EnsureManagedBuffers(values);
                int read = filter.ReadSamples(managed, values);
                if (read <= 0) return;
                WriteManagedSamples(read);
                sampleFrames -= read / Channels;
            }
        }

        private void EnsureManagedBuffers(int length)
        {
            // Capture block sizes can vary by a few samples as the audio clock
            // rounds frame boundaries. Keep the largest buffers seen instead of
            // reallocating both arrays whenever that rounded size changes.
            if (managed == null || managed.Length < length) managed = new float[length];
            var byteLength = checked(length * sizeof(float));
            if (bytes == null || bytes.Length < byteLength) bytes = new byte[byteLength];
        }

        private void WriteManagedSamples(int sampleValues)
        {
            var diagnostics = RenderPerformanceDiagnostics.Current;
            ValidateWavDataSize(checked(stream.Length - 44 + (long)sampleValues * sizeof(float)));
            var diagnosticStart = diagnostics != null ? RenderPerformanceDiagnostics.Timestamp() : 0;
            var peak = Peak;
            for (int i = 0; i < sampleValues; i++) peak = Math.Max(peak, Math.Abs(managed[i]));
            Peak = peak;
            if (diagnostics != null) diagnostics.Record(RenderPerformanceStage.AudioPeakScan, diagnosticStart);
            diagnosticStart = diagnostics != null ? RenderPerformanceDiagnostics.Timestamp() : 0;
            Buffer.BlockCopy(managed, 0, bytes, 0, checked(sampleValues * sizeof(float)));
            if (diagnostics != null) diagnostics.Record(RenderPerformanceStage.AudioCopy, diagnosticStart);
            diagnosticStart = diagnostics != null ? RenderPerformanceDiagnostics.Timestamp() : 0;
            stream.Write(bytes, 0, checked(sampleValues * sizeof(float)));
            if (diagnostics != null) diagnostics.Record(RenderPerformanceStage.AudioWavWrite, diagnosticStart);
            WriteConcurrent(bytes, checked(sampleValues * sizeof(float)));
            SampleFrames += sampleValues / Channels;
            if (diagnostics != null) diagnostics.Increment(RenderPerformanceCounter.AudioSampleFrames, sampleValues / Channels);
        }

        public void Dispose()
        {
            try
            {
                filter?.StopCapture();
                if (started) AudioRenderer.Stop();
                filter?.DetachCapture();
            }
            finally
            {
                filter = null;
                started = false;
                useFilterFallback = false;
                useListenerOutputFallback = false;
                listenerOutputAvailable = false;
                filterSamplesToDiscard = 0;
                zeroSampleReads = 0;
                listenerLeft = null;
                listenerRight = null;
                if (samples.IsCreated) samples.Dispose();
                concurrentEncoder?.Dispose(); concurrentEncoder = null;
                stream?.Dispose(); stream = null;
            }
        }
    }
}
