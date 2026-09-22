using System.Text.Json;

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
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = true };

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
    public void Save(DiagnosticRecord record)
    {
        try
        {
            var json = JsonSerializer.Serialize(DiagnosticFile.From(record), Json);
            if (json.Length > MaxBytes)
                json = JsonSerializer.Serialize(DiagnosticFile.From(record, withSamples: false), Json);

            Directory.CreateDirectory(Path.GetDirectoryName(paths.DiagnosticsFile)!);
            File.WriteAllText(paths.DiagnosticsFile, json);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            LogUnwritable(ex);
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
    };

    /// <summary>Replays this file's contents into <paramref name="record"/> through its own caps.</summary>
    /// <param name="record">The record to populate.</param>
    public void Restore(DiagnosticRecord record)
    {
        foreach (var stored in Counters)
        {
            // Replayed rather than assigned, so the record's own caps apply to a file that was edited by hand.
            record.Note(stored.Key, stored.FirstSeen);
            for (var i = 1; i < stored.Count; i++)
                record.Note(stored.Key, stored.LastSeen);
        }

        foreach (var sample in Samples)
            record.Sample(sample);
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
