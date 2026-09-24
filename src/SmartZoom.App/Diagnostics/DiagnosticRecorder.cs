using SmartZoom.Core.Diagnostics;

namespace SmartZoom.App.Diagnostics;

/// <summary>
/// The one place anything is recorded. Thread-safe, allocation-light, and never touches disk on the zoom path.
/// </summary>
/// <remarks>
/// <see cref="Snapshot"/>, not a live reference, is how a caller reads the record: the renderer runs on the
/// UI thread while the dispatcher thread may be inside <see cref="Note"/> at the same moment, and enumerating
/// a mutating <see cref="DiagnosticRecord"/> would throw. <see cref="DiagnosticRecord"/> itself stays a pure,
/// lock-free, unit-testable Core type; the concurrency boundary lives here, where the threading is known.
/// </remarks>
internal sealed class DiagnosticRecorder(DiagnosticStore store, TimeProvider time, string version) : IGesturePacingSink
{
    private readonly Lock _gate = new();

    // Held across snapshot, save and mark, so that two flushes (the timer's and a crash's) can never write an
    // older snapshot last. Never taken on the zoom path, so the disk still waits for nobody but the flusher.
    private readonly Lock _flushing = new();
    private DiagnosticRecord _record = store.Load(version);

    // A version rather than a bool. "Dirty" cannot express "saved what existed at the moment the write
    // started, and something has been recorded since" - and a flush that cleared a bool would then lose
    // whatever arrived while the file was being written.
    private long _changes;
    private long _written;

    /// <summary>Whether anything is recorded at all.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// A point-in-time copy of the record, safe to enumerate on any thread. Taken under the same lock that
    /// guards every mutation, so it can never observe a record mid-mutation, and later mutation of the live
    /// record can never be observed through it.
    /// </summary>
    public DiagnosticRecord Snapshot()
    {
        lock (_gate)
        {
            return new DiagnosticRecord(_record);
        }
    }

    /// <summary>Counts one occurrence.</summary>
    /// <param name="key">What happened.</param>
    public void Note(DiagnosticKey key)
    {
        if (!Enabled)
            return;

        lock (_gate)
        {
            _record.Note(key, time.GetUtcNow());
            _changes++;
        }
    }

    /// <summary>Keeps one worked example.</summary>
    /// <param name="sample">The example.</param>
    public void Sample(DiagnosticSample sample)
    {
        if (!Enabled)
            return;

        lock (_gate)
        {
            _record.Sample(sample);
            _changes++;
        }
    }

    /// <summary>Records one delivered gesture's pacing.</summary>
    /// <param name="frames">Frames the gesture asked for.</param>
    /// <param name="intervalMs">The interval between them.</param>
    /// <param name="lateFrames">How many missed their slot.</param>
    /// <param name="worstLateMs">The worst lateness in this gesture.</param>
    public void Paced(int frames, int intervalMs, int lateFrames, double worstLateMs)
    {
        if (!Enabled)
            return;

        lock (_gate)
        {
            _record.Gestures.Add(frames, intervalMs, lateFrames, worstLateMs);
            _changes++;
        }
    }

    /// <summary>Forgets everything recorded so far.</summary>
    public void Clear()
    {
        lock (_gate)
        {
            _record = new DiagnosticRecord(version);
            _changes++;
        }
    }

    /// <summary>Writes the record if anything has changed.</summary>
    /// <remarks>
    /// The record is marked written only once the store says it wrote it, so a failed write leaves it pending
    /// for the next flush. The file I/O deliberately happens outside <c>_gate</c>: that lock is taken on the
    /// zoom path, and diagnostics may never make a zoom wait for a disk.
    /// </remarks>
    public void Flush()
    {
        lock (_flushing)
        {
            DiagnosticRecord snapshot;
            long changes;
            lock (_gate)
            {
                if (_changes == _written)
                    return;

                snapshot = new DiagnosticRecord(_record);
                changes = _changes;
            }

            if (!store.Save(snapshot))
                return;

            lock (_gate)
            {
                _written = changes;
            }
        }
    }
}
