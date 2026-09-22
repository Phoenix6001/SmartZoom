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

    private readonly IWordAutomation _word;
    private readonly TimeProvider _time;
    private readonly double _minScale;
    private readonly double _maxScale;
    private readonly int _margin;
    private readonly TimeSpan _animation;
    private readonly ILogger<WordComAdapter> _logger;

    /// <summary>Creates the adapter.</summary>
    /// <param name="word">Word automation.</param>
    /// <param name="zoom">Zoom limits and animation preference.</param>
    /// <param name="time">Clock for the animation delays.</param>
    /// <param name="logger">Logger.</param>
    public WordComAdapter(IWordAutomation word, ZoomSettings zoom, TimeProvider time, ILogger<WordComAdapter> logger)
        : base(Descriptor)
    {
        ArgumentNullException.ThrowIfNull(zoom);

        _word = word ?? throw new ArgumentNullException(nameof(word));
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

        try
        {
            var block = window.GetBlockAt(point);
            if (block is not { } bounds || bounds.IsEmpty)
            {
                LogNoBlock(target.ProcessName);
                return ZoomInResult.Handled(ZoomReason.NoBlock);
            }

            var viewport = window.Viewport;
            var before = window.GetState();

            // Same fit rule as the browsers: the block plus margins fills the pane width.
            var scale = Math.Min((double)viewport.Width / (bounds.Width + (2 * _margin)), _maxScale);
            if (scale < _minScale)
            {
                LogAlreadyFits(target.ProcessName, bounds.Width, viewport.Width);
                return ZoomInResult.Handled(ZoomReason.AlreadyFits);
            }

            var targetZoom = Math.Clamp((int)Math.Round(before.ZoomPercent * scale), MinWordZoom, MaxWordZoom);
            LogPlan(bounds.Width, bounds.Height, viewport.Width, before.ZoomPercent, targetZoom);

            await AnimateZoomAsync(window, before.ZoomPercent, targetZoom, cancellationToken).ConfigureAwait(false);
            window.ScrollBlockIntoView(point);

            return ZoomInResult.Applied(before);
        }
        catch (COMException ex)
        {
            LogComFailure(ex, target.ProcessName);
            return ZoomInResult.Handled(ZoomReason.AutomationFailed);
        }
    }

    /// <inheritdoc />
    protected override async Task ZoomOutAsync(TargetInfo target, WordViewState restoreState, CancellationToken cancellationToken)
    {
        var state = restoreState;

        using var window = await _word.AttachAsync(target, cancellationToken).ConfigureAwait(false);
        if (window is null)
            return;

        try
        {
            await AnimateZoomAsync(window, window.GetState().ZoomPercent, state.ZoomPercent, cancellationToken).ConfigureAwait(false);
            window.Restore(state);
        }
        catch (COMException ex)
        {
            LogComFailure(ex, target.ProcessName);
        }
    }

    // The visible motion: a handful of object-model steps, since Word re-lays out on every zoom change. The
    // caller sets the exact final zoom through the object model afterwards.
    //
    // Driving this with a touch pinch instead was tried and abandoned: Word renders a live scaled preview,
    // which looks much smoother, but it then commits the gesture's own result asynchronously and overwrites
    // the exact value set afterwards. Zoom drifted 100 -> 130 -> 160 across cycles, and polling until Word's
    // zoom settled did not fix it. See docs/decisions.md.
    private async Task AnimateZoomAsync(IWordWindow window, int from, int to, CancellationToken cancellationToken)
    {
        if (_animation == TimeSpan.Zero || from == to)
        {
            window.SetZoom(to);
            return;
        }

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

    [LoggerMessage(Level = LogLevel.Debug, Message = "Block ({Width} px) already fills the {ViewportWidth} px pane in {Process}; nothing to zoom.")]
    private partial void LogAlreadyFits(string? process, int width, int viewportWidth);

    [LoggerMessage(Level = LogLevel.Information, Message = "Smart zoom (Word): block {Width}x{Height} px in {ViewportWidth} px pane -> zoom {From}% to {To}%.")]
    private partial void LogPlan(int width, int height, int viewportWidth, int from, int to);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Word's object model refused the request in {Process} (busy or window closed); nothing was zoomed.")]
    private partial void LogComFailure(Exception exception, string? process);
}
