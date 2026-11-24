using System;
using System.IO;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Threading;
using System.Text;
using Unity.Collections;
using UnityEngine;

namespace OrbitRender.Renderer
{
    internal sealed class GameAudioCapture : IDisposable
    {
        private FileStream stream;
        private BlockingCollection<ArraySegment<byte>> wavBlocks;
        private readonly ConcurrentQueue<byte[]> wavBuffers = new ConcurrentQueue<byte[]>();
        private Thread wavWriter;
        private Exception wavFailure;
        private long wavPayloadLength;
        private long wavQueueWaitTicks;
        private long wavWriteTicks;
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
        public int SampleRate { get; private set; }
        public int Channels { get; private set; }
        public long SampleFrames { get; private set; }
        public float Peak { get; private set; }
        public bool NeedsRealtimePacing => useListenerOutputFallback;
        // Late sound effects must be scheduled after the captured DSP boundary.
        public double RenderedUntilDsp(double dspOrigin)
        {
            if (SampleRate <= 0) return dspOrigin;
            return dspOrigin + SampleFrames / (double)SampleRate;
        }
        public double CaptureSeconds => System.Threading.Interlocked.Read(ref captureTicks)
            / (double)System.Diagnostics.Stopwatch.Frequency;
        public double WavQueueWaitSeconds => wavQueueWaitTicks / (double)Stopwatch.Frequency;
        public double WavWriteSeconds => wavWriteTicks / (double)Stopwatch.Frequency;
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

        public bool FinishConcurrentEncoding(CancellationToken cancellation = default)
        {
            var encoder = concurrentEncoder;
            concurrentEncoder = null;
            if (encoder == null) return false;
            var start = System.Diagnostics.Stopwatch.GetTimestamp();
            try { encoder.Finish(cancellation); return true; }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                Main.Entry.Logger.Log("[Audio] Concurrent audio encoding failed; using final mux: " + ex.Message);
                return false;
            }
            finally
            {
                concurrentFinishTicks += System.Diagnostics.Stopwatch.GetTimestamp() - start;
                encoder.Dispose();
            }
        }

        public void Begin(string path, int videoFps)
        {
            if (videoFps <= 0) throw new ArgumentOutOfRangeException(nameof(videoFps));
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
                wavBlocks = new BlockingCollection<ArraySegment<byte>>(32);
                wavWriter = new Thread(WriteWavBlocks) { IsBackground = true, Name = "OrbitRender WAV" };
                wavWriter.Start();
                filter = AudioFilterCapture.Attach();
                started = AudioRenderer.Start();
                if (!started)
                {
                    Main.Entry.Logger.Log("[Audio] Unity AudioRenderer could not start; trying the listener output fallback.");
                }
                filter?.StartCapture(SampleRate, Channels);
                InitializeListenerOutputCapture(videoFps);
                useFilterFallback = false;
                // Live listener polling is only safe when AudioRenderer never started.
                useListenerOutputFallback = !started && listenerOutputAvailable;
                Main.Entry.Logger.Log(filter == null
                    ? "[Audio] AudioListener filter unavailable on the mixed listener."
                    : "[Audio] AudioListener filter fallback attached and waiting for mixer samples.");
            }
            catch { Dispose(); throw; }
        }

        public void CaptureFrame(long videoFrameIndex, int fps)
        {
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
                int count = AudioRenderer.GetSampleCountForCaptureFrame();
                // A zero read may be temporary; a later block can include those samples.
                if (count == 0)
                {
                    zeroSampleReads++;
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
                    EnsureManagedBuffers(length);
                }
                if (!AudioRenderer.Render(samples))
                {
                    zeroSampleReads++;
                    if (TryUseFilterFallback()) CaptureFilterSamples(expectedSamples - SampleFrames);
                    return;
                }
                NativeArray<float>.Copy(samples, managed, length);
                WriteManagedSamples(count * Channels);
            }
            finally
            {
                if (Time.captureFramerate != previousCaptureFramerate)
                    Time.captureFramerate = previousCaptureFramerate;
                System.Threading.Interlocked.Add(ref captureTicks,
                    System.Diagnostics.Stopwatch.GetTimestamp() - captureStart);
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
            if (targetSamples > 0 && SampleFrames == 0)
                throw new IOException("The audio backend returned no samples. No silent replacement audio was created.");
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
            wavPayloadLength = dataLength;
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

        public void FinishWav()
        {
            StopWavWriter();
            if (wavFailure != null) throw new IOException("Writing captured WAV failed.", wavFailure);
            var dataLength = wavPayloadLength;
            stream.SetLength(44 + dataLength);
            stream.Position = 0; WriteHeader(dataLength); stream.Flush();
            stream.Dispose(); stream = null;
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
            int chunkFrames = Math.Max(1, Math.Min(16384, SampleRate));
            int chunkBytes = checked(chunkFrames * Channels * sizeof(float));
            if (bytes == null || bytes.Length < chunkBytes) bytes = new byte[chunkBytes];
            else Array.Clear(bytes, 0, chunkBytes);
            while (sampleFrames > 0)
            {
                int frames = (int)Math.Min(chunkFrames, sampleFrames);
                QueueWavBlock(checked(frames * Channels * sizeof(float)));
                WriteConcurrent(bytes, checked(frames * Channels * sizeof(float)));
                sampleFrames -= frames;
            }
        }

        private void InitializeListenerOutputCapture(int videoFps)
        {
            // Allocate before playback: resizing a polling buffer loses its history.
            // Keep the smallest power of two that holds a full output frame.
            int historySamples = 1024;
            long frameSamples = ((long)SampleRate + videoFps - 1) / videoFps;
            while (historySamples < frameSamples) historySamples = checked(historySamples * 2);
            listenerLeft = new float[historySamples];
            listenerRight = Channels > 1 ? new float[historySamples] : null;
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
            if (sampleFrames > listenerLeft.Length)
                throw new IOException("Audio listener history is too short for the requested capture interval.");
            int frames = (int)sampleFrames;
            try
            {
                AudioListener.GetOutputData(listenerLeft, 0);
                if (listenerRight != null) AudioListener.GetOutputData(listenerRight, 1);
                EnsureManagedBuffers(checked(frames * Channels));
                int start = listenerLeft.Length - frames;
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
                throw new IOException("Audio listener output capture failed.", ex);
            }
            WriteManagedSamples(checked(frames * Channels));
        }

        private bool TryUseFilterFallback()
        {
            if (filter == null || filter.AvailableSamples < Channels) return false;
            useFilterFallback = true;
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
            ValidateWavDataSize(checked((SampleFrames * Channels + sampleValues) * sizeof(float)));
            var peak = Peak;
            for (int i = 0; i < sampleValues; i++) peak = Math.Max(peak, Math.Abs(managed[i]));
            Peak = peak;
            Buffer.BlockCopy(managed, 0, bytes, 0, checked(sampleValues * sizeof(float)));
            QueueWavBlock(checked(sampleValues * sizeof(float)));
            WriteConcurrent(bytes, checked(sampleValues * sizeof(float)));
            SampleFrames += sampleValues / Channels;
        }

        private void QueueWavBlock(int count)
        {
            var failure = Volatile.Read(ref wavFailure);
            if (failure != null) throw new IOException("Writing captured WAV failed.", failure);
            if (!wavBuffers.TryDequeue(out var buffer) || buffer.Length < count)
                buffer = new byte[count];
            Buffer.BlockCopy(bytes, 0, buffer, 0, count);
            var block = new ArraySegment<byte>(buffer, 0, count);
            if (wavBlocks.TryAdd(block)) return;
            var waitStart = Stopwatch.GetTimestamp();
            wavBlocks.Add(block);
            wavQueueWaitTicks += Stopwatch.GetTimestamp() - waitStart;
        }

        private void WriteWavBlocks()
        {
            foreach (var block in wavBlocks.GetConsumingEnumerable())
            {
                if (wavFailure == null)
                {
                    var writeStart = Stopwatch.GetTimestamp();
                    try { stream.Write(block.Array, block.Offset, block.Count); }
                    catch (Exception ex) { Volatile.Write(ref wavFailure, ex); }
                    finally { wavWriteTicks += Stopwatch.GetTimestamp() - writeStart; }
                }
                wavBuffers.Enqueue(block.Array);
            }
        }

        private void StopWavWriter()
        {
            if (wavBlocks == null) return;
            wavBlocks.CompleteAdding();
            if (wavWriter != null && wavWriter.IsAlive) wavWriter.Join();
            wavBlocks.Dispose();
            wavBlocks = null;
        }

        public void StopCapture()
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
                if (samples.IsCreated) samples.Dispose();
            }
        }

        public void Dispose()
        {
            try { StopCapture(); }
            finally
            {
                concurrentEncoder?.Dispose(); concurrentEncoder = null;
                StopWavWriter();
                stream?.Dispose(); stream = null;
            }
        }
    }
}
