using SmartZoom.Core.Input;

namespace SmartZoom.Core.Zoom.Content;

/// <summary>How to zoom: scale the view by <see cref="Scale"/> around <see cref="Anchor"/>, which stays fixed on screen.</summary>
/// <param name="Scale">Zoom factor, greater than 1.</param>
/// <param name="Anchor">Screen point that does not move during the zoom, in physical pixels.</param>
public readonly record struct ZoomPlan(double Scale, ScreenPoint Anchor);
