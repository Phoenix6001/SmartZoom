namespace SmartZoom.Core.Settings;

/// <summary>Zoom behavior shared by all adapters.</summary>
public sealed class ZoomSettings
{
    /// <summary>
    /// Smallest zoom worth the motion, for the strategies that compute a fit of their own (Word and Excel).
    /// Browsers zoom by <see cref="MaxScale"/> wherever the press lands and never consult this.
    /// </summary>
    public double MinScale { get; set; } = 1.1;

    /// <summary>
    /// How much a press zooms. The tray's <c>Zoom amount</c> sets this, and in a browser it is the amount
    /// exactly: the same everywhere on the page, whatever the cursor happens to be over. Word and Excel,
    /// which fit the block they can identify, treat it as the ceiling on that fit and as the amount when
    /// there is no fit worth having.
    /// </summary>
    public double MaxScale { get; set; } = 3.0;

    /// <summary>Animate zoom transitions where the adapter supports it.</summary>
    public bool Animate { get; set; } = true;

    /// <summary>When a richer adapter can't act, fall back to a crude Ctrl+wheel zoom rather than doing nothing.</summary>
    public bool FallbackToCtrlWheel { get; set; } = true;

    /// <summary>Tuning for the generic Ctrl+wheel adapter.</summary>
    public CtrlWheelSettings CtrlWheel { get; set; } = new();

    /// <summary>Tuning shared by every element-aware zoom.</summary>
    public SmartZoomTuning Smart { get; set; } = new();

    /// <summary>Tuning that only browsers need.</summary>
    public BrowserZoomSettings Browser { get; set; } = new();

    /// <summary>How PDF readers and other document viewers are zoomed.</summary>
    public ReaderZoomSettings Reader { get; set; } = new();
}
