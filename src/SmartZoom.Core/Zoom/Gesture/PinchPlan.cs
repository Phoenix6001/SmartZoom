using SmartZoom.Core.Input;

namespace SmartZoom.Core.Zoom.Gesture;

/// <summary>
/// Where to put two synthetic fingers and how far to move them, for one pinch. Everything the injector has
/// to decide, decided; what is left over there is Win32.
/// </summary>
/// <param name="Focus">Midpoint of the two contacts, which is the point the zoom keeps still.</param>
/// <param name="Vertical">Whether the contacts are spread up and down rather than left and right.</param>
/// <param name="DownHalf">Half the span when the fingers first touch down.</param>
/// <param name="PreRolledHalf">Half the span after the invisible step that crosses the recognizer's slop.</param>
/// <param name="EndHalf">Half the span at the end of the visible animation.</param>
/// <param name="Pan">
/// How far to drag the content afterwards, and (0, 0) when no drag is needed. A pinch around a substitute
/// focus leaves the content offset by this much from where a pinch around the requested anchor would have
/// left it.
/// </param>
/// <param name="NarrowedHalfGap">
/// The reduced half gap used because the full spread did not fit around the anchor, or null if it did.
/// </param>
/// <param name="Shortfall">What was missing when even the middle of the content area was too small, or null.</param>
public sealed record PinchPlan(
    ScreenPoint Focus,
    bool Vertical,
    double DownHalf,
    double PreRolledHalf,
    double EndHalf,
    ScreenPoint Pan,
    double? NarrowedHalfGap,
    PinchShortfall? Shortfall);
