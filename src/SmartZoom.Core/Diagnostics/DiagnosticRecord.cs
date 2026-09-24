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

    /// <summary>The most occurrences one restored counter may claim.</summary>
    /// <remarks>
    /// A ceiling on what a file is allowed to assert, not on what SmartZoom can count in a session. The file
    /// is plain JSON in the user's own profile and the record is scoped to one build, so a seven-figure count
    /// is a typo or a hand edit rather than a tally anybody produced by pressing a button.
    /// </remarks>
    public const int MaxRestoredCount = 1_000_000;

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

    /// <summary>The counters, most frequent first, then by key.</summary>
    /// <remarks>
    /// The second sort key is what makes the order stable. Without it two renders of the same data can put
    /// equally frequent rows in different places - dictionary order is not defined - and a user comparing
    /// two reports sees a difference that is not one.
    /// </remarks>
    public IReadOnlyList<DiagnosticCounter> Counters =>
        [.. _counters.Values
            .OrderByDescending(c => c.Count)
            .ThenBy(c => c.Key.ToString(), StringComparer.Ordinal)];

    /// <summary>The worked examples, oldest first.</summary>
    /// <remarks>A copy, like <see cref="Counters"/>: a caller must not be able to reach the backing list.</remarks>
    public IReadOnlyList<DiagnosticSample> Samples => [.. _samples];

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

    /// <summary>Puts a stored counter back, in one step rather than one call per occurrence.</summary>
    /// <param name="key">What was counted.</param>
    /// <param name="count">How many times, as the file claims; clamped to <see cref="MaxRestoredCount"/>.</param>
    /// <param name="firstSeen">When it first happened.</param>
    /// <param name="lastSeen">When it last happened.</param>
    /// <remarks>
    /// The key cap still applies, so a file listing more than <see cref="MaxKeys"/> keys fills
    /// <see cref="OmittedKeys"/> exactly as a running session would. What does not apply is the cost of the
    /// count: the file is read while SmartZoom starts, so a count is assigned in one step rather than replayed
    /// one occurrence at a time, and a file claiming two billion occurrences costs nothing extra.
    /// </remarks>
    public void Restore(DiagnosticKey key, int count, DateTimeOffset firstSeen, DateTimeOffset lastSeen)
    {
        ArgumentNullException.ThrowIfNull(key);

        if (count <= 0)
            return;

        var claimed = Math.Min(count, MaxRestoredCount);
        if (_counters.TryGetValue(key, out var counter))
        {
            counter.Add(claimed, lastSeen);
            return;
        }

        if (_counters.Count >= MaxKeys)
        {
            OmittedKeys++;
            return;
        }

        _counters[key] = new DiagnosticCounter(key, claimed, firstSeen, lastSeen);
    }

    /// <summary>Puts back the count of keys a stored record had already refused.</summary>
    /// <param name="keys">How many, as the file claims; clamped to <see cref="MaxRestoredCount"/>.</param>
    /// <remarks>
    /// Restored rather than recomputed. The file holds only the keys that fitted, so replaying it can never
    /// rediscover the ones that did not - without this the report says "nothing was dropped" on the very
    /// records where most was.
    /// </remarks>
    public void RestoreOmitted(int keys)
    {
        if (keys > 0)
            OmittedKeys += Math.Min(keys, MaxRestoredCount);
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
