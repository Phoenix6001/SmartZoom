using SmartZoom.Core.Input;

namespace SmartZoom.Core.Windows;

/// <summary>The scale factor of the display showing a point on the desktop.</summary>
public interface IDisplayScale
{
    /// <summary>The scale of the display showing the point: 1.0 at 100%, 2.0 at 200%.</summary>
    /// <param name="point">A point on the desktop, in physical pixels.</param>
    /// <returns>1.0 when the display does not say; that means "not reported", not "confirmed 100%".</returns>
    double ScaleAt(ScreenPoint point);
}
