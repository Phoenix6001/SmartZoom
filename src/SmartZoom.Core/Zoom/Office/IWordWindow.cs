using SmartZoom.Core.Input;
using SmartZoom.Core.Zoom.Content;

namespace SmartZoom.Core.Zoom.Office;

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

    /// <summary>Restores a zoom and scroll position captured with <see cref="GetState"/>, exactly.</summary>
    void Restore(WordViewState state);
}
