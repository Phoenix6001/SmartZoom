namespace SmartZoom.Core.Zoom.Office;

/// <summary>What <see cref="IExcelWindow.ApplyFitToBlockAt"/> found, and the zoom it applied.</summary>
/// <param name="FitZoomPercent">Zoom at which the block's width fills the pane, as Excel computed it.</param>
/// <param name="PaneWidthPx">Width of the worksheet pane in physical pixels, for the margin rule.</param>
/// <param name="Row">Top row of the block (1-based).</param>
/// <param name="Column">Left column of the block (1-based).</param>
/// <param name="Rows">How many rows the block spans.</param>
/// <param name="Columns">How many columns the block spans.</param>
/// <param name="CursorRow">Row of the cell under the cursor, which is what the reader wanted to look at.</param>
public readonly record struct ExcelFit(int FitZoomPercent, int PaneWidthPx, int Row, int Column, int Rows, int Columns, int CursorRow);
