using System.Runtime.InteropServices;

using Microsoft.CSharp.RuntimeBinder;
using Microsoft.Extensions.Logging;

using SmartZoom.Core.Input;
using SmartZoom.Core.Routing;
using SmartZoom.Core.Zoom.Office;
using SmartZoom.Interop.Windows;

using Windows.Win32;
using Windows.Win32.Foundation;

namespace SmartZoom.Interop.Office;

/// <summary>Attaches to Excel worksheet windows through the object model exposed on the grid.</summary>
/// <remarks>
/// <para>
/// Excel's worksheet grid (window class <c>EXCEL7</c>, a grandchild of the frame through <c>XLDESK</c>)
/// answers <c>WM_GETOBJECT</c> with <c>OBJID_NATIVEOM</c> by returning its own <c>Window</c> automation
/// object, which resolves the exact workbook window under the cursor even with several open.
/// </para>
/// <para>
/// Everything is late-bound (<c>dynamic</c>) so no Office interop assemblies are needed and any Excel version
/// works. All calls run on one dedicated STA thread, as Office's object model expects.
/// </para>
/// </remarks>
public sealed partial class ExcelAutomation(ILogger<ExcelAutomation> logger) : IExcelAutomation, IDisposable
{
    private const string GridClass = "EXCEL7";
    private const uint ObjIdNativeOm = 0xFFFFFFF0;
    private static readonly Guid IidIDispatch = new("00020400-0000-0000-C000-000000000046");

    private readonly StaThread _sta = new("SmartZoom Excel COM");

    /// <inheritdoc />
    public async Task<IExcelWindow?> AttachAsync(TargetInfo target, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(target);

        var grid = FindGrid(target);
        if (grid.IsNull)
            return null;

        return await _sta.RunAsync<IExcelWindow?>(() => Attach(grid), cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public void Dispose() => _sta.Dispose();

    private ExcelWindow? Attach(HWND grid)
    {
        var hr = AccessibleObjectFromWindow(grid, ObjIdNativeOm, in IidIDispatch, out var native);
        if (hr != 0 || native is null)
        {
            LogNoObjectModel(hr);
            return null;
        }

        return new ExcelWindow(native, grid, _sta);
    }

    private static HWND FindGrid(TargetInfo target)
    {
        if (target.HitClassName == GridClass)
            return new HWND(target.HitWindow);

        // EnumChildWindows walks the whole tree, which it needs to: EXCEL7 sits under XLDESK, not under the frame.
        var found = HWND.Null;
        PInvoke.EnumChildWindows(new HWND(target.RootWindow), (child, _) =>
        {
            if (WindowInspector.GetClassName(child) != GridClass)
                return true;

            found = child;
            return false;
        }, default);

        return found;
    }

    [DllImport("oleacc.dll", ExactSpelling = true)]
    private static extern int AccessibleObjectFromWindow(HWND hwnd, uint dwId, in Guid riid, [MarshalAs(UnmanagedType.IDispatch)] out object? ppvObject);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Excel did not hand out its object model (0x{HResult:X8}).")]
    private partial void LogNoObjectModel(int hresult);

    /// <summary>One attached Excel <c>Window</c>. Every member marshals to the STA thread.</summary>
    private sealed class ExcelWindow(object window, HWND grid, StaThread sta) : IExcelWindow
    {
        public ExcelViewState GetState() => sta.Run(() =>
        {
            dynamic w = window;
            return new ExcelViewState((int)w.Zoom, (int)w.ScrollRow, (int)w.ScrollColumn);
        });

        public ExcelFit? ApplyFitToBlockAt(ScreenPoint point) => sta.Run(() =>
        {
            dynamic w = window;
            dynamic app = w.Application;

            object? hit = w.RangeFromPoint(point.X, point.Y);
            if (hit is null)
                return (ExcelFit?)null;

            object? found = SurroundingCells(app, hit);
            if (found is null)
                return (ExcelFit?)null;

            dynamic cells = found;
            if (IsBlank(cells))
                return (ExcelFit?)null;

            PInvoke.GetWindowRect(grid, out var rect);
            var row = (int)cells.Row;
            var column = (int)cells.Column;
            var rows = (int)cells.Rows.Count;
            var columns = (int)cells.Columns.Count;
            var cursorRow = CursorRow(hit, row);

            // Excel reports a fitting zoom only by performing one: "zoom to selection" on the block's first row
            // fits its width, since a single row can never be the binding dimension. The user's selection is put
            // back even if that fails half way, because losing it would be the most visible thing we ever did.
            var selection = Selected(app);
            int fit;
            try
            {
                dynamic firstRow = cells.Rows[1];
                firstRow.Select();
                w.Zoom = true;
                fit = (int)w.Zoom;
            }
            finally
            {
                Reselect(selection);
            }

            return new ExcelFit(fit, rect.right - rect.left, row, column, rows, columns, cursorRow);
        });

        public void SetZoom(int percent) => sta.Run(() =>
        {
            dynamic w = window;
            w.Zoom = percent;
        });

        public void ScrollTo(int row, int column) => sta.Run(() =>
        {
            dynamic w = window;
            w.ScrollRow = row;
            w.ScrollColumn = column;
        });

        public void Restore(ExcelViewState state) => sta.Run(() =>
        {
            dynamic w = window;
            w.Zoom = state.ZoomPercent;
            w.ScrollRow = state.ScrollRow;
            w.ScrollColumn = state.ScrollColumn;
        });

        public void Dispose() => sta.Run(() =>
        {
            // One release for the one reference this object took; the runtime may be sharing the wrapper with
            // another caller, and FinalReleaseComObject would pull it out from under them.
            if (Marshal.IsComObject(window))
                Marshal.ReleaseComObject(window);
        });

        // The reading unit under the cursor. Excel answers a point over a chart or picture with that object
        // (which has no Address), and a point over the grid with the cell, whose island of filled cells is the
        // block. A shape reports the cells it covers, so neither case needs Excel's points converted to pixels.
        private static object? SurroundingCells(dynamic app, object hit)
        {
            dynamic candidate = hit;
            try
            {
                _ = candidate.Address;
                return (object)candidate.CurrentRegion;
            }
            catch (RuntimeBinderException)
            {
            }
            catch (COMException)
            {
            }

            try
            {
                return (object)app.Range(candidate.TopLeftCell, candidate.BottomRightCell);
            }
            catch (RuntimeBinderException)
            {
                return null;
            }
            catch (COMException)
            {
                return null;
            }
        }

        // A lone empty cell is not a block; zooming it would pick an arbitrary scale.
        private static bool IsBlank(dynamic cells)
        {
            try
            {
                return (int)cells.Cells.Count <= 1 && cells.Value2 is null;
            }
            catch (COMException)
            {
                return false;
            }
        }

        /// <summary>The row of the cell the cursor was over, or the block's first row for a chart or picture.</summary>
        private static int CursorRow(object hit, int fallback)
        {
            dynamic cell = hit;
            try
            {
                _ = cell.Address;
                return (int)cell.Row;
            }
            catch (RuntimeBinderException)
            {
                return fallback;
            }
            catch (COMException)
            {
                return fallback;
            }
        }

        /// <summary>Puts a remembered selection back; a selection that has become invalid is left alone.</summary>
        private static void Reselect(object? selection)
        {
            if (selection is null)
                return;

            try
            {
                ((dynamic)selection).Select();
            }
            catch (RuntimeBinderException)
            {
            }
            catch (COMException)
            {
            }
        }

        // Null when nothing usable is selected. The object is kept rather than its address: an address is only
        // meaningful on the sheet it came from, and the selection may be a chart or a shape.
        private static object? Selected(dynamic app)
        {
            try
            {
                return (object)app.Selection;
            }
            catch (RuntimeBinderException)
            {
                return null;
            }
            catch (COMException)
            {
                return null;
            }
        }
    }
}
