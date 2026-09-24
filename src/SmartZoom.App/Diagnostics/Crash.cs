using SmartZoom.Core.Diagnostics;

namespace SmartZoom.App.Diagnostics;

/// <summary>Writes a crash to the record before the process goes.</summary>
/// <remarks>
/// Flushed here and now rather than left to the timer: the timer will not run again. Every failure is
/// swallowed, because throwing out of a crash handler replaces a diagnosable crash with an undiagnosable one.
/// </remarks>
internal static class Crash
{
    /// <summary>
    /// The slot that marks an unhandled exception on the UI thread, which the process survives. Kept apart in
    /// the record so a report can tell those from the crashes that took the process down.
    /// </summary>
    public const string UiThread = "UiThread";

    /// <summary>Records an unhandled exception and flushes it to disk immediately.</summary>
    /// <param name="recorder">The diagnostics recorder.</param>
    /// <param name="ex">The exception nothing else caught.</param>
    /// <param name="thread">
    /// <see cref="UiThread"/> for an exception the process survives; null for one that is about to take it down.
    /// </param>
    public static void Record(DiagnosticRecorder recorder, Exception ex, string? thread = null)
    {
        try
        {
            var key = new DiagnosticKey(DiagnosticKind.Crashed, null, thread, ex.GetType().Name);
            recorder.Note(key);
            recorder.Sample(new DiagnosticSample(
                key,
                DateTimeOffset.UtcNow,
                Detail: null,
                Exception: DiagnosticText.ForException(ex)));
            recorder.Flush();
        }
        catch
        {
            // There is nowhere left to report a failure to report a failure.
        }
    }
}
