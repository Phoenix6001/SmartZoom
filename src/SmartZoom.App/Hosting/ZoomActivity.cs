using SmartZoom.Core.Zoom;

namespace SmartZoom.App.Hosting;

/// <summary>
/// The last thing SmartZoom did, so the tray can say so. Without it a user has no way to tell what happened —
/// whether the press was seen at all, which application it reached, and which strategy handled it — short of
/// opening a log file.
/// </summary>
internal sealed class ZoomActivity
{
    /// <summary>Raised on the dispatcher's thread after every trigger that reached an application.</summary>
    public event EventHandler<ZoomOutcome>? Happened;

    /// <summary>The most recent outcome, or null before the first trigger.</summary>
    public ZoomOutcome? Last { get; private set; }

    /// <summary>Records an outcome and tells whoever is listening.</summary>
    /// <param name="outcome">What the trigger did.</param>
    public void Report(ZoomOutcome outcome)
    {
        Last = outcome;
        Happened?.Invoke(this, outcome);
    }
}
