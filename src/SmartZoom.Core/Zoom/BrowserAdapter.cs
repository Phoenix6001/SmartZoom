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
public sealed partial class BrowserAdapter : ZoomAdapter<BrowserAdapter.RestoreState>
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
    // ~600 ms of wake-up retries per look, so a second look would keep the user waiting longer than a press is worth.
    private static readonly TimeSpan ResetSettle = TimeSpan.FromMilliseconds(200);

    // Whether the page took the pinch is read from the screen: Chromium hands the gesture to the page's own scripts
    // on an element with touch-action: none, and its accessibility rects never reflect visual zoom, so an injected
    // pinch that changed nothing looks exactly like one that worked. The region compared is this much either side
    // of the anchor, clipped to the viewport: wide enough that a zoom around the anchor moves most of it, small
    // enough to read and compare in well under a frame.
    internal const int VerifyHalfWidth = 300;
    internal const int VerifyHalfHeight = 200;

    // Below this fraction of changed cells the screen is "the same": a caret blink or a hover highlight moves a
    // cell or two, a zoom moves most of them.
    internal const double PinchTakenThreshold = 0.02;

    // The injector returns when its last contact lifts; the browser needs a frame or two to draw the final scale.
    private static readonly TimeSpan VerifySettle = TimeSpan.FromMilliseconds(60);

    private readonly IContentHitTester _hitTester;
    private readonly IPinchInjector _pinch;
    private readonly IScreenSampler _screen;
    private readonly bool _ctrlWheelWhenPinchBlocked;
    private readonly BlockSelector _blocks;
    private readonly SmartZoomPlanner _planner;
    private readonly AnchorInsets _insets;
    private readonly TimeSpan _animation;
    private readonly ILogger<BrowserAdapter> _logger;

    /// <summary>Creates the adapter.</summary>
    /// <param name="hitTester">Finds content under the cursor.</param>
    /// <param name="pinch">Performs the zoom gesture.</param>
    /// <param name="screen">Reads the screen, to tell whether the page took the gesture.</param>
    /// <param name="zoom">Zoom limits and animation preference.</param>
    /// <param name="logger">Logger.</param>
    public BrowserAdapter(IContentHitTester hitTester, IPinchInjector pinch, IScreenSampler screen, ZoomSettings zoom, ILogger<BrowserAdapter> logger)
        : base(Descriptor)
    {
        ArgumentNullException.ThrowIfNull(zoom);

        _hitTester = hitTester ?? throw new ArgumentNullException(nameof(hitTester));
        _pinch = pinch ?? throw new ArgumentNullException(nameof(pinch));
        _screen = screen ?? throw new ArgumentNullException(nameof(screen));
        _ctrlWheelWhenPinchBlocked = zoom.Browser.CtrlWheelWhenPinchBlocked;
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _blocks = new BlockSelector();
        _planner = new SmartZoomPlanner(zoom.MinScale, zoom.MaxScale, zoom.Smart.MarginPx);

        var inset = Math.Max(EdgeInset, zoom.Browser.AnchorInsetPx);
        _insets = new AnchorInsets(inset, inset);
        _animation = zoom.Animate ? TimeSpan.FromMilliseconds(zoom.Smart.AnimationMs) : TimeSpan.Zero;
    }

    /// <summary>How this adapter is named in settings, and what it handles out of the box.</summary>
    public static AdapterDescriptor Descriptor { get; } = new(
        "Browser",
        ["chrome", "msedge", "brave", "opera", "vivaldi", "firefox"],
        "Browsers",
        "Reads the page's accessibility tree to find the block under the cursor, then zooms it with a touch pinch. Chromium-based browsers and Firefox.");

    /// <inheritdoc />
    protected override async Task<ZoomInResult> ZoomInAsync(TargetInfo target, ScreenPoint point, CancellationToken cancellationToken)
    {
        // Every "can't" below is reported as handled-with-nothing-to-undo rather than Unhandled: the
        // coordinator's Ctrl+wheel fallback is browser page zoom, which is per site across all windows and
        // drifts when steps get coalesced, so for a browser it is worse than doing nothing. The one exception
        // is a page that blocks the pinch outright, where page zoom is the only zoom there is (see the end).
        var hit = await _hitTester.HitTestAsync(target, point, cancellationToken).ConfigureAwait(false);
        if (hit is null)
        {
            LogNoContent(target.ProcessName);
            return ZoomInResult.Handled(ZoomReason.NoContent);
        }

        var block = _blocks.Select(hit);
        if (block is null)
        {
            // Maybe the page is still visually zoomed from an earlier zoom this app has forgotten (restarted, or
            // the restore did not take): accessibility rects are then off-screen or wrong and nothing qualifies.
            // Zooming out below 1.0 is invisible on a page that is not zoomed and resets one that is; look again.
            // TimeSpan.Zero is deliberate: this is a state reset nobody sees happen, not a gesture, so it must
            // not appear in the gesture-health totals (the injector excludes zero-duration pinches for exactly
            // that reason).
            await _pinch.PinchAsync(ClampInto(point, hit.Viewport), RestoreOvershoot / _planner.MaxScale, TimeSpan.Zero, ContactBounds(hit.Viewport), cancellationToken).ConfigureAwait(false);
            await Task.Delay(ResetSettle, cancellationToken).ConfigureAwait(false);
            hit = await _hitTester.HitTestAsync(target, point, cancellationToken).ConfigureAwait(false);
            block = hit is null ? null : _blocks.Select(hit);

            if (hit is null || block is null)
            {
                LogNoBlock(target.ProcessName, hit?.Chain.Count ?? 0);
                if (hit is not null && _logger.IsEnabled(LogLevel.Debug))
                {
                    var path = ContentPath.Describe(hit.Chain);
                    LogPath(path, hit.Viewport.Width, hit.Viewport.Height);
                }

                // Role and size only, via ContentPath.Shape — never Describe's coordinates, and never any
                // text, which ContentNode does not carry in the first place. This is what makes "no zoomable
                // block" diagnosable from a bug report without it ever containing what was on the page.
                return ZoomInResult.Handled(ZoomReason.NoBlock, hit is null ? null : ContentPath.Shape(hit.Chain));
            }
        }

        var plan = _planner.Plan(block.Bounds, hit.Viewport, point, _insets);
        if (plan is not { } p)
        {
            // Already fills the width: nothing to zoom to. Handled, but nothing to undo either.
            LogAlreadyFits(target.ProcessName, block.Role, block.Bounds.Width, hit.Viewport.Width);
            return ZoomInResult.Handled(ZoomReason.AlreadyFits);
        }

        LogPlan(block.Role, block.Bounds.Width, block.Bounds.Height, hit.Viewport.Width, p.Scale, p.Anchor.X, p.Anchor.Y);

        var bounds = ContactBounds(hit.Viewport);

        // The first gesture is the zoom itself: Edge renders a pinch-out below 1.0 as a visible shrink-and-rebound,
        // so no preparatory gesture may precede it (see docs/decisions.md, "Browsers").
        var region = VerifyRegion(p.Anchor, hit.Viewport);
        var before = _screen.Sample(region);
        if (!await _pinch.PinchAsync(p.Anchor, p.Scale, _animation, bounds, cancellationToken).ConfigureAwait(false))
        {
            LogPinchRejected(target.ProcessName);
            return ZoomInResult.Handled(ZoomReason.GestureRefused);
        }

        var restore = new RestoreState(p, bounds);
        if (before is null)
        {
            LogVerificationSkipped(target.ProcessName);
            return Applied(restore);
        }

        await Task.Delay(VerifySettle, cancellationToken).ConfigureAwait(false);
        var after = _screen.Sample(region);
        if (after is null)
        {
            LogVerificationSkipped(target.ProcessName);
            return Applied(restore);
        }

        var changed = before.Difference(after);
        if (changed >= PinchTakenThreshold)
            return Applied(restore);

        // The gesture went in cleanly and nothing moved: the block under the cursor kept it (touch-action: none).
        // The rest of the page usually does not, and a pinch scales the whole visual viewport around its anchor,
        // so the same zoom is available from an anchor outside the blocking element. At most two such retries —
        // a refused press costs three gestures and nothing more.
        var candidates = RetryAnchor.Candidates(block.Bounds, hit.Viewport, p.Anchor);
        foreach (var candidate in candidates)
        {
            var retryRegion = VerifyRegion(candidate, hit.Viewport);
            var retryBefore = _screen.Sample(retryRegion);
            if (!await _pinch.PinchAsync(candidate, p.Scale, _animation, bounds, cancellationToken).ConfigureAwait(false))
            {
                LogPinchRejected(target.ProcessName);
                return ZoomInResult.Handled(ZoomReason.GestureRefused);
            }

            // The zoom-out has to reverse the gesture that actually happened, not the one that was refused.
            var retried = new RestoreState(p with { Anchor = candidate }, bounds);
            if (retryBefore is null)
            {
                LogVerificationSkipped(target.ProcessName);
                return Applied(retried);
            }

            await Task.Delay(VerifySettle, cancellationToken).ConfigureAwait(false);
            var retryAfter = _screen.Sample(retryRegion);
            if (retryAfter is null)
            {
                LogVerificationSkipped(target.ProcessName);
                return Applied(retried);
            }

            if (retryBefore.Difference(retryAfter) >= PinchTakenThreshold)
            {
                LogPinchRetried(target.ProcessName, p.Anchor.X, p.Anchor.Y, candidate.X, candidate.Y);
                return Applied(retried);
            }
        }

        // Every anchor was refused, so the whole window blocks the pinch. There is no zoom to remember, and the
        // page's own zoom is the only one it will allow, so this is the one case where a browser may fall back.
        LogPinchBlocked(
            target.ProcessName,
            changed,
            candidates.Count,
            _ctrlWheelWhenPinchBlocked
                ? "Falling back to Ctrl+wheel page zoom."
                : "Set Zoom.Browser.CtrlWheelWhenPinchBlocked to fall back to Ctrl+wheel.");
        return _ctrlWheelWhenPinchBlocked
            ? ZoomInResult.Unhandled
            : ZoomInResult.Handled(ZoomReason.GestureRefused, "pinch blocked by the page");
    }

    // The part of the screen a zoom around the anchor must visibly change.
    internal static PixelRect VerifyRegion(ScreenPoint anchor, PixelRect viewport) =>
        new PixelRect(anchor.X - VerifyHalfWidth, anchor.Y - VerifyHalfHeight, anchor.X + VerifyHalfWidth, anchor.Y + VerifyHalfHeight).Intersect(viewport);

    private static ScreenPoint ClampInto(ScreenPoint point, PixelRect rect) => new(
        PixelRect.ClampWithInset(point.X, rect.Left, rect.Right - 1, EdgeInset),
        PixelRect.ClampWithInset(point.Y, rect.Top, rect.Bottom - 1, EdgeInset));

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
    protected override async Task ZoomOutAsync(TargetInfo target, RestoreState restoreState, CancellationToken cancellationToken)
    {
        var plan = restoreState.Plan;
        await _pinch.PinchAsync(plan.Anchor, RestoreOvershoot / plan.Scale, _animation, restoreState.Bounds, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>What was applied, so the same gesture can be reversed.</summary>
    /// <param name="Plan">Scale and anchor of the zoom-in.</param>
    /// <param name="Bounds">Contact area used for the zoom-in; reused so the zoom-out takes the same orientation.</param>
    public sealed record RestoreState(ZoomPlan Plan, PixelRect Bounds);

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

    [LoggerMessage(Level = LogLevel.Information, Message = "The element under the cursor in {Process} blocks pinch gestures (touch-action), so the same zoom was retried around ({RetryX}, {RetryY}), outside it, instead of around ({AnchorX}, {AnchorY}), and the page took it.")]
    private partial void LogPinchRetried(string? process, int anchorX, int anchorY, int retryX, int retryY);

    [LoggerMessage(Level = LogLevel.Warning, Message = "The page did not zoom: it blocks pinch gestures across the window (touch-action), so the gesture reached the page's own scripts instead ({Changed:P0} of the screen around the cursor changed in {Process}, and {Retries} other anchors were tried). {Fallback}")]
    private partial void LogPinchBlocked(string? process, double changed, int retries, string fallback);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Could not read the screen around the cursor in {Process}, so whether the page took the pinch was not checked.")]
    private partial void LogVerificationSkipped(string? process);
}
