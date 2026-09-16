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

    // The anchor itself only needs to be off the very edge; the injector orients the contacts to fit.
    private const int EdgeInset = 8;

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
            LogNoBlock(target.ProcessName, hit.Chain.Count);
            return ZoomInResult.SelfManaged;
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

    // Where synthetic contacts may land: the viewport minus the vertical scrollbar strip on the right.
    private static PixelRect ContactBounds(PixelRect viewport) =>
        viewport with { Right = Math.Max(viewport.Left + 1, viewport.Right - ScrollbarAllowance) };

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

    [LoggerMessage(Level = LogLevel.Debug, Message = "{Role} block ({Width} px) already fills the {ViewportWidth} px viewport in {Process}; nothing to zoom.")]
    private partial void LogAlreadyFits(string? process, ContentRole role, int width, int viewportWidth);

    [LoggerMessage(Level = LogLevel.Information, Message = "Smart zoom: {Role} {Width}x{Height} px in {ViewportWidth} px viewport -> x{Scale:F2} around ({AnchorX}, {AnchorY}).")]
    private partial void LogPlan(ContentRole role, int width, int height, int viewportWidth, double scale, int anchorX, int anchorY);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Pinch injection was rejected for {Process}; is the window elevated?")]
    private partial void LogPinchRejected(string? process);
}
