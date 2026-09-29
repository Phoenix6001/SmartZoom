using SmartZoom.Core.Input;
using SmartZoom.Core.Zoom.Content;

namespace SmartZoom.Core.Zoom.Office;

/// <summary>A Word document window that SmartZoom is attached to.</summary>
/// <remarks>Implementations talk to Word's object model; every member may throw
/// <see cref="System.Runtime.InteropServices.COMException"/> if the window has closed, or
/// <see cref="TimeoutException"/> if Word is busy or showing a dialog and does not answer at all.
/// Callers treat both as "nothing was zoomed".</remarks>
public interface IWordWindow : IDisposable
{
    /// <summary>Screen bounds of the document pane in physical pixels.</summary>
    PixelRect Viewport { get; }

    /// <summary>Current zoom and scroll position.</summary>
    WordViewState GetState();

    /// <summary>
    /// The reading unit under a screen point: the paragraph, or the table / inline picture it belongs to.
    /// Null when the point is not over document content (margins, headers, blank page area).
    /// </summary>
    WordBlock? GetBlockAt(ScreenPoint point);

    /// <summary>Sets the view zoom. Word re-lays out synchronously; returns when the view is updated.</summary>
    void SetZoom(int percent);

    /// <summary>Scrolls so the block starting at <paramref name="start"/> is at the top of the pane.</summary>
    /// <remarks>
    /// Takes the position in the text rather than the position on screen, because the caller has zoomed since
    /// finding it: asking what is under the original pixel after a zoom answers with different text, which is
    /// how a press at the end of page 2 used to end up showing page 1.
    /// </remarks>
    /// <param name="start">Character position from <see cref="WordBlock.Start"/>.</param>
    void ScrollIntoView(int start);

    /// <summary>Restores a zoom and scroll position captured with <see cref="GetState"/>, exactly.</summary>
    void Restore(WordViewState state);
}
