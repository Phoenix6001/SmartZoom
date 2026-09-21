using SmartZoom.Core.Input;

namespace SmartZoom.Core.Zoom.Gesture;

/// <summary>One leg of a one-finger drag, already placed so that it and its slop stay inside the bounds.</summary>
/// <param name="From">Where the finger touches down.</param>
/// <param name="To">Where it lifts.</param>
public sealed record PanLeg(ScreenPoint From, ScreenPoint To);
