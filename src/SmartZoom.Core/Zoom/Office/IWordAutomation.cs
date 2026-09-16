using SmartZoom.Core.Input;
using SmartZoom.Core.Routing;
using SmartZoom.Core.Zoom.Content;

namespace SmartZoom.Core.Zoom.Office;

/// <summary>Zoom and scroll position of a Word document window, as Word reports them.</summary>
/// <param name="ZoomPercent">View zoom, 10..500.</param>
/// <param name="VerticalPercent">Vertical scroll position, 0..100.</param>
/// <param name="HorizontalPercent">Horizontal scroll position, 0..100.</param>
public readonly record struct WordViewState(int ZoomPercent, int VerticalPercent, int HorizontalPercent);

/// <summary>A Word document window that SmartZoom is attached to.</summary>
/// <remarks>Implementations talk to Word's object model; every member may throw
/// <see cref="System.Runtime.InteropServices.COMException"/> if Word is busy (modal dialog) or the
/// window has closed. Callers treat that as "nothing was zoomed".</remarks>
public interface IWordWindow : IDisposable
{
    /// <summary>Screen bounds of the document pane in physical pixels.</summary>
    PixelRect Viewport { get; }

    /// <summary>Current zoom and scroll position.</summary>
    WordViewState GetState();

    /// <summary>
    /// Bounds of the reading unit under a screen point: the paragraph, or the table / inline picture it belongs to.
    /// Null when the point is not over document content (margins, headers, blank page area).
    /// </summary>
    PixelRect? GetBlockAt(ScreenPoint point);

    /// <summary>Sets the view zoom. Word re-lays out synchronously; returns when the view is updated.</summary>
    void SetZoom(int percent);

    /// <summary>Scrolls so the block found by <see cref="GetBlockAt"/> for <paramref name="point"/> starts at the top of the pane.</summary>
    void ScrollBlockIntoView(ScreenPoint point);

    /// <summary>Restores a previously captured zoom and scroll position exactly.</summary>
    void Restore(WordViewState state);
}

/// <summary>Attaches to Word windows.</summary>
public interface IWordAutomation
{
    /// <summary>Attaches to the Word document window under the cursor, or returns null if the target isn't one.</summary>
    Task<IWordWindow?> AttachAsync(TargetInfo target, CancellationToken cancellationToken);
}
