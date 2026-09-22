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
}
