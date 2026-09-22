using SmartZoom.Core.Diagnostics;
using SmartZoom.Core.Zoom;

namespace SmartZoom.App.Diagnostics;

/// <summary>Builds what <see cref="DiagnosticRecorder"/> is given, from a real trigger outcome.</summary>
/// <remarks>
/// This is the code the privacy contract actually lives in. <see cref="DiagnosticKey"/> and
/// <see cref="DiagnosticSample"/> cannot themselves carry a window title, a URL or a coordinate — they have
/// no field for one — so a test against those types alone can never fail. It can only fail here, where a
/// <see cref="ZoomOutcome"/> built from live accessibility data is turned into the sample that gets written
/// to disk: if <see cref="ZoomOutcome.Detail"/> ever started carrying something richer than a role-and-size
/// path shape, a test that feeds this method a real outcome and inspects the result would catch it.
/// </remarks>
internal static class DiagnosticSampleFactory
{
    /// <summary>
    /// Builds the counter key and, when the outcome carries a path shape, the worked example for a
    /// press that resolved to a window and produced no zoom.
    /// </summary>
    /// <param name="outcome">What the trigger did.</param>
    /// <param name="when">When it happened.</param>
    /// <returns>The key to note, and the sample to keep alongside it, if any.</returns>
    public static (DiagnosticKey Key, DiagnosticSample? Sample) ForZoomedNothing(ZoomOutcome outcome, DateTimeOffset when)
    {
        ArgumentNullException.ThrowIfNull(outcome);

        var key = new DiagnosticKey(
            DiagnosticKind.ZoomedNothing,
            outcome.Process,
            outcome.Adapter?.ToString(),
            outcome.Reason?.ToString());

        var sample = outcome.Detail is null ? null : new DiagnosticSample(key, when, outcome.Detail, Exception: null);
        return (key, sample);
    }
}
