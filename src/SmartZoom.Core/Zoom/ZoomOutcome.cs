using SmartZoom.Core.Routing;

namespace SmartZoom.Core.Zoom;

/// <summary>What one trigger did, in enough detail to tell a user without opening the log file.</summary>
/// <param name="Action">What happened.</param>
/// <param name="Process">The application it happened in, or null when the process could not be identified.</param>
/// <param name="Adapter">
/// The strategy that acted, which is not always the one that was routed to: an adapter that cannot act hands
/// over to Ctrl+wheel, and this says who finished the job. Null when nothing was tried.
/// </param>
/// <param name="Reason">
/// Why nothing was zoomed. Always set when <see cref="Action"/> is <see cref="ZoomAction.Handled"/>,
/// <see cref="ZoomAction.Unhandled"/> or <see cref="ZoomAction.Ignored"/> — those are the three the
/// diagnostics record counts, and a counter whose reason is missing cannot answer why the press did nothing.
/// Null when a zoom actually happened.
/// </param>
/// <param name="Detail">
/// A privacy-safe description of what was under the cursor, when one is available. Never a title, a URL
/// or a coordinate — see <see cref="Content.ContentPath.Shape"/>, the only intended source of this value.
/// </param>
public sealed record ZoomOutcome(ZoomAction Action, string? Process, AdapterId? Adapter, ZoomReason? Reason = null, string? Detail = null)
{
    /// <summary>A short sentence for a tooltip or a status line.</summary>
    public override string ToString() => Action switch
    {
        ZoomAction.ZoomedIn => $"Zoomed in {Process} via {Adapter}",
        ZoomAction.ZoomedOut => $"Zoomed out {Process} via {Adapter}",
        ZoomAction.Handled => $"Nothing to zoom in {Process} ({Reason})",
        ZoomAction.Unhandled => $"Could not zoom {Process}",
        _ => $"Ignored {Process}",
    };
}
