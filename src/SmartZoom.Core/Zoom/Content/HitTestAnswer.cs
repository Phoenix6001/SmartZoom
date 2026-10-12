namespace SmartZoom.Core.Zoom.Content;

/// <summary>What one attempt to hit-test a browser's accessibility tree came back with, before anything is judged.</summary>
/// <remarks>
/// The platform side produces this and nothing else; <see cref="HitTestPolicy"/> decides what it means. Keeping
/// the raw answer apart from its meaning is what lets every answer a browser has been seen to give be replayed
/// in a unit test.
/// </remarks>
/// <param name="Path">
/// The nodes from the element under the query point out to the tree's root, leaf first, exactly as the tree
/// reported them: documents of frames included, coordinates as reported. Empty when the tree had nothing there.
/// </param>
/// <param name="Window">The rectangle of the window that renders the page, or null when it could not be read.</param>
/// <param name="Failure">The error the tree answered with instead of a path, or null when it answered.</param>
public sealed record HitTestAnswer(IReadOnlyList<ContentNode> Path, PixelRect? Window, Exception? Failure = null)
{
    /// <summary>The tree refused to answer: it threw, as a browser still building its tree does.</summary>
    /// <param name="failure">What the call threw.</param>
    /// <returns>An answer carrying only the failure.</returns>
    public static HitTestAnswer Refused(Exception failure)
    {
        ArgumentNullException.ThrowIfNull(failure);
        return new HitTestAnswer([], null, failure);
    }
}
