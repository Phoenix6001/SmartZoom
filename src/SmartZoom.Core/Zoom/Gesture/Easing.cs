namespace SmartZoom.Core.Zoom.Gesture;

/// <summary>Shared easing for animated zooms, so a gesture and an object-model zoom move the same way.</summary>
public static class Easing
{
    /// <summary>Ease in and out: 0 and 1 stay put, the middle moves fastest.</summary>
    /// <param name="t">Progress from 0 to 1.</param>
    public static double SmoothStep(double t) => t * t * (3 - (2 * t));
}
