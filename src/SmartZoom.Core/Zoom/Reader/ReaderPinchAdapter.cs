using Microsoft.Extensions.Logging;

using SmartZoom.Core.Input;
using SmartZoom.Core.Routing;
using SmartZoom.Core.Settings;

namespace SmartZoom.Core.Zoom.Reader;

/// <summary>
/// Zooms a document reader with the same animated pinch the browsers get: what you pointed at stays where it
/// is and simply grows. The second press animates the magnification away and then lands on the reader's own
/// "fit page" command, so a reader can be toggled all day without drifting.
/// </summary>
/// <remarks>
/// The closing gesture is for the eye, not for the arithmetic. Windows' recognizer keeps back a share of a
/// closing pinch that depends on the zoom it starts from, so an inverse gesture alone leaves the reader a
/// couple of percent smaller every time and it compounds — measured at 0.980 per toggle, -10% over five.
/// The shortcut afterwards is what makes the second press exact, because it names a state rather than a
/// change. The cost is that a user who was not at fit page loses that zoom once; it does not compound.
/// </remarks>
public sealed partial class ReaderPinchAdapter : ZoomAdapter<ReaderPinchAdapter.RestoreState>
{
    private readonly IPinchInjector _pinch;
    private readonly IReaderView _view;
    private readonly ShortcutSender _shortcuts;
    private readonly double _scale;
    private readonly TimeSpan _animation;
    private readonly KeyCombo _zoomOut;
    private readonly ILogger<ReaderPinchAdapter> _logger;

    /// <summary>Creates the adapter.</summary>
    /// <param name="pinch">Performs the gesture.</param>
    /// <param name="view">The reader's content area, and how to measure its scrolling.</param>
    /// <param name="shortcuts">Sends the reader's own zoom command after the closing gesture.</param>
    /// <param name="settings">How to zoom the reader.</param>
    /// <param name="animation">How long the gesture takes.</param>
    /// <param name="logger">Logger.</param>
    /// <exception cref="ArgumentOutOfRangeException"><see cref="ReaderZoomSettings.Magnification"/> is not above 1.</exception>
    /// <exception cref="FormatException"><see cref="ReaderZoomSettings.ZoomOutKeys"/> is not a valid combination.</exception>
    public ReaderPinchAdapter(IPinchInjector pinch, IReaderView view, ShortcutSender shortcuts, ReaderZoomSettings settings, TimeSpan animation, ILogger<ReaderPinchAdapter> logger)
        : base(ReaderAdapter.Descriptor)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(settings.Magnification, 1.0, "Zoom.Reader.Magnification");

        _pinch = pinch ?? throw new ArgumentNullException(nameof(pinch));
        _view = view ?? throw new ArgumentNullException(nameof(view));
        _shortcuts = shortcuts ?? throw new ArgumentNullException(nameof(shortcuts));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _scale = settings.Magnification;
        _animation = animation;
        _zoomOut = KeyCombo.Parse(settings.ZoomOutKeys);
    }

    /// <inheritdoc />
    protected override async Task<ZoomInResult> ZoomInAsync(TargetInfo target, ScreenPoint point, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(target);

        if (_view.Bounds(target) is not { } bounds || !bounds.Contains(point))
        {
            LogNoContentArea(target.ProcessName);
            return ZoomInResult.Unhandled;
        }

        // Where the view sits now, so the gesture's own imprecision can be taken out on the way back.
        var mark = _view.Snapshot(target);

        LogPinching(target.ProcessName, _scale, point.X, point.Y);
        if (!await _pinch.PinchAsync(point, _scale, _animation, bounds, cancellationToken).ConfigureAwait(false))
        {
            LogGestureRejected(target.ProcessName);
            return ZoomInResult.Unhandled;
        }

        return Applied(new RestoreState(point, mark));
    }

    /// <inheritdoc />
    protected override async Task ZoomOutAsync(TargetInfo target, RestoreState restoreState, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(target);

        var anchor = restoreState.Anchor;
        if (_view.Bounds(target) is { } bounds)
        {
            var back = Math.Clamp(anchor.X, bounds.Left, bounds.Right - 1);
            var down = Math.Clamp(anchor.Y, bounds.Top, bounds.Bottom - 1);
            if (!await _pinch.PinchAsync(new ScreenPoint(back, down), 1 / _scale, _animation, bounds, cancellationToken).ConfigureAwait(false))
                LogGestureRejected(target.ProcessName);
        }

        var landed = await _shortcuts.SendAsync(
            target,
            _zoomOut,
            prepare: null,
            // The reader's zoom command also moves the view; where it came from is what the mark remembers.
            finish: () =>
            {
                if (restoreState.Mark is { } mark && !_view.ScrollBackTo(target, mark, cancellationToken))
                    LogNotAligned(target.ProcessName);
            },
            cancellationToken).ConfigureAwait(false);

        // Without the shortcut the reader is left a little smaller than it started. Nothing can be done about
        // it from here, but it should not pass in silence.
        if (!landed)
            LogNotLanded(target.ProcessName);
    }

    /// <summary>What has to be undone after a gesture.</summary>
    /// <param name="Anchor">The point the gesture magnified around.</param>
    /// <param name="Mark">How the view looked beforehand, or null when the screen could not be read.</param>
    public sealed record RestoreState(ScreenPoint Anchor, ReaderViewMark? Mark);

    [LoggerMessage(Level = LogLevel.Information, Message = "Smart zoom ({Process}): pinching x{Scale} around ({X}, {Y}).")]
    private partial void LogPinching(string? process, double scale, int x, int y);

    [LoggerMessage(Level = LogLevel.Warning, Message = "The gesture was rejected in {Process}; nothing was zoomed.")]
    private partial void LogGestureRejected(string? process);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Smart zoom unavailable: {Process} has no content area under the cursor. Nothing was zoomed.")]
    private partial void LogNoContentArea(string? process);

    [LoggerMessage(Level = LogLevel.Warning, Message = "{Process} did not take the zoom command after the gesture; it is left slightly smaller than it started.")]
    private partial void LogNotLanded(string? process);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Could not read {Process} well enough to put the view back exactly after the gesture.")]
    private partial void LogNotAligned(string? process);
}
