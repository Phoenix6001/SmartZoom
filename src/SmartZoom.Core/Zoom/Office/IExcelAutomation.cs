using SmartZoom.Core.Input;
using SmartZoom.Core.Routing;

namespace SmartZoom.Core.Zoom.Office;

/// <summary>Zoom and scroll position of an Excel worksheet window, as Excel reports them.</summary>
/// <param name="ZoomPercent">View zoom, 10..400.</param>
/// <param name="ScrollRow">Row shown at the top of the pane (1-based).</param>
/// <param name="ScrollColumn">Column shown at the left of the pane (1-based).</param>
public readonly record struct ExcelViewState(int ZoomPercent, int ScrollRow, int ScrollColumn);

/// <summary>What <see cref="IExcelWindow.TryFitBlockAt"/> found and did.</summary>
/// <param name="FitZoomPercent">Zoom at which the block's width fills the pane, as Excel computed it.</param>
/// <param name="PaneWidthPx">Width of the worksheet pane in physical pixels, for the margin rule.</param>
/// <param name="Row">Top row of the block (1-based).</param>
/// <param name="Column">Left column of the block (1-based).</param>
/// <param name="Rows">How many rows the block spans.</param>
/// <param name="Columns">How many columns the block spans.</param>
/// <param name="CursorRow">Row of the cell under the cursor, which is what the reader wanted to look at.</param>
public readonly record struct ExcelBlock(int FitZoomPercent, int PaneWidthPx, int Row, int Column, int Rows, int Columns, int CursorRow);

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
    /// or picture covers — and zooms so its width fills the pane, reporting what it did. Null when the point is
    /// not over content (column headers, an empty cell, outside the grid).
    /// </summary>
    /// <remarks>
    /// The zoom really is applied: Excel only computes a fitting zoom by performing it, and applying it once is
    /// better than showing the user an intermediate value. The caller clamps afterwards with
    /// <see cref="SetZoom"/>, or puts the view back with <see cref="Restore"/> when it decides not to zoom.
    /// </remarks>
    /// <param name="point">Screen point, physical pixels.</param>
    ExcelBlock? TryFitBlockAt(ScreenPoint point);

    /// <summary>Sets the view zoom.</summary>
    /// <param name="percent">Zoom percentage, 10..400.</param>
    void SetZoom(int percent);

    /// <summary>Scrolls so a cell sits at the top left of the pane.</summary>
    /// <param name="row">Row to show first (1-based).</param>
    /// <param name="column">Column to show first (1-based).</param>
    void ScrollTo(int row, int column);

    /// <summary>Restores a previously captured zoom and scroll position exactly.</summary>
    /// <param name="state">State from <see cref="GetState"/>.</param>
    void Restore(ExcelViewState state);
}

/// <summary>Attaches to Excel worksheet windows.</summary>
public interface IExcelAutomation
{
    /// <summary>Attaches to the Excel worksheet window under the cursor, or returns null if the target isn't one.</summary>
    /// <param name="target">The window the trigger landed on.</param>
    /// <param name="cancellationToken">Cancellation.</param>
    Task<IExcelWindow?> AttachAsync(TargetInfo target, CancellationToken cancellationToken);
}
