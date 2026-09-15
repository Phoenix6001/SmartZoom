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

    // A contact that lands on the browser's scrollbar drags it instead of pinching; keep clear of the edge.
    private const int ScrollbarAllowance = 24;

    // Contacts sit on a horizontal line, so vertically they only need to be off the very edge.
    private const int VerticalInset = 8;

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

        // The widest spread happens at MaxScale; planning for it keeps every gesture's contacts inside the window.
        _insets = new AnchorInsets(
            X: Math.Max(zoom.Browser.AnchorInsetPx, pinch.MaxContactOffset(zoom.MaxScale) + ScrollbarAllowance),
            Y: VerticalInset);
        _animation = zoom.Animate ? TimeSpan.FromMilliseconds(zoom.Browser.AnimationMs) : TimeSpan.Zero;
    }

    /// <inheritdoc />
    public AdapterKind Kind => AdapterKind.Browser;

    /// <inheritdoc />
    public async Task<ZoomInResult> ZoomInAsync(TargetInfo target, ScreenPoint point, CancellationToken cancellationToken)
    {
        var hit = await _hitTester.HitTestAsync(target, point, cancellationToken).ConfigureAwait(false);
        if (hit is null)
        {
            LogNoContent(target.ProcessName);
            return ZoomInResult.Unhandled;
        }

        var block = _blocks.Select(hit);
        if (block is null)
        {
            LogNoBlock(target.ProcessName, hit.Chain.Count);
            return ZoomInResult.Unhandled;
        }

        var plan = _planner.Plan(block.Bounds, hit.Viewport, point, _insets);
        if (plan is not { } p)
        {
            // Already fills the width: nothing to zoom to. Handled, but nothing to undo either.
            LogAlreadyFits(target.ProcessName, block.Role, block.Bounds.Width, hit.Viewport.Width);
            return ZoomInResult.SelfManaged;
        }

        LogPlan(block.Role, block.Bounds.Width, block.Bounds.Height, hit.Viewport.Width, p.Scale, p.Anchor.X, p.Anchor.Y);

        if (!await _pinch.PinchAsync(p.Anchor, p.Scale, _animation, cancellationToken).ConfigureAwait(false))
        {
            LogPinchRejected(target.ProcessName);
            return ZoomInResult.Unhandled;
        }

        return ZoomInResult.Applied(new RestoreState(p));
    }

    /// <inheritdoc />
    public async Task ZoomOutAsync(TargetInfo target, object restoreState, CancellationToken cancellationToken)
    {
        if (restoreState is not RestoreState state)
            throw new ArgumentException($"Expected {nameof(RestoreState)} from a previous zoom-in.", nameof(restoreState));

        var plan = state.Plan;
        await _pinch.PinchAsync(plan.Anchor, RestoreOvershoot / plan.Scale, _animation, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>What was applied, so the same gesture can be reversed.</summary>
    internal sealed record RestoreState(ZoomPlan Plan);

    [LoggerMessage(Level = LogLevel.Debug, Message = "No accessible content under the cursor in {Process}.")]
    private partial void LogNoContent(string? process);

    [LoggerMessage(Level = LogLevel.Debug, Message = "No zoomable block on the {Depth}-node path under the cursor in {Process}.")]
    private partial void LogNoBlock(string? process, int depth);

    [LoggerMessage(Level = LogLevel.Debug, Message = "{Role} block ({Width} px) already fills the {ViewportWidth} px viewport in {Process}; nothing to zoom.")]
    private partial void LogAlreadyFits(string? process, ContentRole role, int width, int viewportWidth);

    [LoggerMessage(Level = LogLevel.Information, Message = "Smart zoom: {Role} {Width}x{Height} px in {ViewportWidth} px viewport -> x{Scale:F2} around ({AnchorX}, {AnchorY}).")]
    private partial void LogPlan(ContentRole role, int width, int height, int viewportWidth, double scale, int anchorX, int anchorY);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Pinch injection was rejected for {Process}; is the window elevated?")]
    private partial void LogPinchRejected(string? process);
}
