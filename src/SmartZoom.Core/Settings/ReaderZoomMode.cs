namespace SmartZoom.Core.Settings;

/// <summary>How a document reader is zoomed.</summary>
public enum ReaderZoomMode
{
    /// <summary>
    /// An animated pinch around the cursor, the way browsers are zoomed: what you pointed at stays where it
    /// is and grows. Needs a reader that accepts touch input, which Acrobat and Sumatra do.
    /// </summary>
    Pinch = 0,

    /// <summary>
    /// The reader's own fit-width and fit-page commands, with the cursor's content scrolled to the top
    /// first. For readers that ignore touch, where the pinch would do nothing.
    /// </summary>
    Shortcuts,
}
