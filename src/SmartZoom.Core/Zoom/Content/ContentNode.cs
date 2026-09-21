namespace SmartZoom.Core.Zoom.Content;

/// <summary>One node on the path from the element under the cursor up to the document.</summary>
/// <param name="Role">Normalized role.</param>
/// <param name="Bounds">Bounds in physical screen pixels (may extend beyond the viewport).</param>
public sealed record ContentNode(ContentRole Role, PixelRect Bounds);
