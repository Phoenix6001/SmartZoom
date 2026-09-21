using SmartZoom.Core.Routing;

namespace SmartZoom.Core.Zoom;

/// <summary>What one trigger did, in enough detail to tell a user without opening the log file.</summary>
/// <param name="Action">What happened.</param>
/// <param name="Process">The application it happened in, or null when the process could not be identified.</param>
/// <param name="Adapter">
/// The strategy that acted, which is not always the one that was routed to: an adapter that cannot act hands
/// over to Ctrl+wheel, and this says who finished the job. Null when nothing was tried.
/// </param>
public sealed record ZoomOutcome(ZoomAction Action, string? Process, AdapterId? Adapter)
{
    /// <summary>Nothing was done, because nothing here is SmartZoom's business.</summary>
    /// <param name="process">The application the trigger landed in.</param>
    public static ZoomOutcome Ignored(string? process) => new(ZoomAction.Ignored, process, null);

    /// <summary>A short sentence for a tooltip or a status line.</summary>
    public override string ToString() => Action switch
    {
        ZoomAction.ZoomedIn => $"Zoomed in {Process} via {Adapter}",
        ZoomAction.ZoomedOut => $"Zoomed out {Process} via {Adapter}",
        ZoomAction.Unhandled => $"Could not zoom {Process}",
        _ => $"Ignored {Process}",
    };
}
