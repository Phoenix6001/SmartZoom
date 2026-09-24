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

    /// <summary>
    /// When a trigger last reached SmartZoom at all, or null if none ever has.
    /// </summary>
    /// <remarks>
    /// Recorded before anything is zoomed, because the question this answers is "did the press get here?" —
    /// which is exactly what cannot be told apart from "SmartZoom is not running" when a trigger silently never
    /// arrives. Vendor mouse software that remaps a side button per application is the usual cause, and it
    /// leaves no trace anywhere else.
    /// </remarks>
    public DateTimeOffset? LastTrigger { get; private set; }

    /// <summary>What the last trigger did, or null if none ever reached an application.</summary>
    /// <remarks>
    /// Kept here rather than only in the event, because the tray panel is built after the fact: it opens long
    /// after the zoom happened and still has to name the application it was in.
    /// </remarks>
    public ZoomOutcome? LastOutcome { get; private set; }

    /// <summary>When <see cref="LastOutcome"/> happened.</summary>
    public DateTimeOffset? LastOutcomeAt { get; private set; }

    /// <summary>Records that a trigger arrived, before anything has been done with it.</summary>
    /// <param name="when">The time it arrived.</param>
    public void Seen(DateTimeOffset when) => LastTrigger = when;

    /// <summary>Tells whoever is listening what the trigger did, and remembers it for whoever asks later.</summary>
    /// <param name="outcome">What the trigger did.</param>
    public void Report(ZoomOutcome outcome)
    {
        LastOutcome = outcome;
        LastOutcomeAt = DateTimeOffset.UtcNow;
        Happened?.Invoke(this, outcome);
    }
}
