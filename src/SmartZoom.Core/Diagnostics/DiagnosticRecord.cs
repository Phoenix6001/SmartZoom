namespace SmartZoom.Core.Diagnostics;

/// <summary>
/// A tally of what SmartZoom failed to do, bounded by construction.
/// </summary>
/// <remarks>
/// A tally rather than a journal: a journal of every event grows without limit and says less than a count.
/// When the key cap is reached new keys are refused rather than evicted, because the long tail is the
/// interesting part and silently dropping it would make the counts lie.
/// </remarks>
public sealed class DiagnosticRecord
{
    /// <summary>The most distinct things worth counting before the record stops taking new ones.</summary>
    public const int MaxKeys = 200;

    /// <summary>How many worked examples are kept.</summary>
    public const int MaxSamples = 20;

    private readonly Dictionary<DiagnosticKey, DiagnosticCounter> _counters = [];
    private readonly List<DiagnosticSample> _samples = [];

    /// <summary>Creates an empty record for one build of SmartZoom.</summary>
    /// <param name="version">The version that produced it; a record from another version is discarded on load.</param>
    public DiagnosticRecord(string version) => Version = version;

    /// <summary>Creates an independent copy of <paramref name="source"/> that shares no mutable state with it.</summary>
    /// <param name="source">The record to copy.</param>
    /// <remarks>
    /// The copy's counters and samples are copied by value, so mutating <paramref name="source"/> afterwards
    /// cannot be observed through the copy. This is how a caller who must not tear a record mid-mutation (for
    /// example while enumerating it on another thread) gets a safe, immutable-in-practice view.
    /// </remarks>
    public DiagnosticRecord(DiagnosticRecord source)
    {
        ArgumentNullException.ThrowIfNull(source);

        Version = source.Version;
        OmittedKeys = source.OmittedKeys;
        Gestures = new GestureHealth(source.Gestures);
        _counters = source._counters.ToDictionary(kv => kv.Key, kv => new DiagnosticCounter(kv.Value));
        _samples = [.. source._samples];
    }

    /// <summary>The SmartZoom version this record describes.</summary>
    public string Version { get; }

    /// <summary>The counters, most frequent first.</summary>
    public IReadOnlyList<DiagnosticCounter> Counters =>
        [.. _counters.Values.OrderByDescending(c => c.Count)];

    /// <summary>The worked examples, oldest first.</summary>
    public IReadOnlyList<DiagnosticSample> Samples => _samples;

    /// <summary>How many distinct things were not counted because the cap was reached.</summary>
    public int OmittedKeys { get; private set; }

    /// <summary>How well injected gestures have been delivered on this machine.</summary>
    public GestureHealth Gestures { get; } = new();

    /// <summary>Counts one occurrence.</summary>
    /// <param name="key">What happened.</param>
    /// <param name="when">When it happened.</param>
    public void Note(DiagnosticKey key, DateTimeOffset when)
    {
        ArgumentNullException.ThrowIfNull(key);

        if (_counters.TryGetValue(key, out var counter))
        {
            counter.Add(when);
            return;
        }

        if (_counters.Count >= MaxKeys)
        {
            OmittedKeys++;
            return;
        }

        _counters[key] = new DiagnosticCounter(key, when);
    }

    /// <summary>Keeps one worked example, dropping the oldest when the ring is full.</summary>
    /// <param name="sample">The example.</param>
    public void Sample(DiagnosticSample sample)
    {
        ArgumentNullException.ThrowIfNull(sample);

        _samples.Add(sample);
        if (_samples.Count > MaxSamples)
            _samples.RemoveAt(0);
    }
}
