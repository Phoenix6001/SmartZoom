namespace SmartZoom.Core.Diagnostics;

/// <summary>
/// Running totals for how well injected gestures were delivered.
/// </summary>
/// <remarks>
/// Totals rather than one row per gesture: the question is whether this machine can deliver frames on time
/// at all, which a hundred rows answer no better than four numbers. The frame interval follows the display's
/// refresh rate, so it belongs beside the display section rather than as a constant in the source.
/// </remarks>
public sealed class GestureHealth
{
    /// <summary>Creates an empty tally.</summary>
    public GestureHealth()
    {
    }

    /// <summary>Creates an independent copy of <paramref name="source"/>.</summary>
    /// <param name="source">The tally to copy.</param>
    /// <remarks>Used to build a point-in-time snapshot that cannot be affected by later mutation of the original.</remarks>
    public GestureHealth(GestureHealth source)
    {
        ArgumentNullException.ThrowIfNull(source);

        Gestures = source.Gestures;
        Frames = source.Frames;
        LateFrames = source.LateFrames;
        WorstLateMs = source.WorstLateMs;
        IntervalMs = source.IntervalMs;
    }

    /// <summary>How many gestures have been delivered.</summary>
    public int Gestures { get; private set; }

    /// <summary>How many frames those gestures asked for in total.</summary>
    public int Frames { get; private set; }

    /// <summary>How many of those frames missed their slot.</summary>
    public int LateFrames { get; private set; }

    /// <summary>The worst lateness seen, in milliseconds.</summary>
    public double WorstLateMs { get; private set; }

    /// <summary>The frame interval most recently used, in milliseconds.</summary>
    public int IntervalMs { get; private set; }

    /// <summary>Records one delivered gesture.</summary>
    /// <param name="frames">Frames the gesture asked for.</param>
    /// <param name="intervalMs">The interval between them.</param>
    /// <param name="lateFrames">How many missed their slot.</param>
    /// <param name="worstLateMs">The worst lateness in this gesture.</param>
    public void Add(int frames, int intervalMs, int lateFrames, double worstLateMs)
    {
        Gestures++;
        Frames += frames;
        LateFrames += lateFrames;
        IntervalMs = intervalMs;
        WorstLateMs = Math.Max(WorstLateMs, worstLateMs);
    }
}
