using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;
using SmartZoom.Core.Input;
using SmartZoom.Core.Routing;
using SmartZoom.Core.Settings;
using SmartZoom.Core.Zoom.Content;

namespace SmartZoom.Core.Zoom.Office;

/// <summary>
/// Smart zoom for Microsoft Word through its object model: the paragraph (or table / picture) under the
/// cursor is zoomed to fill the document pane, with an animated zoom and an exact return.
/// </summary>
public sealed partial class WordComAdapter : IZoomAdapter
{
    private const int MinWordZoom = 10;
    private const int MaxWordZoom = 500;
    private const int AnimationSteps = 10;

    // Word's vertical scrollbar; synthetic contacts must stay off it.
    private const int ScrollbarAllowance = 56;

    private readonly IWordAutomation _word;
    private readonly IPinchInjector? _pinch;
    private readonly TimeProvider _time;
    private readonly double _minScale;
    private readonly double _maxScale;
    private readonly int _margin;
    private readonly TimeSpan _animation;
    private readonly ILogger<WordComAdapter> _logger;

    /// <summary>Creates the adapter.</summary>
    /// <param name="word">Word automation.</param>
    /// <param name="pinch">
    /// Optional touch pinch for the visible motion: Word renders a live scaled preview during a pinch, which is
    /// smooth, whereas each object-model zoom step is a full re-layout. The object model still sets the exact
    /// final zoom and scroll afterwards. Null means object-model steps only.
    /// </param>
    /// <param name="zoom">Zoom limits and animation preference.</param>
    /// <param name="time">Clock for the animation delays.</param>
    /// <param name="logger">Logger.</param>
    public WordComAdapter(IWordAutomation word, IPinchInjector? pinch, ZoomSettings zoom, TimeProvider time, ILogger<WordComAdapter> logger)
    {
        ArgumentNullException.ThrowIfNull(zoom);

        _word = word ?? throw new ArgumentNullException(nameof(word));
        _pinch = pinch;
        _time = time ?? throw new ArgumentNullException(nameof(time));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _minScale = zoom.MinScale;
        _maxScale = zoom.MaxScale;
        _margin = zoom.Browser.MarginPx;
        _animation = zoom.Animate ? TimeSpan.FromMilliseconds(zoom.Browser.AnimationMs) : TimeSpan.Zero;
    }

    /// <inheritdoc />
    public AdapterKind Kind => AdapterKind.WordCom;

    /// <inheritdoc />
    public async Task<ZoomInResult> ZoomInAsync(TargetInfo target, ScreenPoint point, CancellationToken cancellationToken)
    {
        using var window = await _word.AttachAsync(target, cancellationToken).ConfigureAwait(false);
        if (window is null)
            return ZoomInResult.Unhandled;

        try
        {
            var block = window.GetBlockAt(point);
            if (block is not { } bounds || bounds.IsEmpty)
            {
                LogNoBlock(target.ProcessName);
                return ZoomInResult.SelfManaged;
            }

            var viewport = window.Viewport;
            var before = window.GetState();

            // Same fit rule as the browsers: the block plus margins fills the pane width.
            var scale = Math.Min((double)viewport.Width / (bounds.Width + (2 * _margin)), _maxScale);
            if (scale < _minScale)
            {
                LogAlreadyFits(target.ProcessName, bounds.Width, viewport.Width);
                return ZoomInResult.SelfManaged;
            }

            var targetZoom = Math.Clamp((int)Math.Round(before.ZoomPercent * scale), MinWordZoom, MaxWordZoom);
            LogPlan(bounds.Width, bounds.Height, viewport.Width, before.ZoomPercent, targetZoom);

            var anchor = new ScreenPoint((int)Math.Round(bounds.CenterX), Math.Clamp(point.Y, bounds.Top, bounds.Bottom - 1));
            await AnimateZoomAsync(window, before.ZoomPercent, targetZoom, anchor, viewport, cancellationToken).ConfigureAwait(false);
            window.ScrollBlockIntoView(point);

            return ZoomInResult.Applied(before);
        }
        catch (COMException ex)
        {
            LogComFailure(ex, target.ProcessName);
            return ZoomInResult.SelfManaged;
        }
    }

    /// <inheritdoc />
    public async Task ZoomOutAsync(TargetInfo target, object restoreState, CancellationToken cancellationToken)
    {
        if (restoreState is not WordViewState state)
            throw new ArgumentException($"Expected {nameof(WordViewState)} from a previous zoom-in.", nameof(restoreState));

        using var window = await _word.AttachAsync(target, cancellationToken).ConfigureAwait(false);
        if (window is null)
            return;

        try
        {
            var viewport = window.Viewport;
            var anchor = new ScreenPoint((int)Math.Round(viewport.CenterX), (int)Math.Round(viewport.CenterY));
            await AnimateZoomAsync(window, window.GetState().ZoomPercent, state.ZoomPercent, anchor, viewport, cancellationToken).ConfigureAwait(false);
            window.Restore(state);
        }
        catch (COMException ex)
        {
            LogComFailure(ex, target.ProcessName);
        }
    }

    // The visible motion: a touch pinch when available (Word scales a live preview smoothly), otherwise a
    // handful of object-model steps, since Word re-lays out on every zoom change. Either way the caller sets
    // the exact final zoom through the object model afterwards.
    private async Task AnimateZoomAsync(IWordWindow window, int from, int to, ScreenPoint anchor, PixelRect viewport, CancellationToken cancellationToken)
    {
        if (_animation == TimeSpan.Zero || from == to)
        {
            window.SetZoom(to);
            return;
        }

        if (_pinch is not null)
        {
            var bounds = viewport with { Right = Math.Max(viewport.Left + 1, viewport.Right - ScrollbarAllowance) };
            await _pinch.PinchAsync(anchor, to / (double)from, _animation, bounds, cancellationToken).ConfigureAwait(false);

            // Word commits the pinch result on its own a little after the gesture ends; setting the exact value
            // before that commit gets overwritten by it. Wait until Word's zoom stops moving first.
            await WaitForZoomToSettleAsync(window, cancellationToken).ConfigureAwait(false);
            window.SetZoom(to);
            return;
        }

        var stepDelay = _animation / AnimationSteps;
        for (var step = 1; step <= AnimationSteps; step++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var t = step / (double)AnimationSteps;
            t = t * t * (3 - (2 * t)); // ease-in-out, like the browser gesture
            window.SetZoom((int)Math.Round(from + ((to - from) * t)));

            if (step < AnimationSteps)
                await Task.Delay(stepDelay, _time, cancellationToken).ConfigureAwait(false);
        }
    }

    // Polls Word's zoom until two consecutive reads agree (or a bounded time passes).
    private async Task WaitForZoomToSettleAsync(IWordWindow window, CancellationToken cancellationToken)
    {
        const int pollMs = 50;
        const int maxPolls = 16; // ~800 ms

        var last = window.GetState().ZoomPercent;
        var stable = 0;
        for (var i = 0; i < maxPolls && stable < 2; i++)
        {
            await Task.Delay(TimeSpan.FromMilliseconds(pollMs), _time, cancellationToken).ConfigureAwait(false);
            var now = window.GetState().ZoomPercent;
            stable = now == last ? stable + 1 : 0;
            last = now;
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Smart zoom unavailable: no paragraph, table or picture under the cursor in {Process}. Nothing was zoomed.")]
    private partial void LogNoBlock(string? process);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Block ({Width} px) already fills the {ViewportWidth} px pane in {Process}; nothing to zoom.")]
    private partial void LogAlreadyFits(string? process, int width, int viewportWidth);

    [LoggerMessage(Level = LogLevel.Information, Message = "Smart zoom (Word): block {Width}x{Height} px in {ViewportWidth} px pane -> zoom {From}% to {To}%.")]
    private partial void LogPlan(int width, int height, int viewportWidth, int from, int to);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Word's object model refused the request in {Process} (busy or window closed); nothing was zoomed.")]
    private partial void LogComFailure(Exception exception, string? process);
}
