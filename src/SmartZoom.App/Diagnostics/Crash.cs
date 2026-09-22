using SmartZoom.Core.Diagnostics;

namespace SmartZoom.App.Diagnostics;

/// <summary>Writes a crash to the record before the process goes.</summary>
/// <remarks>
/// Flushed here and now rather than left to the timer: the timer will not run again. Every failure is
/// swallowed, because throwing out of a crash handler replaces a diagnosable crash with an undiagnosable one.
/// </remarks>
internal static class Crash
{
    /// <summary>Records an unhandled exception and flushes it to disk immediately.</summary>
    /// <param name="recorder">The diagnostics recorder.</param>
    /// <param name="ex">The exception that is about to take the process down.</param>
    public static void Record(DiagnosticRecorder recorder, Exception ex)
    {
        try
        {
            var key = new DiagnosticKey(DiagnosticKind.Crashed, null, null, ex.GetType().Name);
            recorder.Note(key);
            recorder.Sample(new DiagnosticSample(
                key,
                DateTimeOffset.UtcNow,
                Detail: null,
                Exception: Redaction.Truncate(
                    $"{ex.GetType().FullName}: {ex.Message}{Environment.NewLine}{ex.StackTrace}", 4000)));
            recorder.Flush();
        }
        catch
        {
            // There is nowhere left to report a failure to report a failure.
        }
    }
}
