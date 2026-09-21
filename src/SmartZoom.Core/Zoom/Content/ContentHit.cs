namespace SmartZoom.Core.Zoom.Content;

/// <summary>Result of hit-testing a point in an application's content.</summary>
/// <param name="Chain">Ancestor chain, leaf first, ending with the <see cref="ContentRole.Document"/> node.</param>
/// <param name="Viewport">The visible content area in physical screen pixels.</param>
public sealed record ContentHit(IReadOnlyList<ContentNode> Chain, PixelRect Viewport);
