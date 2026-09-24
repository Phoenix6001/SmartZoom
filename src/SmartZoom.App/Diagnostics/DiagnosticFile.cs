using SmartZoom.Core.Diagnostics;

namespace SmartZoom.App.Diagnostics;

/// <summary>The on-disk shape. Separate from the record so the file format can change freely.</summary>
internal sealed class DiagnosticFile
{
    /// <summary>The SmartZoom version that wrote this file.</summary>
    public string Version { get; set; } = string.Empty;

    /// <summary>How many distinct things were not counted because the cap was reached.</summary>
    public int OmittedKeys { get; set; }

    /// <summary>The counters, as stored.</summary>
    public List<StoredCounter> Counters { get; set; } = [];

    /// <summary>The worked examples, as stored.</summary>
    public List<DiagnosticSample> Samples { get; set; } = [];

    /// <summary>How well injected gestures have been delivered, as stored.</summary>
    public StoredGestures Gestures { get; set; } = new();

    /// <summary>Builds the on-disk shape from a live record.</summary>
    /// <param name="record">The record to persist.</param>
    /// <param name="withSamples">Whether to include the worked examples; false drops them to save space.</param>
    public static DiagnosticFile From(DiagnosticRecord record, bool withSamples = true) => new()
    {
        Version = record.Version,
        OmittedKeys = record.OmittedKeys,
        Counters = [.. record.Counters.Select(c => new StoredCounter
        {
            Key = c.Key,
            Count = c.Count,
            FirstSeen = c.FirstSeen,
            LastSeen = c.LastSeen,
        })],
        Samples = withSamples ? [.. record.Samples] : [],
        Gestures = new StoredGestures
        {
            Gestures = record.Gestures.Gestures,
            Frames = record.Gestures.Frames,
            LateFrames = record.Gestures.LateFrames,
            WorstLateMs = record.Gestures.WorstLateMs,
            IntervalMs = record.Gestures.IntervalMs,
        },
    };

    /// <summary>Replays this file's contents into <paramref name="record"/> through its own caps.</summary>
    /// <param name="record">The record to populate.</param>
    public void Restore(DiagnosticRecord record)
    {
        record.RestoreOmitted(OmittedKeys);

        // Every list here can come back null: "counters": null is valid JSON, and the file is in the user's
        // own profile where anything can have been typed into it.
        foreach (var stored in Counters ?? [])
        {
            if (stored?.Key is null)
                continue;

            // Through the record's own Restore, so its caps apply to a file that was edited by hand - but in
            // one step: replaying a stored count one occurrence at a time costs time proportional to a number
            // that came off disk, inside a field initialiser that runs during DI construction.
            record.Restore(stored.Key, stored.Count, stored.FirstSeen, stored.LastSeen);
        }

        foreach (var sample in Samples ?? [])
        {
            if (sample is not null)
                record.Sample(sample);
        }

        var gestures = Gestures ?? new StoredGestures();
        record.Gestures.Merge(gestures.Gestures, gestures.Frames, gestures.IntervalMs, gestures.LateFrames, gestures.WorstLateMs);
    }

    /// <summary>The on-disk shape of the gesture totals.</summary>
    internal sealed class StoredGestures
    {
        /// <summary>How many gestures were delivered.</summary>
        public int Gestures { get; set; }

        /// <summary>How many frames they asked for in total.</summary>
        public int Frames { get; set; }

        /// <summary>How many of those frames missed their slot.</summary>
        public int LateFrames { get; set; }

        /// <summary>The worst lateness seen, in milliseconds.</summary>
        public double WorstLateMs { get; set; }

        /// <summary>The frame interval most recently used, in milliseconds.</summary>
        public int IntervalMs { get; set; }
    }

    /// <summary>The on-disk shape of one counter.</summary>
    internal sealed class StoredCounter
    {
        /// <summary>What is being counted.</summary>
        public DiagnosticKey Key { get; set; } = new(DiagnosticKind.ZoomedNothing, null, null, null);

        /// <summary>How many times it happened.</summary>
        public int Count { get; set; }

        /// <summary>When it first happened.</summary>
        public DateTimeOffset FirstSeen { get; set; }

        /// <summary>When it last happened.</summary>
        public DateTimeOffset LastSeen { get; set; }
    }
}
