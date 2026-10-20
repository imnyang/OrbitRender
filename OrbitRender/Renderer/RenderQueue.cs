using System;
using System.Collections.Generic;
using System.Linq;

namespace OrbitRender.Renderer
{
    internal enum QueueItemState { Pending, Running, Completed, Failed, Cancelled }

    internal sealed class RenderQueueItem
    {
        internal readonly object Level;
        internal readonly string Label;
        private readonly RenderRequestOptions options;
        internal RenderRequestOptions Options => options.Snapshot();
        internal QueueItemState State { get; set; }
        internal string Output { get; set; }
        internal string Error { get; set; }
        internal RenderQueueItem(object level, string label, RenderRequestOptions options)
        { Level = level; Label = label; this.options = options.Snapshot(); }
    }

    internal sealed class RenderQueue
    {
        private readonly List<RenderQueueItem> items = new List<RenderQueueItem>();
        internal IReadOnlyList<RenderQueueItem> Items => items;
        internal bool IsRunning { get; private set; }
        internal RenderQueueItem Active { get; private set; }
        internal bool HasPending => items.Any(i => i.State == QueueItemState.Pending);
        internal int PendingCount => items.Count(i => i.State == QueueItemState.Pending);
        internal int Revision { get; private set; }
        internal void Add(object level, string label, RenderRequestOptions options)
        {
            if (level == null || options == null) throw new ArgumentNullException();
            items.Add(new RenderQueueItem(level, label, options)); Revision++;
        }
        internal void Start() { IsRunning = HasPending; Revision++; }
        internal void Pause() { IsRunning = false; Revision++; }
        internal RenderQueueItem BeginNext(object level)
        {
            if (!IsRunning || Active != null) return null;
            var next = items.FirstOrDefault(i => i.State == QueueItemState.Pending);
            if (next == null) { Pause(); return null; }
            if (!ReferenceEquals(next.Level, level)) { Pause(); return null; }
            next.State = QueueItemState.Running; Active = next; Revision++; return next;
        }
        internal void Complete(QueueItemState state, string output, string error)
        {
            if (Active == null) return;
            if (state != QueueItemState.Completed && state != QueueItemState.Failed && state != QueueItemState.Cancelled)
                throw new ArgumentOutOfRangeException(nameof(state));
            Active.State = state; Active.Output = output; Active.Error = error; Active = null;
            if (state != QueueItemState.Completed || !HasPending) IsRunning = false;
            Revision++;
        }
        internal void Remove(RenderQueueItem item)
        { if (item != Active && items.Remove(item)) Revision++; }
        internal void Move(RenderQueueItem item, int direction)
        {
            var index = items.IndexOf(item); var other = index + direction;
            if (item.State != QueueItemState.Pending || index < 0 || other < 0 || other >= items.Count
                || items[other].State != QueueItemState.Pending) return;
            items[index] = items[other]; items[other] = item; Revision++;
        }
        internal void ClearFinished()
        { items.RemoveAll(i => i.State != QueueItemState.Pending && i.State != QueueItemState.Running); Revision++; }
    }
}
