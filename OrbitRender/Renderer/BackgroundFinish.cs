using System;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.ExceptionServices;
using System.Threading;

namespace OrbitRender.Renderer
{
    /// <summary>Where a render's background finish is.</summary>
    internal enum FinishStage
    {
        Audio,
        Overlays,
        Composing,
    }

    /// <summary>
    /// The file and FFmpeg end of a render: the audio mux, the host's overlay videos and the
    /// composition. It runs off the game thread, so the game keeps drawing its progress for the
    /// minutes this can take instead of freezing. Cancel kills the FFmpeg it runs; a host call it
    /// is waiting on returns on its own, and the work stops after it.
    /// </summary>
    internal sealed class BackgroundFinish
    {
        private readonly CancellationTokenSource cancel = new CancellationTokenSource();
        private readonly object sync = new object();
        private readonly Thread thread;
        private readonly Stopwatch clock = Stopwatch.StartNew();
        // Seconds spent in each stage, for the log.
        private readonly double[] seconds = new double[3];
        private double stageStart;
        private FinishStage stage;
        private double? progress;
        private Exception failure;
        private volatile bool completed;

        private BackgroundFinish(Action<BackgroundFinish> work)
        {
            thread = new Thread(() =>
            {
                try { work(this); }
                catch (Exception ex) { failure = ex; }
                finally
                {
                    lock (sync) CloseStage();
                    completed = true;
                }
            }) { IsBackground = true, Name = "OrbitRender finish" };
        }

        internal static BackgroundFinish Start(Action<BackgroundFinish> work)
        {
            if (work == null) throw new ArgumentNullException(nameof(work));
            var finish = new BackgroundFinish(work);
            finish.thread.Start();
            return finish;
        }

        internal bool IsCompleted => completed;
        internal double ElapsedSeconds => clock.Elapsed.TotalSeconds;
        internal CancellationToken Token => cancel.Token;

        internal FinishStage Stage { get { lock (sync) return stage; } }

        /// <summary>How far the current stage is, 0 to 1, or null when it cannot tell.</summary>
        internal double? Progress { get { lock (sync) return progress; } }

        internal void Report(FinishStage value, double? fraction = null)
        {
            lock (sync)
            {
                if (value != stage) CloseStage();
                stage = value;
                progress = fraction.HasValue ? Math.Max(0, Math.Min(1, fraction.Value)) : (double?)null;
            }
        }

        private void CloseStage()
        {
            var now = clock.Elapsed.TotalSeconds;
            seconds[(int)stage] += now - stageStart;
            stageStart = now;
        }

        /// <summary>Where the time went, once the work ended: "audio 2.1 s, overlays 79.3 s, composing 61.0 s".</summary>
        internal string Timings()
        {
            lock (sync)
                return string.Format(CultureInfo.InvariantCulture, "audio {0:0.0} s, overlays {1:0.0} s, composing {2:0.0} s",
                    seconds[(int)FinishStage.Audio], seconds[(int)FinishStage.Overlays], seconds[(int)FinishStage.Composing]);
        }

        internal void Cancel()
        {
            try { cancel.Cancel(); }
            catch (ObjectDisposedException) { }
        }

        /// <summary>Waits for the work to end; false when it is still in a host call.</summary>
        internal bool Wait(int milliseconds) => thread.Join(milliseconds);

        /// <summary>Rethrows the work's failure on the game thread, keeping its stack.</summary>
        internal void ThrowIfFailed()
        {
            if (failure != null) ExceptionDispatchInfo.Capture(failure).Throw();
        }
    }
}
