using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;
using SmartZoom.Core.Input;
using SmartZoom.Core.Routing;
using SmartZoom.Core.Settings;

namespace SmartZoom.Core.Zoom.Office;

/// <summary>
/// Smart zoom for Microsoft Excel through its object model: the block of data under the cursor — the
/// surrounding region of filled cells, or the cells a chart covers — is zoomed to fill the worksheet pane
/// and scrolled to the top left, and the previous zoom and scroll position come back exactly.
/// </summary>
/// <remarks>
/// Excel is the one host that computes the fitting zoom for us (its "zoom to selection"), which is why the
/// adapter asks the window to fit the block first and only then decides whether to keep, clamp or undo it.
/// Doing the arithmetic here instead would mean converting between Excel's points, logical pixels and
/// physical pixels, and getting the row headers and scrollbar right on every display scale.
/// </remarks>
public sealed partial class ExcelComAdapter : IZoomAdapter
{
    private const int MinExcelZoom = 10;
    private const int MaxExcelZoom = 400;

    /// <summary>Rows kept above the cell under the cursor, so it does not sit against the top of the pane.</summary>
    private const int ContextRows = 2;

    private readonly IExcelAutomation _excel;
    private readonly double _minScale;
    private readonly double _maxScale;
    private readonly int _margin;
    private readonly ILogger<ExcelComAdapter> _logger;

    /// <summary>Creates the adapter.</summary>
    /// <param name="excel">Excel automation.</param>
    /// <param name="zoom">Zoom limits and margin.</param>
    /// <param name="logger">Logger.</param>
    public ExcelComAdapter(IExcelAutomation excel, ZoomSettings zoom, ILogger<ExcelComAdapter> logger)
    {
        ArgumentNullException.ThrowIfNull(zoom);

        _excel = excel ?? throw new ArgumentNullException(nameof(excel));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _minScale = zoom.MinScale;
        _maxScale = zoom.MaxScale;
        _margin = zoom.Browser.MarginPx;
    }

    /// <inheritdoc />
    public AdapterKind Kind => AdapterKind.ExcelCom;

    /// <inheritdoc />
    public async Task<ZoomInResult> ZoomInAsync(TargetInfo target, ScreenPoint point, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(target);

        using var window = await _excel.AttachAsync(target, cancellationToken).ConfigureAwait(false);
        if (window is null)
            return ZoomInResult.Unhandled;

        var before = default(ExcelViewState);
        try
        {
            before = window.GetState();

            var found = window.TryFitBlockAt(point);
            if (found is not { } block)
            {
                LogNoBlock(target.ProcessName);
                return ZoomInResult.SelfManaged;
            }

            // Leave the same gap at the sides as the browsers and Word do.
            var fit = block.PaneWidthPx > 2 * _margin
                ? block.FitZoomPercent * (block.PaneWidthPx - (2.0 * _margin)) / block.PaneWidthPx
                : block.FitZoomPercent;

            var scale = fit / before.ZoomPercent;
            if (scale < _minScale)
            {
                window.Restore(before);
                LogAlreadyFits(target.ProcessName, block.Rows, block.Columns);
                return ZoomInResult.SelfManaged;
            }

            var targetZoom = Math.Clamp((int)Math.Round(before.ZoomPercent * Math.Min(scale, _maxScale)), MinExcelZoom, MaxExcelZoom);
            if (targetZoom != block.FitZoomPercent)
                window.SetZoom(targetZoom);

            // Scroll to the cursor's own row, not the top of the block: a table taller than the pane would
            // otherwise magnify the cell that was asked about and then leave it below the bottom of the window.
            // The column is the block's, because fitting the width already brings the whole of it into view.
            var row = Math.Max(block.Row, block.CursorRow - ContextRows);
            window.ScrollTo(row, block.Column);
            LogPlan(block.Rows, block.Columns, before.ZoomPercent, targetZoom);
            return ZoomInResult.Applied(before);
        }
        catch (COMException ex)
        {
            LogComFailure(ex, target.ProcessName);
            TryRestore(window, before);
            return ZoomInResult.SelfManaged;
        }
    }

    /// <inheritdoc />
    public async Task ZoomOutAsync(TargetInfo target, object restoreState, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(target);
        if (restoreState is not ExcelViewState state)
            throw new ArgumentException($"Expected {nameof(ExcelViewState)} from a previous zoom-in.", nameof(restoreState));

        using var window = await _excel.AttachAsync(target, cancellationToken).ConfigureAwait(false);
        if (window is null)
            return;

        try
        {
            window.Restore(state);
        }
        catch (COMException ex)
        {
            LogComFailure(ex, target.ProcessName);
        }
    }

    // Best-effort undo after a failure part-way through: the window may already be at the fitting zoom.
    private static void TryRestore(IExcelWindow window, ExcelViewState state)
    {
        if (state.ZoomPercent == 0)
            return;

        try
        {
            window.Restore(state);
        }
        catch (COMException)
        {
            // Excel is still unwell; the user's next trigger will set the zoom anyway.
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Smart zoom unavailable: no data, chart or picture under the cursor in {Process}. Nothing was zoomed.")]
    private partial void LogNoBlock(string? process);

    [LoggerMessage(Level = LogLevel.Debug, Message = "The {Rows}x{Columns} block under the cursor already fills the pane in {Process}; nothing to zoom.")]
    private partial void LogAlreadyFits(string? process, int rows, int columns);

    [LoggerMessage(Level = LogLevel.Information, Message = "Smart zoom (Excel): block of {Rows} rows x {Columns} columns -> zoom {From}% to {To}%.")]
    private partial void LogPlan(int rows, int columns, int from, int to);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Excel's object model refused the request in {Process} (busy, editing a cell, or the window closed); nothing was zoomed.")]
    private partial void LogComFailure(Exception exception, string? process);
}
