using Microsoft.Extensions.Logging;
using SmartZoom.Core.Input;
using SmartZoom.Core.Routing;
using SmartZoom.Core.Settings;
using SmartZoom.Core.Zoom.Content;

namespace SmartZoom.Core.Zoom;

/// <summary>
/// Smart zoom for browsers without an extension: hit-test the page's accessibility tree to find the
/// block under the cursor, then pinch so it fills the viewport. Zoom-out pinches back past 1.0, which
/// the browser clamps to the exact original view.
/// </summary>
public sealed partial class BrowserAdapter : IZoomAdapter
{
    // Pinch slightly past 1.0 so rounding in the gesture can't leave the page at 1.02x.
    private const double RestoreOvershoot = 0.9;

    // A contact that lands on the browser's vertical scrollbar drags it instead of pinching. Classic scrollbars
    // are 17 px at 100% scaling and 51 px at 300%; the injector keeps contacts out of this strip.
    private const int ScrollbarAllowance = 56;

    // Windows gives touch a generous grip on a window's resize borders: a contact that goes down on the
    // viewport's outermost pixels (the viewport starts 8 px inside the window rect in Chromium, and a contact
    // 8 px inside the rect still grabbed the border on a 200% display, 12 px did not) resizes the window
    // instead of pinching, and a converging zoom-out then drags the window edge ~150 px inward. Contacts stay
    // this far inside the viewport on the sides the window frame can touch.
    private const int ResizeBorderAllowance = 24;

    // The anchor stays where the contacts may go: inside the resize-border allowance plus the injector's own 4 px
    // edge margin. A pinch around an anchor outside the contact area is done around a substitute focus plus a
    // one-finger pan, and a pan that pushes the content past the layout viewport's edge scrolls the page, which
    // zooming back out does not undo (an 8 px residual scroll was measured). A slightly inset anchor is free:
    // the browser clamps the visual viewport at the page edge, so the block lands a few pixels further in.
    private const int EdgeInset = ResizeBorderAllowance + 4;

    // After resetting a stuck visual zoom, the browser needs a moment before its accessibility rects are right
    // again. One more look is all it gets: the reset is instant, and a cold accessibility tree costs the hit tester
    // ~600 ms of wake-up retries per look, so three looks kept the user waiting 2.4 s (and their next press was dropped).
    private static readonly TimeSpan ResetSettle = TimeSpan.FromMilliseconds(200);
    private const int ResetAttempts = 1;

    private readonly IContentHitTester _hitTester;
    private readonly IPinchInjector _pinch;
    private readonly BlockSelector _blocks;
    private readonly SmartZoomPlanner _planner;
    private readonly AnchorInsets _insets;
    private readonly TimeSpan _animation;
    private readonly ILogger<BrowserAdapter> _logger;

    /// <summary>Creates the adapter.</summary>
    /// <param name="hitTester">Finds content under the cursor.</param>
    /// <param name="pinch">Performs the zoom gesture.</param>
    /// <param name="zoom">Zoom limits and animation preference.</param>
    /// <param name="logger">Logger.</param>
    public BrowserAdapter(IContentHitTester hitTester, IPinchInjector pinch, ZoomSettings zoom, ILogger<BrowserAdapter> logger)
    {
        ArgumentNullException.ThrowIfNull(zoom);

        _hitTester = hitTester ?? throw new ArgumentNullException(nameof(hitTester));
        _pinch = pinch ?? throw new ArgumentNullException(nameof(pinch));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _blocks = new BlockSelector();
        _planner = new SmartZoomPlanner(zoom.MinScale, zoom.MaxScale, zoom.Browser.MarginPx);

        var inset = Math.Max(EdgeInset, zoom.Browser.AnchorInsetPx);
        _insets = new AnchorInsets(inset, inset);
        _animation = zoom.Animate ? TimeSpan.FromMilliseconds(zoom.Browser.AnimationMs) : TimeSpan.Zero;
    }

    /// <inheritdoc />
    public AdapterKind Kind => AdapterKind.Browser;

    /// <inheritdoc />
    public async Task<ZoomInResult> ZoomInAsync(TargetInfo target, ScreenPoint point, CancellationToken cancellationToken)
    {
        // Every "can't" below is reported as handled-with-nothing-to-undo rather than Unhandled: the
        // coordinator's Ctrl+wheel fallback is browser page zoom, which is per site across all windows and
        // drifts when steps get coalesced, so for a browser it is worse than doing nothing.
        var hit = await _hitTester.HitTestAsync(target, point, cancellationToken).ConfigureAwait(false);
        if (hit is null)
        {
            LogNoContent(target.ProcessName);
            return ZoomInResult.SelfManaged;
        }

        var block = _blocks.Select(hit);
        if (block is null)
        {
            // Maybe the page is still visually zoomed from an earlier zoom this app has forgotten (restarted, or
            // the restore did not take): accessibility rects are then off-screen or wrong and nothing qualifies.
            // Zooming out below 1.0 is invisible on a page that is not zoomed and resets one that is; look again.
            await _pinch.PinchAsync(ClampInto(point, hit.Viewport), RestoreOvershoot / _planner.MaxScale, TimeSpan.Zero, ContactBounds(hit.Viewport), cancellationToken).ConfigureAwait(false);
            for (var attempt = 0; attempt < ResetAttempts && block is null; attempt++)
            {
                await Task.Delay(ResetSettle, cancellationToken).ConfigureAwait(false);
                hit = await _hitTester.HitTestAsync(target, point, cancellationToken).ConfigureAwait(false);
                block = hit is null ? null : _blocks.Select(hit);
            }

            if (hit is null || block is null)
            {
                LogNoBlock(target.ProcessName, hit?.Chain.Count ?? 0);
                if (hit is not null && _logger.IsEnabled(LogLevel.Debug))
                {
                    var path = string.Join(" < ", hit.Chain.Select(n => $"{n.Role} {n.Bounds.Width}x{n.Bounds.Height}@({n.Bounds.Left},{n.Bounds.Top})"));
                    LogPath(path, hit.Viewport.Width, hit.Viewport.Height);
                }

                return ZoomInResult.SelfManaged;
            }
        }

        var plan = _planner.Plan(block.Bounds, hit.Viewport, point, _insets);
        if (plan is not { } p)
        {
            // Already fills the width: nothing to zoom to. Handled, but nothing to undo either.
            LogAlreadyFits(target.ProcessName, block.Role, block.Bounds.Width, hit.Viewport.Width);
            return ZoomInResult.SelfManaged;
        }

        LogPlan(block.Role, block.Bounds.Width, block.Bounds.Height, hit.Viewport.Width, p.Scale, p.Anchor.X, p.Anchor.Y);

        var bounds = ContactBounds(hit.Viewport);

        // Start from a known baseline: an instant pinch-out is invisible at 1.0 (the browser clamps there) but
        // undoes any visual zoom left behind — e.g. when SmartZoom was restarted while a window was zoomed in.
        await _pinch.PinchAsync(p.Anchor, RestoreOvershoot / _planner.MaxScale, TimeSpan.Zero, bounds, cancellationToken).ConfigureAwait(false);

        if (!await _pinch.PinchAsync(p.Anchor, p.Scale, _animation, bounds, cancellationToken).ConfigureAwait(false))
        {
            LogPinchRejected(target.ProcessName);
            return ZoomInResult.SelfManaged;
        }

        return ZoomInResult.Applied(new RestoreState(p, bounds));
    }

    private static ScreenPoint ClampInto(ScreenPoint point, PixelRect rect) =>
        new(ClampWithInset(point.X, rect.Left, rect.Right - 1), ClampWithInset(point.Y, rect.Top, rect.Bottom - 1));

    // Clamp into [min + EdgeInset, max - EdgeInset]; a range narrower than two insets (a tiny viewport) yields its middle.
    private static int ClampWithInset(int value, int min, int max)
    {
        var low = min + EdgeInset;
        var high = max - EdgeInset;
        return low > high ? (min + max) / 2 : Math.Clamp(value, low, high);
    }

    // Where synthetic contacts may land: the viewport minus the vertical scrollbar strip on the right and minus
    // the window's touch resize zone on the other sides (the scrollbar strip already covers it on the right).
    internal static PixelRect ContactBounds(PixelRect viewport)
    {
        var left = viewport.Left + ResizeBorderAllowance;
        var top = viewport.Top + ResizeBorderAllowance;
        return new PixelRect(
            left,
            top,
            Math.Max(left + 1, viewport.Right - ScrollbarAllowance),
            Math.Max(top + 1, viewport.Bottom - ResizeBorderAllowance));
    }

    /// <inheritdoc />
    public async Task ZoomOutAsync(TargetInfo target, object restoreState, CancellationToken cancellationToken)
    {
        if (restoreState is not RestoreState state)
            throw new ArgumentException($"Expected {nameof(RestoreState)} from a previous zoom-in.", nameof(restoreState));

        var plan = state.Plan;
        await _pinch.PinchAsync(plan.Anchor, RestoreOvershoot / plan.Scale, _animation, state.Bounds, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>What was applied, so the same gesture can be reversed.</summary>
    /// <param name="Plan">Scale and anchor of the zoom-in.</param>
    /// <param name="Bounds">Contact area used for the zoom-in; reused so the zoom-out takes the same orientation.</param>
    internal sealed record RestoreState(ZoomPlan Plan, PixelRect Bounds);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Smart zoom unavailable: no accessible content under the cursor in {Process}. Nothing was zoomed.")]
    private partial void LogNoContent(string? process);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Smart zoom unavailable: no zoomable block on the {Depth}-node path under the cursor in {Process}. Nothing was zoomed.")]
    private partial void LogNoBlock(string? process, int depth);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Path under the cursor (leaf first) in a {ViewportWidth}x{ViewportHeight} viewport: {Path}")]
    private partial void LogPath(string path, int viewportWidth, int viewportHeight);

    [LoggerMessage(Level = LogLevel.Debug, Message = "{Role} block ({Width} px) already fills the {ViewportWidth} px viewport in {Process}; nothing to zoom.")]
    private partial void LogAlreadyFits(string? process, ContentRole role, int width, int viewportWidth);

    [LoggerMessage(Level = LogLevel.Information, Message = "Smart zoom: {Role} {Width}x{Height} px in {ViewportWidth} px viewport -> x{Scale:F2} around ({AnchorX}, {AnchorY}).")]
    private partial void LogPlan(ContentRole role, int width, int height, int viewportWidth, double scale, int anchorX, int anchorY);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Pinch injection was rejected for {Process}; is the window elevated?")]
    private partial void LogPinchRejected(string? process);
}
