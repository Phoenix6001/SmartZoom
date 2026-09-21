using SmartZoom.Core.Zoom.Content;

namespace SmartZoom.Core.Zoom;

/// <summary>
/// What a reader's content area looked like at one moment: the strip of screen that was measured, and the
/// row-brightness profile across it. Two marks are comparable only when they measured the same strip, so the
/// strip travels with the profile.
/// </summary>
/// <param name="Strip">The screen rectangle the profile was taken from.</param>
/// <param name="Profile">One brightness value per row of <paramref name="Strip"/>.</param>
public sealed record ReaderViewMark(PixelRect Strip, ReadOnlyMemory<int> Profile);
