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
internal sealed class DiagnosticRecorder(DiagnosticStore store, TimeProvider time, string version)
{
    private readonly Lock _gate = new();
    private DiagnosticRecord _record = store.Load(version);
    private bool _dirty;

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
            _dirty = true;
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
            _dirty = true;
        }
    }

    /// <summary>Forgets everything recorded so far.</summary>
    public void Clear()
    {
        lock (_gate)
        {
            _record = new DiagnosticRecord(version);
            _dirty = true;
        }
    }

    /// <summary>Writes the record if anything has changed.</summary>
    public void Flush()
    {
        DiagnosticRecord snapshot;
        lock (_gate)
        {
            if (!_dirty)
                return;

            snapshot = new DiagnosticRecord(_record);
            _dirty = false;
        }

        store.Save(snapshot);
    }
}
