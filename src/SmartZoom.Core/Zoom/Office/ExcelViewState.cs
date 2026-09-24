namespace SmartZoom.Core.Zoom.Office;

/// <summary>Zoom and scroll position of an Excel worksheet window, as Excel reports them.</summary>
/// <param name="ZoomPercent">View zoom, 10..400.</param>
/// <param name="ScrollRow">Row shown at the top of the pane (1-based).</param>
/// <param name="ScrollColumn">Column shown at the left of the pane (1-based).</param>
public readonly record struct ExcelViewState(int ZoomPercent, int ScrollRow, int ScrollColumn);
