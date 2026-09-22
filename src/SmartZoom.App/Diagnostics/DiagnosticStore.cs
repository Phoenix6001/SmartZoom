using System.Text.Json;
using System.Text.Json.Serialization;

using Microsoft.Extensions.Logging;

using SmartZoom.Core.Diagnostics;

namespace SmartZoom.App.Diagnostics;

/// <summary>Reads and writes the diagnostics record, and never lets a failure reach the caller.</summary>
/// <remarks>
/// Every failure here is swallowed: diagnostics exists to explain a problem, and a diagnostics subsystem that
/// creates one has failed at its only job. A record from another version is discarded rather than merged,
/// because a count that spans a fix cannot answer "is this build broken for you".
/// </remarks>
internal sealed partial class DiagnosticStore(AppPaths paths, ILogger<DiagnosticStore> logger)
{
    // A string-named enum, not the integer JsonSerializerDefaults.Web would write: the file is meant to be
    // readable by the person it describes, which is part of what SECURITY.md asks them to take on trust.
    // Reading still accepts the numbers an older build wrote.
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    // Serialises writers against each other. The 30-second flush timer, a shutdown flush and Crash.Record's
    // synchronous flush can all reach Save at once, and File.WriteAllText opens with FileShare.Read - so
    // without this the loser throws and is swallowed. The crash write is the one with no second chance.
    private readonly Lock _writing = new();

    /// <summary>The maximum size of the file on disk.</summary>
    public const int MaxBytes = 256 * 1024;

    /// <summary>Loads the record for this version, or an empty one.</summary>
    /// <param name="version">The running SmartZoom version.</param>
    /// <returns>A record; never null.</returns>
    public DiagnosticRecord Load(string version)
    {
        try
        {
            if (!File.Exists(paths.DiagnosticsFile))
                return new DiagnosticRecord(version);

            var stored = JsonSerializer.Deserialize<DiagnosticFile>(File.ReadAllText(paths.DiagnosticsFile), Json);
            if (stored is null || stored.Version != version)
                return new DiagnosticRecord(version);

            var record = new DiagnosticRecord(version);
            stored.Restore(record);
            return record;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            LogUnreadable(ex);
            return new DiagnosticRecord(version);
        }
    }

    /// <summary>Writes the record, dropping detail before counters if it is too large.</summary>
    /// <param name="record">The record.</param>
    /// <returns>True when the file was written; false when it could not be.</returns>
    /// <remarks>
    /// Reports success rather than swallowing silently. The caller clears its dirty flag on the strength of
    /// this answer, and a transient failure that looked like a success would discard the whole session's
    /// record: nothing on disk, nothing left to flush at shutdown.
    /// </remarks>
    public bool Save(DiagnosticRecord record)
    {
        try
        {
            var json = JsonSerializer.Serialize(DiagnosticFile.From(record), Json);
            if (json.Length > MaxBytes)
                json = JsonSerializer.Serialize(DiagnosticFile.From(record, withSamples: false), Json);

            Directory.CreateDirectory(Path.GetDirectoryName(paths.DiagnosticsFile)!);
            lock (_writing)
            {
                File.WriteAllText(paths.DiagnosticsFile, json);
            }

            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            LogUnwritable(ex);
            return false;
        }
    }

    [LoggerMessage(Level = LogLevel.Debug, Message = "Diagnostics record could not be read; starting a new one.")]
    private partial void LogUnreadable(Exception ex);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Diagnostics record could not be written.")]
    private partial void LogUnwritable(Exception ex);
}

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
