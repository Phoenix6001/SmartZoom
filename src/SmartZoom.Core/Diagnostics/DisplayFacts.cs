namespace SmartZoom.Core.Diagnostics;

/// <summary>One display. Refresh rate is here because gesture pacing follows it.</summary>
/// <param name="Width">Width in physical pixels.</param>
/// <param name="Height">Height in physical pixels.</param>
/// <param name="RefreshHz">Refresh rate in hertz, or 0 when the display did not report one.</param>
/// <param name="Scale">Scale factor, where 2.0 is 200%.</param>
/// <param name="Primary">Whether this is the primary display.</param>
public sealed record DisplayFacts(int Width, int Height, int RefreshHz, double Scale, bool Primary);
