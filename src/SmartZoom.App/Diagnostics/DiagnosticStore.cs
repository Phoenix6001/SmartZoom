using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

using Microsoft.Extensions.Logging;

using SmartZoom.Core.Diagnostics;

namespace SmartZoom.App.Diagnostics;

/// <summary>Reads and writes the diagnostics record, and never lets a failure reach the caller.</summary>
/// <remarks>
/// Every failure here is swallowed: diagnostics exists to explain a problem, and a diagnostics subsystem that
/// creates one has failed at its only job. A record from another version is discarded rather than merged,
/// because a count that spans a fix cannot answer "is this build broken for you". Writers are serialised by
/// <see cref="DiagnosticRecorder.Flush"/>, the only caller of <see cref="Save"/>.
/// </remarks>
internal sealed partial class DiagnosticStore(AppPaths paths, ILogger<DiagnosticStore> logger)
{
    // A string-named enum, not the integer JsonSerializerDefaults.Web would write: the file is meant to be
    // readable by the person it describes, which is part of what SECURITY.md asks them to take on trust.
    // Reading still accepts the numbers an older build wrote. Non-ASCII text (a process name, an exception
    // message) is written as itself rather than as \uXXXX escapes, for the same reader: the file is local and
    // is never handed to a browser or another parser that the relaxed escaping would matter to.
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

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

            // The ceiling is on the file, so it is measured in the bytes the file will hold, not in characters.
            if (Encoding.UTF8.GetByteCount(json) > MaxBytes)
                json = JsonSerializer.Serialize(DiagnosticFile.From(record, withSamples: false), Json);

            Directory.CreateDirectory(Path.GetDirectoryName(paths.DiagnosticsFile)!);
            File.WriteAllText(paths.DiagnosticsFile, json);
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
