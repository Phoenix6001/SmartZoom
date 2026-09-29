using SmartZoom.Core.Input;
using SmartZoom.Core.Zoom.Content;

namespace SmartZoom.Core.Zoom.Office;

/// <summary>An Excel worksheet window that SmartZoom is attached to.</summary>
/// <remarks>
/// Implementations talk to Excel's object model; every member may throw
/// <see cref="System.Runtime.InteropServices.COMException"/> when the window has closed, or
/// <see cref="TimeoutException"/> when Excel is busy — a modal dialog, or the user part-way through editing
/// a cell — and does not answer at all. Callers treat both as "nothing was zoomed".
/// </remarks>
public interface IExcelWindow : IDisposable
{
    /// <summary>Current zoom and scroll position.</summary>
    ExcelViewState GetState();

    /// <summary>Screen bounds of the worksheet pane in physical pixels, or empty when the grid has gone.</summary>
    PixelRect Pane { get; }

    /// <summary>
    /// Finds the block under a screen point — the surrounding region of contiguous data, or the cells a chart
    /// or picture covers — and reports the zoom at which its width fills the pane. Null when the point is not
    /// over content (column headers, an empty cell, outside the grid).
    /// </summary>
    /// <remarks>
    /// Excel will only tell you a fitting zoom by performing it, so this does zoom, read and undo. The view
    /// and the selection are exactly as they were when it returns, and none of it reaches the screen.
    /// </remarks>
    /// <param name="point">Screen point, physical pixels.</param>
    ExcelFit? MeasureFitAt(ScreenPoint point);

    /// <summary>Sets the zoom and the cell at the top left of the pane together.</summary>
    /// <remarks>
    /// One call, and one repaint. Setting the zoom and then scrolling is two changes to the screen, and the
    /// user sees the sheet move twice for one press.
    /// </remarks>
    /// <param name="zoomPercent">Zoom percentage, 10..400.</param>
    /// <param name="row">Row to show first (1-based).</param>
    /// <param name="column">Column to show first (1-based).</param>
    void ApplyView(int zoomPercent, int row, int column);

    /// <summary>Restores a zoom and scroll position captured with <see cref="GetState"/>, exactly.</summary>
    /// <param name="state">State from <see cref="GetState"/>.</param>
    void Restore(ExcelViewState state);
}
