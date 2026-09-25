namespace SmartZoom.Core.Zoom.Content;

/// <summary>Result of hit-testing a point in an application's content.</summary>
/// <param name="Chain">Ancestor chain, leaf first, ending with the <see cref="ContentRole.Document"/> node.</param>
/// <param name="Viewport">The visible content area in physical screen pixels.</param>
/// <param name="PageScale">
/// The pinch zoom the page is already showing, or 1 when it is at rest or the hit tester cannot tell. Above 1
/// means a zoom is on the screen that this process did not put there or no longer remembers.
/// </param>
public sealed record ContentHit(IReadOnlyList<ContentNode> Chain, PixelRect Viewport, double PageScale = 1);
