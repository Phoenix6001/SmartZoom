using SmartZoom.Core.Input;

namespace SmartZoom.Core.Zoom.Office;

/// <summary>An Excel worksheet window that SmartZoom is attached to.</summary>
/// <remarks>
/// Implementations talk to Excel's object model; every member may throw
/// <see cref="System.Runtime.InteropServices.COMException"/> when Excel is busy (a modal dialog, or the user
/// is editing a cell) or the window has closed. Callers treat that as "nothing was zoomed".
/// </remarks>
public interface IExcelWindow : IDisposable
{
    /// <summary>Current zoom and scroll position.</summary>
    ExcelViewState GetState();

    /// <summary>
    /// Finds the block under a screen point — the surrounding region of contiguous data, or the cells a chart
    /// or picture covers — and <em>applies</em> the zoom at which its width fills the pane, reporting what it
    /// did. Null when the point is not over content (column headers, an empty cell, outside the grid).
    /// </summary>
    /// <remarks>
    /// The mutation is in the name because it cannot be avoided: Excel will only tell you a fitting zoom by
    /// performing it. Applying it once is better than showing the user an intermediate value. The caller
    /// clamps afterwards with <see cref="SetZoom"/>, or puts the view back with <see cref="Restore"/> when it
    /// decides not to zoom at all.
    /// </remarks>
    /// <param name="point">Screen point, physical pixels.</param>
    ExcelFit? ApplyFitToBlockAt(ScreenPoint point);

    /// <summary>Sets the view zoom.</summary>
    /// <param name="percent">Zoom percentage, 10..400.</param>
    void SetZoom(int percent);

    /// <summary>Scrolls so a cell sits at the top left of the pane.</summary>
    /// <param name="row">Row to show first (1-based).</param>
    /// <param name="column">Column to show first (1-based).</param>
    void ScrollTo(int row, int column);

    /// <summary>Restores a zoom and scroll position captured with <see cref="GetState"/>, exactly.</summary>
    /// <param name="state">State from <see cref="GetState"/>.</param>
    void Restore(ExcelViewState state);
}
