using System.Runtime.InteropServices;

using Microsoft.Extensions.Logging;

using SmartZoom.Core.Input;
using SmartZoom.Core.Routing;
using SmartZoom.Core.Settings;
using SmartZoom.Core.Zoom.Content;
using SmartZoom.Core.Zoom.Gesture;

namespace SmartZoom.Core.Zoom.Office;

/// <summary>
/// Smart zoom for Microsoft Word through its object model: the paragraph (or table / picture) under the
/// cursor is zoomed to fill the document pane, with an animated zoom and an exact return.
/// </summary>
public sealed partial class WordComAdapter : ZoomAdapter<WordViewState>
{
    private const int MinWordZoom = 10;
    private const int MaxWordZoom = 500;
    private const int AnimationSteps = 10;

    /// <summary>
    /// How much more to ask the gesture for than the zoom actually wanted. Windows' recognizer delivers less
    /// span than is injected: measured in Word at x2 -> x1.85, i.e. 93% of what was asked for. The exact zoom
    /// is set through the object model afterwards either way; this only keeps that correction invisible.
    /// </summary>
    private const double GestureShortfall = 1.075;

    /// <summary>
    /// How long Word is given to commit the gesture before the exact zoom is set over it. Measured: the new
    /// zoom is not readable at all immediately after the gesture ends and is there by 150 ms. Setting the
    /// exact value inside that window is what made an earlier attempt at this drift (100 -> 130 -> 160 across
    /// cycles) and get abandoned; waiting for it, Word holds the value — still exact 1.5 s later.
    /// </summary>
    private static readonly TimeSpan GestureSettle = TimeSpan.FromMilliseconds(200);

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
    /// <param name="pinch">Gesture injector that carries the motion, or null to step the zoom instead.</param>
    /// <param name="zoom">Zoom limits and animation preference.</param>
    /// <param name="time">Clock for the animation delays.</param>
    /// <param name="logger">Logger.</param>
    public WordComAdapter(IWordAutomation word, IPinchInjector? pinch, ZoomSettings zoom, TimeProvider time, ILogger<WordComAdapter> logger)
        : base(Descriptor)
    {
        ArgumentNullException.ThrowIfNull(zoom);

        _word = word ?? throw new ArgumentNullException(nameof(word));
        _pinch = pinch;
        _time = time ?? throw new ArgumentNullException(nameof(time));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _minScale = zoom.MinScale;
        _maxScale = zoom.MaxScale;
        _margin = zoom.Smart.MarginPx;
        _animation = zoom.Animate ? TimeSpan.FromMilliseconds(zoom.Smart.AnimationMs) : TimeSpan.Zero;
    }

    /// <summary>How this adapter is named in settings, and what it handles out of the box.</summary>
    public static AdapterDescriptor Descriptor { get; } = new(
        "WordCom",
        ["WINWORD"],
        "Microsoft Word",
        "Asks Word itself what is under the cursor and sets the view's zoom, so the paragraph, table or picture fills the window exactly.");

    /// <inheritdoc />
    protected override async Task<ZoomInResult> ZoomInAsync(TargetInfo target, ScreenPoint point, CancellationToken cancellationToken)
    {
        using var window = await _word.AttachAsync(target, cancellationToken).ConfigureAwait(false);
        if (window is null)
            return ZoomInResult.Unhandled;

        // Null until GetState has run: a failure before that point has nothing to put back.
        WordViewState? captured = null;
        try
        {
            var found = window.GetBlockAt(point);
            if (found is not { } block || block.Bounds.IsEmpty)
            {
                LogNoBlock(target.ProcessName);
                return ZoomInResult.Handled(ZoomReason.NoBlock);
            }

            var viewport = window.Viewport;
            if (viewport.IsEmpty)
            {
                // The pane is gone (the document closed under the cursor); a zero-width fit would read as "already fits".
                LogNoViewport(target.ProcessName);
                return ZoomInResult.Handled(ZoomReason.AutomationFailed);
            }

            var before = window.GetState();
            captured = before;

            // Same fit rule as the browsers: the block plus margins fills the pane width.
            var scale = Math.Min((double)viewport.Width / (block.Bounds.Width + (2 * _margin)), _maxScale);
            if (scale < _minScale)
            {
                LogAlreadyFits(target.ProcessName, block.Bounds.Width, viewport.Width);
                return ZoomInResult.Handled(ZoomReason.AlreadyFits);
            }

            var targetZoom = Math.Clamp((int)Math.Round(before.ZoomPercent * scale), MinWordZoom, MaxWordZoom);
            LogPlan(block.Bounds.Width, block.Bounds.Height, viewport.Width, before.ZoomPercent, targetZoom);

            await AnimateZoomAsync(window, before.ZoomPercent, targetZoom, point, gesture: true, cancellationToken).ConfigureAwait(false);
            // By where the block is in the text, not by where it was on screen: the zoom above has
            // re-laid the document out, so that pixel now belongs to different text entirely.
            window.ScrollIntoView(block.Start);

            return Applied(before);
        }
        catch (Exception ex) when (ex is COMException or TimeoutException)
        {
            LogComFailure(ex, target.ProcessName);
            TryRestore(window, captured);
            return ZoomInResult.Handled(ZoomReason.AutomationFailed);
        }
    }

    /// <inheritdoc />
    protected override async Task ZoomOutAsync(TargetInfo target, WordViewState restoreState, CancellationToken cancellationToken)
    {
        using var window = await _word.AttachAsync(target, cancellationToken).ConfigureAwait(false);
        if (window is null)
            return;

        try
        {
            await AnimateZoomAsync(window, window.GetState().ZoomPercent, restoreState.ZoomPercent, around: null, gesture: false, cancellationToken).ConfigureAwait(false);
            window.Restore(restoreState);
        }
        catch (Exception ex) when (ex is COMException or TimeoutException)
        {
            LogComFailure(ex, target.ProcessName);
        }
    }

    // Best-effort undo after a failure part-way through: the animation may already have stepped Word some of
    // the way. Nothing to do when the state was never read.
    private static void TryRestore(IWordWindow window, WordViewState? captured)
    {
        if (captured is not { } state)
            return;

        try
        {
            window.Restore(state);
        }
        catch (Exception ex) when (ex is COMException or TimeoutException)
        {
            // Word is still unwell; the user's next trigger will set the zoom anyway.
        }
    }

    // The visible motion: a handful of object-model steps, since Word re-lays out on every zoom change. The
    // caller sets the exact final zoom through the object model afterwards.
    //
    // Driving this with a touch pinch instead was tried and abandoned: Word renders a live scaled preview,
    // which looks much smoother, but it then commits the gesture's own result asynchronously and overwrites
    // the exact value set afterwards. Zoom drifted 100 -> 130 -> 160 across cycles, and polling until Word's
    // zoom settled did not fix it. See docs/decisions.md.
    private async Task AnimateZoomAsync(IWordWindow window, int from, int to, ScreenPoint? around, bool gesture, CancellationToken cancellationToken)
    {
        if (_animation == TimeSpan.Zero || from == to)
        {
            window.SetZoom(to);
            return;
        }

        // Word renders a pinch itself, which is the whole reason the PDF readers look smooth: the motion is
        // the application's own, not a sequence of zoom values each of which re-lays the document out. The
        // gesture is not the zoom, though — the recognizer delivers about 93% of what it is asked for, so the
        // exact value is set through the object model once Word has committed the gesture.
        //
        // ZOOMING OUT DOES NOT USE IT, and the asymmetry is not an oversight. Word commits an opening pinch
        // about 150 ms after the gesture ends, but a closing one about a SECOND after — long past any settle
        // worth making a user wait for. The exact zoom set in between is then overwritten by Word's own
        // result, and since that result is a fraction of the way back, every toggle left the document about
        // 20% bigger than it started: 100 -> 300 -> 119 -> 316 -> 144, without limit. Measured 2026-09-29;
        // it is also what made an earlier attempt at the pinch drift 100 -> 130 -> 160 and get abandoned.
        // Stepping the zoom out is exact, because object-model steps have nothing to commit late.
        if (gesture && _pinch is not null && window.Viewport is { IsEmpty: false } pane)
        {
            var anchor = around is { } point && pane.Contains(point)
                ? point
                : new ScreenPoint((int)pane.CenterX, (int)pane.CenterY);

            if (await _pinch.PinchAsync(anchor, (double)to / from * GestureShortfall, _animation, pane, cancellationToken).ConfigureAwait(false))
            {
                await Task.Delay(GestureSettle, _time, cancellationToken).ConfigureAwait(false);
                window.SetZoom(to);
                return;
            }
        }

        // No gesture to be had: step the zoom instead, which is what this did before and still looks like
        // motion, even though Word re-lays the document out on every step.
        var stepDelay = _animation / AnimationSteps;
        for (var step = 1; step <= AnimationSteps; step++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var t = Easing.SmoothStep(step / (double)AnimationSteps);
            window.SetZoom((int)Math.Round(from + ((to - from) * t)));

            if (step < AnimationSteps)
                await Task.Delay(stepDelay, _time, cancellationToken).ConfigureAwait(false);
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Smart zoom unavailable: no paragraph, table or picture under the cursor in {Process}. Nothing was zoomed.")]
    private partial void LogNoBlock(string? process);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Word's document pane in {Process} has no size (closed or minimised); nothing was zoomed.")]
    private partial void LogNoViewport(string? process);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Block ({Width} px) already fills the {ViewportWidth} px pane in {Process}; nothing to zoom.")]
    private partial void LogAlreadyFits(string? process, int width, int viewportWidth);

    [LoggerMessage(Level = LogLevel.Information, Message = "Smart zoom (Word): block {Width}x{Height} px in {ViewportWidth} px pane -> zoom {From}% to {To}%.")]
    private partial void LogPlan(int width, int height, int viewportWidth, int from, int to);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Word did not act on the request in {Process} (busy, showing a dialog, or the window closed); nothing was zoomed.")]
    private partial void LogComFailure(Exception exception, string? process);
}
