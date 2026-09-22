using Microsoft.Extensions.Logging.Abstractions;

using SmartZoom.Core.Input;
using SmartZoom.Core.Routing;
using SmartZoom.Core.Settings;
using SmartZoom.Core.Zoom;
using SmartZoom.Core.Zoom.Office;

namespace SmartZoom.Core.Tests.Zoom.Office;

public sealed class ExcelComAdapterTests
{
    private static readonly TargetInfo Excel = new(0x400, 0x401, 21, "EXCEL", "XLMAIN", "EXCEL7");
    private static readonly ScreenPoint Cursor = new(800, 600);

    private readonly FakeExcel _excel = new();

    // A 1000 px pane and a 32 px margin leave 96.8% of the width, so a 300% fit is kept as 290%.
    private IZoomAdapter Create(double maxScale = 3.0, double minScale = 1.1) =>
        new ExcelComAdapter(_excel, new ZoomSettings { MinScale = minScale, MaxScale = maxScale }, NullLogger<ExcelComAdapter>.Instance);

    [Fact]
    public async Task Zoom_in_keeps_the_fitting_zoom_and_scrolls_the_block_to_the_top_left()
    {
        _excel.Window.Block = new ExcelFit(FitZoomPercent: 200, PaneWidthPx: 1000, Row: 3, Column: 1, Rows: 38, Columns: 10, CursorRow: 3);

        var result = await Create().ZoomInAsync(Excel, Cursor, CancellationToken.None);

        Assert.Equal(ZoomInStatus.Applied, result.Status);
        Assert.Equal(new ExcelViewState(100, 1, 1), result.RestoreState);
        Assert.Equal(194, _excel.Window.Zoom); // 200% less the 16 px margin either side of a 1000 px pane
        Assert.Equal((3, 1), _excel.Window.Scroll);
    }

    [Fact]
    public async Task The_view_is_scrolled_to_the_row_under_the_cursor_not_the_top_of_the_block()
    {
        // A table taller than the pane: magnifying row 36 and then showing row 3 would hide what was asked about.
        _excel.Window.Block = new ExcelFit(200, 1000, Row: 3, Column: 1, Rows: 38, Columns: 10, CursorRow: 36);

        await Create().ZoomInAsync(Excel, Cursor, CancellationToken.None);

        Assert.Equal((34, 1), _excel.Window.Scroll);       // two rows of context above the cursor's row
    }

    [Fact]
    public async Task Framing_never_scrolls_above_the_block_itself()
    {
        _excel.Window.Block = new ExcelFit(200, 1000, Row: 3, Column: 1, Rows: 38, Columns: 10, CursorRow: 4);

        await Create().ZoomInAsync(Excel, Cursor, CancellationToken.None);

        Assert.Equal((3, 1), _excel.Window.Scroll);
    }

    [Fact]
    public async Task Fitting_zoom_beyond_the_maximum_scale_is_clamped()
    {
        _excel.Window.State = new ExcelViewState(100, 1, 1);
        _excel.Window.Block = new ExcelFit(1200, 1000, 5, 2, 1, 1, 5);

        await Create(maxScale: 3.0).ZoomInAsync(Excel, Cursor, CancellationToken.None);

        Assert.Equal(300, _excel.Window.Zoom);
    }

    [Fact]
    public async Task Zoom_never_exceeds_what_Excel_accepts()
    {
        _excel.Window.State = new ExcelViewState(200, 1, 1);
        _excel.Window.Block = new ExcelFit(1200, 1000, 5, 2, 1, 1, 5);

        await Create(maxScale: 8.0).ZoomInAsync(Excel, Cursor, CancellationToken.None);

        Assert.Equal(400, _excel.Window.Zoom);
    }

    [Fact]
    public async Task A_block_that_already_fills_the_pane_is_left_alone_and_the_view_put_back()
    {
        _excel.Window.Block = new ExcelFit(FitZoomPercent: 105, PaneWidthPx: 1000, Row: 3, Column: 1, Rows: 60, Columns: 20, CursorRow: 3);

        var result = await Create(minScale: 1.1).ZoomInAsync(Excel, Cursor, CancellationToken.None);

        Assert.Equal(ZoomInStatus.Handled, result.Status);
        Assert.Equal(ZoomReason.AlreadyFits, result.Reason);
        Assert.Equal(100, _excel.Window.Zoom);
        Assert.Equal((1, 1), _excel.Window.Scroll);
    }

    [Fact]
    public async Task Nothing_under_the_cursor_is_self_managed()
    {
        _excel.Window.Block = null;

        var result = await Create().ZoomInAsync(Excel, Cursor, CancellationToken.None);

        Assert.Equal(ZoomInStatus.Handled, result.Status);
        Assert.Equal(ZoomReason.NoBlock, result.Reason);
        Assert.Equal(100, _excel.Window.Zoom);
    }

    [Fact]
    public async Task A_window_that_is_not_Excel_is_unhandled()
    {
        _excel.Attached = null;

        Assert.Equal(ZoomInStatus.Unhandled, (await Create().ZoomInAsync(Excel, Cursor, CancellationToken.None)).Status);
    }

    [Fact]
    public async Task Zoom_out_restores_the_captured_view()
    {
        var adapter = Create();
        _excel.Window.Block = new ExcelFit(200, 1000, 3, 1, 38, 10, 3);
        var result = await adapter.ZoomInAsync(Excel, Cursor, CancellationToken.None);

        await adapter.ZoomOutAsync(Excel, result.RestoreState!, CancellationToken.None);

        Assert.Equal(100, _excel.Window.Zoom);
        Assert.Equal((1, 1), _excel.Window.Scroll);
    }

    [Fact]
    public async Task Zoom_out_rejects_foreign_state() =>
        await Assert.ThrowsAsync<ArgumentException>(() => Create().ZoomOutAsync(Excel, "nope", CancellationToken.None));

    [Fact]
    public async Task An_object_model_failure_puts_the_view_back()
    {
        _excel.Window.Block = new ExcelFit(200, 1000, 3, 1, 38, 10, 3);
        _excel.Window.FailScroll = true;

        var result = await Create().ZoomInAsync(Excel, Cursor, CancellationToken.None);

        Assert.Equal(ZoomInStatus.Handled, result.Status);
        Assert.Equal(ZoomReason.AutomationFailed, result.Reason);
        Assert.Equal(100, _excel.Window.Zoom);
        Assert.True(_excel.Window.Disposed);
    }

    private sealed class FakeExcel : IExcelAutomation
    {
        public FakeExcelWindow Window { get; } = new();

        public IExcelWindow? Attached { get; set; }

        public FakeExcel() => Attached = Window;

        public Task<IExcelWindow?> AttachAsync(TargetInfo target, CancellationToken cancellationToken) =>
            Task.FromResult(Attached);
    }

    private sealed class FakeExcelWindow : IExcelWindow
    {
        public ExcelViewState State { get; set; } = new(100, 1, 1);

        public ExcelFit? Block { get; set; }

        public bool FailScroll { get; set; }

        public bool Disposed { get; private set; }

        public int Zoom { get; private set; } = 100;

        public (int Row, int Column) Scroll { get; private set; } = (1, 1);

        public ExcelViewState GetState() => State;

        public ExcelFit? ApplyFitToBlockAt(ScreenPoint point)
        {
            if (Block is { } block)
                Zoom = block.FitZoomPercent; // Excel applies the fitting zoom while measuring it

            return Block;
        }

        public void SetZoom(int percent) => Zoom = percent;

        public void ScrollTo(int row, int column)
        {
            if (FailScroll)
            {
                // Exactly what Excel's object model throws when it is busy; the adapter catches this type.
#pragma warning disable CA2201
                throw new System.Runtime.InteropServices.COMException("Excel is busy.", unchecked((int)0x800AC472));
#pragma warning restore CA2201
            }

            Scroll = (row, column);
        }

        public void Restore(ExcelViewState state)
        {
            Zoom = state.ZoomPercent;
            Scroll = (state.ScrollRow, state.ScrollColumn);
        }

        public void Dispose() => Disposed = true;
    }
}
