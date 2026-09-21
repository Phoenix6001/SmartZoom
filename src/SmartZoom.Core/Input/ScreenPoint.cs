namespace SmartZoom.Core.Input;

/// <summary>A point in physical (per-monitor, unscaled) screen pixels, as reported by the low-level hook.</summary>
/// <param name="X">Horizontal position in physical pixels on the virtual screen.</param>
/// <param name="Y">Vertical position in physical pixels on the virtual screen.</param>
public readonly record struct ScreenPoint(int X, int Y);
