namespace SmartZoom.Core.Diagnostics;

/// <summary>How often one thing has happened, and when it first and last did.</summary>
public sealed class DiagnosticCounter
{
    /// <summary>Creates a counter at its first occurrence.</summary>
    /// <param name="key">What is being counted.</param>
    /// <param name="when">When it first happened.</param>
    public DiagnosticCounter(DiagnosticKey key, DateTimeOffset when)
    {
        Key = key;
        FirstSeen = when;
        LastSeen = when;
        Count = 1;
    }

    /// <summary>Puts a stored counter back at its stored count and timestamps, in one step.</summary>
    /// <param name="key">What is being counted.</param>
    /// <param name="count">How many times it has happened; at least one.</param>
    /// <param name="firstSeen">When it first happened.</param>
    /// <param name="lastSeen">When it last happened.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="count"/> is below one.</exception>
    /// <remarks>
    /// Both timestamps are assigned whatever the count, so a counter restored with a count of one keeps the
    /// last-seen time the file gave it rather than the first-seen one.
    /// </remarks>
    public DiagnosticCounter(DiagnosticKey key, int count, DateTimeOffset firstSeen, DateTimeOffset lastSeen)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(count, 1);

        Key = key;
        Count = count;
        FirstSeen = firstSeen;
        LastSeen = lastSeen;
    }

    /// <summary>Creates an independent copy of <paramref name="source"/>.</summary>
    /// <param name="source">The counter to copy.</param>
    /// <remarks>Builds a point-in-time snapshot that cannot be affected by later mutation of the original.</remarks>
    public DiagnosticCounter(DiagnosticCounter source)
    {
        ArgumentNullException.ThrowIfNull(source);

        Key = source.Key;
        Count = source.Count;
        FirstSeen = source.FirstSeen;
        LastSeen = source.LastSeen;
    }

    /// <summary>What is being counted.</summary>
    public DiagnosticKey Key { get; }

    /// <summary>How many times it has happened.</summary>
    public int Count { get; private set; }

    /// <summary>When it first happened.</summary>
    public DateTimeOffset FirstSeen { get; }

    /// <summary>When it last happened.</summary>
    public DateTimeOffset LastSeen { get; private set; }

    /// <summary>Records one more occurrence.</summary>
    /// <param name="when">When it happened.</param>
    public void Add(DateTimeOffset when)
    {
        Count++;
        LastSeen = when;
    }

    /// <summary>Records several occurrences at once.</summary>
    /// <param name="occurrences">How many to add; zero and below add nothing.</param>
    /// <param name="when">When the last of them happened.</param>
    /// <remarks>
    /// Restoring a stored counter needs this: replaying a saved count one <see cref="Add(DateTimeOffset)"/>
    /// at a time costs time proportional to a number that came off disk, and a hand-edited file could make
    /// that number two billion. The total is saturated rather than allowed to overflow.
    /// </remarks>
    public void Add(int occurrences, DateTimeOffset when)
    {
        if (occurrences <= 0)
            return;

        Count = (int)Math.Min(int.MaxValue, (long)Count + occurrences);
        LastSeen = when;
    }
}
