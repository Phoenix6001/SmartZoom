using System.Runtime.InteropServices;

using Microsoft.Extensions.Logging;

using SmartZoom.Core.Input;
using SmartZoom.Core.Routing;
using SmartZoom.Core.Zoom.Content;
using SmartZoom.Core.Zoom.Office;
using SmartZoom.Interop.Windows;

using Windows.Win32;
using Windows.Win32.Foundation;

namespace SmartZoom.Interop.Office;

/// <summary>Attaches to Word document windows through the object model exposed on the document pane.</summary>
/// <remarks>
/// <para>
/// Word's document pane (window class <c>_WwG</c>) answers <c>WM_GETOBJECT</c> with <c>OBJID_NATIVEOM</c> by
/// returning its own <c>Window</c> automation object. That resolves the exact window under the cursor even
/// with several documents or Word instances open, which <c>GetActiveObject</c> and the running object table
/// cannot do.
/// </para>
/// <para>
/// Everything is late-bound (<c>dynamic</c>) so no Office interop assemblies are needed and any Office version
/// works. All calls run on one dedicated STA thread, as Office's object model expects.
/// </para>
/// </remarks>
public sealed partial class WordAutomation(ILogger<WordAutomation> logger) : IWordAutomation, IDisposable
{
    private const string DocumentPaneClass = "_WwG";
    private const uint ObjIdNativeOm = 0xFFFFFFF0;
    private static readonly Guid IidIDispatch = new("00020400-0000-0000-C000-000000000046");

    private readonly StaThread _sta = new("SmartZoom Office COM");

    /// <inheritdoc />
    public async Task<IWordWindow?> AttachAsync(TargetInfo target, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(target);

        var pane = FindDocumentPane(target);
        if (pane.IsNull)
            return null;

        return await _sta.RunAsync<IWordWindow?>(() => Attach(pane), cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public void Dispose() => _sta.Dispose();

    private WordWindow? Attach(HWND pane)
    {
        var hr = AccessibleObjectFromWindow(pane, ObjIdNativeOm, in IidIDispatch, out var native);
        if (hr != 0 || native is null)
        {
            LogNoObjectModel(hr);
            return null;
        }

        return new WordWindow(native, pane, _sta);
    }

    private static HWND FindDocumentPane(TargetInfo target)
    {
        if (target.HitClassName == DocumentPaneClass)
            return new HWND(target.HitWindow);

        var found = HWND.Null;
        PInvoke.EnumChildWindows(new HWND(target.RootWindow), (child, _) =>
        {
            if (WindowInspector.GetClassName(child) == DocumentPaneClass)
            {
                found = child;
                return false;
            }

            return true;
        }, default);

        return found;
    }

    [DllImport("oleacc.dll", ExactSpelling = true)]
    private static extern int AccessibleObjectFromWindow(HWND hwnd, uint dwId, in Guid riid, [MarshalAs(UnmanagedType.IDispatch)] out object? ppvObject);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Word did not hand out its object model (0x{HResult:X8}).")]
    private partial void LogNoObjectModel(int hresult);

    /// <summary>One attached Word <c>Window</c>. Every member marshals to the STA thread.</summary>
    private sealed class WordWindow(object window, HWND pane, StaThread sta) : IWordWindow
    {
        // Word constants (WdUnits etc.) are plain integers in the object model.
        private const int WdParagraph = 4;

        public PixelRect Viewport
        {
            get
            {
                PInvoke.GetWindowRect(pane, out var rect);
                return new PixelRect(rect.left, rect.top, rect.right, rect.bottom);
            }
        }

        public WordViewState GetState() => sta.Run(() =>
        {
            dynamic w = window;
            return new WordViewState((int)w.View.Zoom.Percentage, (int)w.VerticalPercentScrolled, (int)w.HorizontalPercentScrolled);
        });

        public PixelRect? GetBlockAt(ScreenPoint point) => sta.Run(() =>
        {
            dynamic w = window;
            dynamic? range = w.RangeFromPoint(point.X, point.Y);
            if (range is null)
                return (PixelRect?)null;

            // A picture or table is the reading unit; otherwise the paragraph the point falls in.
            dynamic block = range.InlineShapes.Count > 0 ? range.InlineShapes[1].Range
                : range.Information[12] /* wdWithInTable */ ? range.Tables[1].Range
                : range.Paragraphs[1].Range;

            int left, top, width, height;
            w.GetPoint(out left, out top, out width, out height, block);
            return PixelRect.FromSize(left, top, width, height);
        });

        public void SetZoom(int percent) => sta.Run(() =>
        {
            dynamic w = window;
            w.View.Zoom.Percentage = percent;
        });

        public void ScrollBlockIntoView(ScreenPoint point) => sta.Run(() =>
        {
            dynamic w = window;
            dynamic? range = w.RangeFromPoint(point.X, point.Y);
            if (range is null)
                return;

            dynamic block = range.Paragraphs[1].Range;
            w.ScrollIntoView(block, true);
        });

        public void Restore(WordViewState state) => sta.Run(() =>
        {
            dynamic w = window;
            w.View.Zoom.Percentage = state.ZoomPercent;
            w.VerticalPercentScrolled = state.VerticalPercent;
            w.HorizontalPercentScrolled = state.HorizontalPercent;
        });

        public void Dispose() => sta.Run(() =>
        {
            if (Marshal.IsComObject(window))
                Marshal.FinalReleaseComObject(window);
        });
    }
}
