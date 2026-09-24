namespace SmartZoom.Core.Zoom.Content;

/// <summary>
/// Minimum distance from the anchor to the viewport edges, per axis. The plan already keeps the zoomed view
/// inside the viewport; this is for callers that need the anchor itself away from the very edge.
/// </summary>
/// <param name="X">Horizontal keep-out, pixels.</param>
/// <param name="Y">Vertical keep-out, pixels.</param>
public readonly record struct AnchorInsets(int X, int Y);
