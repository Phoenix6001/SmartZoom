namespace SmartZoom.Core.Zoom.Gesture;

/// <summary>A pinch that had to be shrunk because the content area could not hold it.</summary>
/// <param name="NeededHalfSpread">Half the span the gesture wanted.</param>
/// <param name="Room">Half the span the content area could offer.</param>
public sealed record PinchShortfall(double NeededHalfSpread, double Room);
