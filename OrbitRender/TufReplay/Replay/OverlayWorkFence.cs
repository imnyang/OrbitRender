using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;

namespace TUFReplayRenderer.Replay;

// Instrument standard managed queues at overlay call sites. A dequeued item is
// still pending until the consumer reaches its next dequeue or leaves the method.
// This distinguishes 'queue empty' from 'the worker finished drawing the key'.
internal sealed class OverlayWorkFence
{
    private sealed class QueueState
    {
        internal readonly LinkedList<Receipt> Times = new();
        internal int Running;
        internal int RecordedPending;
        internal Func<int> Count;
    }
    private sealed class Receipt { internal double Time; internal object Item; }
    private readonly Dictionary<object, QueueState> queues = new();
    private sealed class Lease
    {
        internal OverlayWorkFence Owner;
        internal object Queue;
        internal QueueState State;
        internal double Time;
        internal Lease Parent;
    }
    private long revision;
    internal readonly HashSet<Type> HandoffQueues = new();
    internal long Revision { get { lock (queues) return revision; } }
    [ThreadStatic] private static OverlayWorkFence dispatchOwner;
    [ThreadStatic] private static double dispatchTime;
    [ThreadStatic] private static Lease consuming;
    internal double? EventTime => dispatchOwner == this ? dispatchTime
        : consuming?.Owner == this ? consuming.Time : null;

    internal IDisposable Dispatch(double seconds)
    {
        var previous = dispatchOwner; double time = dispatchTime;
        dispatchOwner = this; dispatchTime = seconds;
        return new Scope(() => { dispatchOwner = previous; dispatchTime = time; });
    }
    internal void Enqueue<T>(ConcurrentQueue<T> queue, T item)
    {
        lock (queues)
        {
            // Queue mutation and timestamp registration are atomic relative to
            // wrapped consumers, including when a worker propagates UI work.
            double? time = EventTime;
            if (time.HasValue)
            {
                if (!queues.TryGetValue(queue, out var state))
                {
                    queues.Add(queue, state = new QueueState { Count = () => queue.Count });
                    for (int i = 0, count = queue.Count; i < count; i++) state.Times.AddLast(new Receipt { Time = double.NaN });
                }
                state.Times.AddLast(new Receipt { Time = time.Value, Item = HandoffQueues.Contains(queue.GetType()) ? (object)item : null });
                state.RecordedPending++;
            }
            else if (queues.TryGetValue(queue, out var state)) state.Times.AddLast(new Receipt { Time = double.NaN });
            queue.Enqueue(item);
        }
    }
    internal bool TryDequeue<T>(ConcurrentQueue<T> queue, out T item)
    {
        // A nested queue must not falsely complete its parent's in-flight work.
        if (consuming?.Owner == this && ReferenceEquals(consuming.Queue, queue)) CompleteConsumer();
        lock (queues)
        {
            if (!queue.TryDequeue(out item)) return false;
            if (queues.TryGetValue(queue, out var state) && state.Times.Count > 0)
            {
                double time = state.Times.First.Value.Time;
                state.Times.RemoveFirst();
                if (!double.IsNaN(time))
                {
                    state.RecordedPending--;
                    state.Running++;
                    consuming = new Lease { Owner = this, Queue = queue, State = state, Time = time, Parent = consuming };
                }
            }
            return true;
        }
    }
    internal IDisposable ConsumeHandoff(object item)
    {
        Lease before = consuming;
        if (item == null) return null;
        lock (queues)
        {
            foreach (var entry in queues)
            {
                if (!HandoffQueues.Contains(entry.Key.GetType())) continue;
                for (var node = entry.Value.Times.First; node != null; node = node.Next)
                {
                    if (node.Value.Item == null || !Equals(node.Value.Item, item)) continue;
                    double time = node.Value.Time;
                    entry.Value.Times.Remove(node);
                    entry.Value.RecordedPending--;
                    entry.Value.Running++;
                    consuming = new Lease { Owner = this, Queue = entry.Key, State = entry.Value, Time = time, Parent = before };
                    return new Scope(() => { while (consuming != before && consuming?.Owner == this) CompleteConsumer(); });
                }
            }
        }
        return null;
    }
    internal string PendingDescription
    {
        get {
            lock (queues)
            {
                var pending = new List<string>();
                foreach (var entry in queues)
                    if (entry.Value.Running != 0 || entry.Value.RecordedPending != 0)
                        pending.Add(entry.Key.GetType().ToString() + ": queued=" + entry.Value.Count() + ", tracked=" + entry.Value.RecordedPending + ", processing=" + entry.Value.Running);
                return string.Join("; ", pending);
            }
        }
    }
    internal void CompleteConsumer()
    {
        if (consuming?.Owner != this) return;
        lock (queues) { consuming.State.Running--; revision++; }
        consuming = consuming.Parent;
    }
    internal IDisposable ConsumerScope()
    {
        Lease before = consuming;
        return new Scope(() => { while (consuming != before && consuming?.Owner == this) CompleteConsumer(); });
    }
    internal bool IsSettled
    {
        get { lock (queues) { foreach (var state in queues.Values) if (state.Running != 0 || state.RecordedPending != 0) return false; return true; } }
    }
    private sealed class Scope : IDisposable
    {
        private Action restore;
        internal Scope(Action restore) { this.restore = restore; }
        public void Dispose() { Interlocked.Exchange(ref restore, null)?.Invoke(); }
    }
}
