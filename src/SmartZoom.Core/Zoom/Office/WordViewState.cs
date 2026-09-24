namespace SmartZoom.Core.Zoom.Office;

/// <summary>Zoom and scroll position of a Word document window, as Word reports them.</summary>
/// <param name="ZoomPercent">View zoom, 10..500.</param>
/// <param name="VerticalPercent">Vertical scroll position, 0..100.</param>
/// <param name="HorizontalPercent">Horizontal scroll position, 0..100.</param>
public readonly record struct WordViewState(int ZoomPercent, int VerticalPercent, int HorizontalPercent);
