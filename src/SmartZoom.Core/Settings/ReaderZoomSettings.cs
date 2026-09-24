namespace SmartZoom.Core.Settings;

/// <summary>
/// How a document reader is zoomed: by default an animated pinch around the cursor, with the reader's own
/// commands to land on. Defaults are Acrobat's and Sumatra's fit width and fit page.
/// </summary>
public sealed class ReaderZoomSettings
{
    /// <summary>Combination sent on the first press; by default "fit width", which fills the window with the page.</summary>
    public string ZoomInKeys { get; set; } = "Ctrl+2";

    /// <summary>Combination sent on the second press; by default "fit page", which shows the whole page again.</summary>
    public string ZoomOutKeys { get; set; } = "Ctrl+0";

    /// <summary>
    /// Which of the two reader strategies to use: an animated pinch around the cursor, or the reader's own
    /// fit-width and fit-page shortcuts. Only one of them is registered, decided here at startup.
    /// </summary>
    public ReaderZoomMode Mode { get; set; } = ReaderZoomMode.Pinch;

    /// <summary>
    /// How much the pinch magnifies. Must be above 1; unused in <see cref="ReaderZoomMode.Shortcuts"/>.
    /// Unlike <see cref="ZoomSettings.MinScale"/> and <see cref="ZoomSettings.MaxScale"/>, which bound a
    /// computed fit, this is a fixed factor and they do not clamp it.
    /// </summary>
    public double Magnification { get; set; } = 2.0;

    /// <summary>
    /// In <see cref="ReaderZoomMode.Shortcuts"/>: scroll the block under the cursor to the top of the reader
    /// before zooming, so the first press magnifies what you pointed at rather than wherever the reader
    /// happened to be. The second press scrolls back.
    /// </summary>
    public bool FollowCursor { get; set; } = true;

    /// <summary>Gap left above the cursor's content when <see cref="FollowCursor"/> scrolls it to the top.</summary>
    public int TopGapPx { get; set; } = 16;

    /// <summary>How long the gesture takes, in milliseconds.</summary>
    public int AnimationMs { get; set; } = 300;
}
