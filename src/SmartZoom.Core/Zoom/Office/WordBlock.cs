using SmartZoom.Core.Zoom.Content;

namespace SmartZoom.Core.Zoom.Office;

/// <summary>A reading unit in a Word document: where it is on screen, and where it is in the text.</summary>
/// <param name="Bounds">Screen bounds in physical pixels, at the zoom that was in force when it was found.</param>
/// <param name="Start">
/// Character position of the block in the document. This is what survives a zoom: the bounds do not, because
/// Word re-lays the document out, and the screen point the block was found at then belongs to different text
/// entirely.
/// </param>
public readonly record struct WordBlock(PixelRect Bounds, int Start);
