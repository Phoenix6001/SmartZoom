using System.Globalization;

using Microsoft.Extensions.Logging;

using SmartZoom.Core.Input;
using SmartZoom.Core.Routing;
using SmartZoom.Core.Settings;
using SmartZoom.Core.Windows;
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
    private readonly IDisplayScale _display;
    private readonly bool _ctrlWheelWhenPinchBlocked;
    private readonly SmartZoomPlanner _planner;
    private readonly int _anchorInsetPx;
    private readonly TimeSpan _animation;
    private readonly ILogger<BrowserAdapter> _logger;

    /// <summary>Creates the adapter.</summary>
    /// <param name="hitTester">Finds content under the cursor.</param>
    /// <param name="pinch">Performs the zoom gesture.</param>
    /// <param name="screen">Reads the screen, to tell whether the page took the gesture.</param>
    /// <param name="display">The scale of the display a press is on, which the edge allowances follow.</param>
    /// <param name="zoom">Zoom limits and animation preference.</param>
    /// <param name="logger">Logger.</param>
    public BrowserAdapter(IContentHitTester hitTester, IPinchInjector pinch, IScreenSampler screen, IDisplayScale display, ZoomSettings zoom, ILogger<BrowserAdapter> logger)
        : base(Descriptor)
    {
        ArgumentNullException.ThrowIfNull(zoom);

        _hitTester = hitTester ?? throw new ArgumentNullException(nameof(hitTester));
        _pinch = pinch ?? throw new ArgumentNullException(nameof(pinch));
        _screen = screen ?? throw new ArgumentNullException(nameof(screen));
        _display = display ?? throw new ArgumentNullException(nameof(display));
        _ctrlWheelWhenPinchBlocked = zoom.Browser.CtrlWheelWhenPinchBlocked;
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _planner = new SmartZoomPlanner(zoom.MinScale, zoom.MaxScale, zoom.Smart.MarginPx);

        _anchorInsetPx = zoom.Browser.AnchorInsetPx;
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

        // Read where the press is, every press: a window can be dragged to a differently scaled display at any time.
        var edges = EdgeAllowances.For(_display.ScaleAt(point));
        var insets = Insets(edges);

        // A zoom is already on the screen that this process is not remembering - it was restarted, switched off
        // while the page was zoomed, or a restore did not take. It cannot simply be zoomed on top of: Chromium
        // clamps the visual viewport at x4, so the gesture would be refused, and a refused gesture is
        // indistinguishable from a page that blocks gestures (touch-action). That reading sends the press to
        // Ctrl+wheel page zoom, which is a second, separate zoom stacked on the first - the page magnifies twice
        // and the next press takes only one of the two back off. Clear it and read the tree again instead.
        if (hit.PageScale > 1)
        {
            LogForgottenZoom(target.ProcessName, hit.PageScale);
            hit = await ResetAsync(target, point, hit, edges, cancellationToken).ConfigureAwait(false);
            if (hit is null)
            {
                LogNoContent(target.ProcessName);
                return ZoomInResult.Handled(ZoomReason.NoContent);
            }
        }

        // One amount, everywhere on the page. Fitting the block under the cursor to the width of the window
        // sounds right and is not: how much a press zooms then depends on how wide the thing you happened to
        // point at is, so a narrow image grew threefold and a paragraph most of the width of the window grew
        // by a twelfth — the same press doing visibly different things a few pixels apart. The zoom is the
        // amount in the settings, and the cursor is the one point it leaves where it is.
        var plan = _planner.Magnify(hit.Viewport, point, insets);
        if (plan is not { } p)
        {
            // A viewport with no area (the page has gone), or an amount that is not a zoom. Role and size
            // only, via ContentPath.Shape — never Describe's coordinates, and never any text, which
            // ContentNode does not carry in the first place. This is what makes a press that did nothing
            // diagnosable from a bug report without the report ever containing what was on the page.
            LogNoContent(target.ProcessName);
            return ZoomInResult.Handled(ZoomReason.NoContent, ContentPath.Shape(hit.Chain));
        }

        LogPlan(target.ProcessName, p.Scale, p.Anchor.X, p.Anchor.Y);

        var bounds = edges.ContactBounds(hit.Viewport);

        // The first gesture is the zoom itself: Edge renders a pinch-out below 1.0 as a visible
        // shrink-and-rebound, so no preparatory gesture may precede it (see docs/decisions.md, "Browsers").
        var first = await PinchAndVerifyAsync(target, p.Anchor, p.Scale, hit.Viewport, bounds, cancellationToken).ConfigureAwait(false);
        if (first.Outcome == PinchOutcome.NotSent)
            return ZoomInResult.Handled(ZoomReason.GestureRefused);

        if (first.Outcome == PinchOutcome.Took)
            return Applied(new RestoreState(p, bounds));

        // The gesture went in cleanly and nothing moved: the element under the cursor kept it
        // (touch-action: none). The rest of the page usually does not, and a pinch scales the whole visual
        // viewport around its anchor, so the same zoom is available from an anchor outside that element. At
        // most two such retries. The element that refused is whatever sits under the anchor; a point rather
        // than a rectangle still puts every candidate the required clearance away from it.
        var refused = new PixelRect(p.Anchor.X, p.Anchor.Y, p.Anchor.X, p.Anchor.Y);
        var candidates = RetryAnchor.Candidates(refused, hit.Viewport, p.Anchor, edges);
        foreach (var candidate in candidates)
        {
            var retry = await PinchAndVerifyAsync(target, candidate, p.Scale, hit.Viewport, bounds, cancellationToken).ConfigureAwait(false);
            if (retry.Outcome == PinchOutcome.NotSent)
                return ZoomInResult.Handled(ZoomReason.GestureRefused);

            if (retry.Outcome == PinchOutcome.Took)
            {
                LogPinchRetried(target.ProcessName, p.Anchor.X, p.Anchor.Y, candidate.X, candidate.Y);

                // The zoom-out has to reverse the gesture that actually happened, not the one that was refused.
                return Applied(new RestoreState(p with { Anchor = candidate }, bounds));
            }
        }

        // Nothing has moved after three gestures, and there are two reasons for that, not one: a page that
        // blocks pinch gestures, and a page ALREADY at Chromium's x4 visual-viewport ceiling, which has no
        // room left to magnify and so refuses in exactly the same way. The second is a zoom this process does
        // not remember, and the check at the top of this method cannot always see it — Chromium only bakes the
        // scale into its accessibility tree when it next re-serializes it, so a page whose tree still reads 1.0
        // slips through. Telling them apart is worth one more gesture, because getting it wrong is expensive:
        // page zoom would stack a second, separate zoom on top of the first, and the next press would take
        // only one of the two back off.
        var cleared = await ResetAsync(target, point, hit, edges, cancellationToken).ConfigureAwait(false);
        if (cleared is not null && _planner.Magnify(cleared.Viewport, point, insets) is { } replanned)
        {
            var clearedBounds = edges.ContactBounds(cleared.Viewport);
            var afterReset = await PinchAndVerifyAsync(target, replanned.Anchor, replanned.Scale, cleared.Viewport, clearedBounds, cancellationToken).ConfigureAwait(false);
            if (afterReset.Outcome == PinchOutcome.Took)
            {
                LogZoomedAfterClearing(target.ProcessName);
                return Applied(new RestoreState(replanned, clearedBounds));
            }
        }

        // It really is the page. There is no zoom to remember, and the page's own zoom is the only one it will
        // allow, so this is the one case where a browser may fall back.
        LogPinchBlocked(
            target.ProcessName,
            first.Changed,
            candidates.Count,
            _ctrlWheelWhenPinchBlocked
                ? "Falling back to Ctrl+wheel page zoom."
                : "Set Zoom.Browser.CtrlWheelWhenPinchBlocked to fall back to Ctrl+wheel.");
        return _ctrlWheelWhenPinchBlocked
            ? ZoomInResult.Unhandled
            : ZoomInResult.Handled(ZoomReason.GestureRefused, "pinch blocked by the page");
    }

    /// <summary>What one gesture did.</summary>
    private enum PinchOutcome
    {
        /// <summary>The injector would not send it at all.</summary>
        NotSent,

        /// <summary>The screen changed, or could not be read, so the gesture is trusted.</summary>
        Took,

        /// <summary>It went in cleanly and nothing moved.</summary>
        Kept,
    }

    /// <summary>
    /// Sends one gesture and decides whether the page took it, by comparing the screen around the anchor
    /// before and after.
    /// </summary>
    /// <returns>What happened, and how much of the sampled region changed.</returns>
    private async Task<(PinchOutcome Outcome, double Changed)> PinchAndVerifyAsync(
        TargetInfo target,
        ScreenPoint anchor,
        double scale,
        PixelRect viewport,
        PixelRect bounds,
        CancellationToken cancellationToken)
    {
        var region = VerifyRegion(anchor, viewport);
        var before = _screen.Sample(region);

        if (!await _pinch.PinchAsync(anchor, scale, _animation, bounds, cancellationToken).ConfigureAwait(false))
        {
            LogPinchRejected(target.ProcessName);
            return (PinchOutcome.NotSent, 0);
        }

        if (before is null)
        {
            LogVerificationSkipped(target.ProcessName);
            return (PinchOutcome.Took, 0);
        }

        await Task.Delay(VerifySettle, cancellationToken).ConfigureAwait(false);
        var after = _screen.Sample(region);
        if (after is null)
        {
            LogVerificationSkipped(target.ProcessName);
            return (PinchOutcome.Took, 0);
        }

        var changed = before.Difference(after);
        if (changed >= PinchTakenThreshold)
            return (PinchOutcome.Took, changed);

        // A region with less structure in it than the change being looked for cannot show that change, so a
        // gesture that worked perfectly would read as one the page kept.
        if (Blind(before, after))
        {
            LogVerificationBlind(target.ProcessName);
            return (PinchOutcome.Took, changed);
        }

        return (PinchOutcome.Kept, changed);
    }

    // The part of the screen a zoom around the anchor must visibly change.
    internal static PixelRect VerifyRegion(ScreenPoint anchor, PixelRect viewport) =>
        new PixelRect(anchor.X - VerifyHalfWidth, anchor.Y - VerifyHalfHeight, anchor.X + VerifyHalfWidth, anchor.Y + VerifyHalfHeight).Intersect(viewport);

    /// <summary>
    /// Whether the screen around the anchor is too plain for the before/after comparison to mean anything: a
    /// wide margin, an empty panel, a flat image, or a window that reads back black because it is protected.
    /// </summary>
    /// <remarks>
    /// Content can move anywhere inside a region with no structure without changing a cell, so a difference of
    /// 0 there is not evidence of anything. Treating it as one is how a zoom that worked used to be read as a
    /// page that blocks gestures — which sent two more pinches at the same scale on top of it and then a
    /// Ctrl+wheel page zoom on top of that, leaving a page the next press could not undo. Trusting the gesture
    /// instead costs, at worst, one wasted press on a page that really did refuse it.
    /// </remarks>
    private static bool Blind(ScreenSample before, ScreenSample after) => !before.HasDetail && !after.HasDetail;

    /// <summary>
    /// Clears a pinch zoom the page is carrying and re-reads the tree underneath it. Zooming out below 1.0 is
    /// invisible on a page that is at rest and resets one that is not.
    /// </summary>
    /// <remarks>
    /// <see cref="TimeSpan.Zero"/> is deliberate: this is a state reset nobody sees happen, not a gesture, so
    /// it must not appear in the gesture-health totals (the injector excludes zero-duration pinches for exactly
    /// that reason).
    /// </remarks>
    private async Task<ContentHit?> ResetAsync(TargetInfo target, ScreenPoint point, ContentHit hit, EdgeAllowances edges, CancellationToken cancellationToken)
    {
        await _pinch.PinchAsync(ClampInto(point, hit.Viewport, Insets(edges)), RestoreOvershoot / _planner.MaxScale, TimeSpan.Zero, edges.ContactBounds(hit.Viewport), cancellationToken).ConfigureAwait(false);
        await Task.Delay(ResetSettle, cancellationToken).ConfigureAwait(false);
        return await _hitTester.HitTestAsync(target, point, cancellationToken).ConfigureAwait(false);
    }

    private static ScreenPoint ClampInto(ScreenPoint point, PixelRect rect, AnchorInsets insets) => new(
        PixelRect.ClampWithInset(point.X, rect.Left, rect.Right - 1, insets.X),
        PixelRect.ClampWithInset(point.Y, rect.Top, rect.Bottom - 1, insets.Y));

    // How far in the anchor stays (see EdgeAllowances.AnchorInset and AnchorInsetVertical), or further in when
    // the settings file asks for more.
    private AnchorInsets Insets(EdgeAllowances edges) => new(
        Math.Max(edges.AnchorInset, _anchorInsetPx),
        Math.Max(edges.AnchorInsetVertical, _anchorInsetPx));

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

    [LoggerMessage(Level = LogLevel.Debug, Message = "Path under the cursor (leaf first) in a {ViewportWidth}x{ViewportHeight} viewport: {Path}")]
    private partial void LogPath(string path, int viewportWidth, int viewportHeight);

    [LoggerMessage(Level = LogLevel.Information, Message = "Smart zoom in {Process}: x{Scale:F2} around ({AnchorX}, {AnchorY}).")]
    private partial void LogPlan(string? process, double scale, int anchorX, int anchorY);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Pinch injection was rejected for {Process}; is the window elevated?")]
    private partial void LogPinchRejected(string? process);

    [LoggerMessage(Level = LogLevel.Information, Message = "The page in {Process} was already zoomed as far as it goes; cleared it and zoomed again rather than stacking page zoom on top.")]
    private partial void LogZoomedAfterClearing(string? process);

    [LoggerMessage(Level = LogLevel.Information, Message = "The element under the cursor in {Process} blocks pinch gestures (touch-action), so the same zoom was retried around ({RetryX}, {RetryY}), outside it, instead of around ({AnchorX}, {AnchorY}), and the page took it.")]
    private partial void LogPinchRetried(string? process, int anchorX, int anchorY, int retryX, int retryY);

    [LoggerMessage(Level = LogLevel.Warning, Message = "The page did not zoom: it blocks pinch gestures across the window (touch-action), so the gesture reached the page's own scripts instead ({Changed:P0} of the screen around the cursor changed in {Process}, and {Retries} other anchors were tried). {Fallback}")]
    private partial void LogPinchBlocked(string? process, double changed, int retries, string fallback);

    [LoggerMessage(Level = LogLevel.Information, Message = "The page in {Process} is still showing a x{Scale:F2} zoom SmartZoom does not remember making; clearing it before zooming again.")]
    private partial void LogForgottenZoom(string? process, double scale);

    [LoggerMessage(Level = LogLevel.Debug, Message = "The screen around the cursor in {Process} has too little in it to show whether the page took the pinch, so the gesture is trusted.")]
    private partial void LogVerificationBlind(string? process);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Could not read the screen around the cursor in {Process}, so whether the page took the pinch was not checked.")]
    private partial void LogVerificationSkipped(string? process);
}
