using Microsoft.Extensions.Logging;

using SmartZoom.Core.Input;
using SmartZoom.Core.Routing;
using SmartZoom.Core.Settings;

namespace SmartZoom.Core.Zoom.Reader;

/// <summary>
/// Zooms a document reader with its own keyboard shortcuts: one combination to zoom in, another to come
/// back. Made for readers with "fit width" and "fit page" commands, where the first press makes the page
/// fill the window and the second shows the whole page again, the way a reader is usually left. Both states
/// are exact and never drift. This is the mode for readers that ignore touch, where the pinch cannot work.
/// </summary>
/// <remarks>
/// The shortcut alone ignores the cursor, so the reader decides what you end up looking at. Given a view,
/// the adapter first brings the content under the cursor to the top of the window: readers keep the top of
/// the view when the zoom changes, so what you pointed at is what fills the window. The second press undoes
/// the zoom and then the scroll, by the distance the view was measured to have moved — near the end of a
/// document a reader scrolls less than it was asked to, and undoing the request would leave it adrift.
/// </remarks>
public sealed partial class ReaderShortcutAdapter : ZoomAdapter<ReaderShortcutAdapter.RestoreState>
{
    private readonly ShortcutSender _shortcuts;
    private readonly IReaderView? _view;
    private readonly bool _followCursor;
    private readonly int _margin;
    private readonly KeyCombo _zoomIn;
    private readonly KeyCombo _zoomOut;
    private readonly ILogger<ReaderShortcutAdapter> _logger;

    /// <summary>Creates the adapter.</summary>
    /// <param name="shortcuts">Sends the reader's zoom commands.</param>
    /// <param name="view">The reader's content area and scrolling. Null leaves the framing to the reader.</param>
    /// <param name="settings">How to zoom the reader.</param>
    /// <param name="logger">Logger.</param>
    /// <exception cref="FormatException">One of the shortcuts is not a valid combination.</exception>
    public ReaderShortcutAdapter(ShortcutSender shortcuts, IReaderView? view, ReaderZoomSettings settings, ILogger<ReaderShortcutAdapter> logger)
        : base(ReaderAdapter.Descriptor)
    {
        ArgumentNullException.ThrowIfNull(settings);

        _shortcuts = shortcuts ?? throw new ArgumentNullException(nameof(shortcuts));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _view = view;
        _followCursor = settings.FollowCursor;
        _margin = settings.TopGapPx;
        _zoomIn = KeyCombo.Parse(settings.ZoomInKeys);
        _zoomOut = KeyCombo.Parse(settings.ZoomOutKeys);
    }

    /// <inheritdoc />
    protected override async Task<ZoomInResult> ZoomInAsync(TargetInfo target, ScreenPoint point, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(target);

        // Scrolling before the shortcut, while the page is still small, keeps the arithmetic out of this: the
        // reader holds the top of the view across a zoom change, so whatever is at the top stays there and
        // simply grows. It waits for the window and the keyboard along with the shortcut, because a wheel turn
        // with the trigger's Control key still down is not a scroll to a reader, it is a zoom.
        var scrolled = 0;
        var sent = await _shortcuts.SendAsync(
            target,
            _zoomIn,
            prepare: () => scrolled = BringToTop(target, point, cancellationToken),
            finish: null,
            cancellationToken).ConfigureAwait(false);

        if (sent)
            return Applied(new RestoreState(scrolled));

        if (scrolled != 0)
            Scroll(target, -scrolled, cancellationToken);

        return ZoomInResult.Unhandled;
    }

    /// <inheritdoc />
    protected override async Task ZoomOutAsync(TargetInfo target, RestoreState restoreState, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(target);

        // The zoom first, then the scroll: the reader anchors the top of the view, so undoing them in the other
        // order would scroll at the wrong scale. Both happen while the reader still has the foreground, or the
        // wheel would land on whatever window comes back to the front.
        await _shortcuts.SendAsync(
            target,
            _zoomOut,
            prepare: null,
            finish: () =>
            {
                if (restoreState.ScrolledPixels != 0)
                    Scroll(target, -restoreState.ScrolledPixels, cancellationToken);
            },
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Scrolls the content under the cursor to the top of the reader; returns how far the view moved.</summary>
    private int BringToTop(TargetInfo target, ScreenPoint point, CancellationToken cancellationToken)
    {
        if (!_followCursor || _view is null || _view.Bounds(target) is not { } bounds || !bounds.Contains(point))
            return 0;

        var below = point.Y - bounds.Top;
        if (below <= _margin)
            return 0;

        var moved = Scroll(target, below - _margin, cancellationToken);
        LogFollowed(target.ProcessName, below - _margin, moved);
        return moved;
    }

    private int Scroll(TargetInfo target, int pixels, CancellationToken cancellationToken) =>
        _view is null ? 0 : _view.ScrollBy(target, pixels, cancellationToken);

    /// <summary>What has to be undone: the scroll that framed the cursor's content, as it was measured.</summary>
    /// <param name="ScrolledPixels">Measured movement of the scroll before the zoom command.</param>
    public sealed record RestoreState(int ScrolledPixels);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Scrolled the cursor's content to the top of {Process}: asked for {Requested} px, the view moved {Moved} px.")]
    private partial void LogFollowed(string? process, int requested, int moved);
}
