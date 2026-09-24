using Microsoft.Extensions.Logging.Abstractions;

using SmartZoom.Core.Input;
using SmartZoom.Core.Routing;
using SmartZoom.Core.Settings;
using SmartZoom.Core.Zoom;

namespace SmartZoom.Core.Tests.Zoom;

public sealed class ZoomCoordinatorTests
{
    private static readonly ScreenPoint Point = new(10, 10);
    private static readonly TargetInfo IrfanView = new(0x100, 0x101, 1, "i_view64", "R", "H");
    private static readonly TargetInfo Browser = new(0x200, 0x201, 2, "chrome", "R", "H");
    private static readonly TargetInfo Word = new(0x300, 0x301, 3, "WINWORD", "R", "H");
    private static readonly TargetInfo Unknown = new(0x400, 0x401, 4, "notepad", "R", "H");

    private readonly ScriptedAdapter _ctrlWheel = new(CtrlWheelAdapter.Descriptor);
    private readonly ScriptedAdapter _browser = new(BrowserAdapter.Descriptor);
    private readonly WindowZoomStateStore _store = new();
    private readonly FakeWindowInspector _windows = new();

    private ZoomCoordinator Create(bool fallback = true, IDictionary<string, AdapterId>? apps = null)
    {
        var adapters = new[] { _ctrlWheel, _browser };
        var routing = new RoutingSettings { Apps = apps ?? new Dictionary<string, AdapterId>(StringComparer.OrdinalIgnoreCase) };

        return new ZoomCoordinator(
            new ZoomRouter(adapters.Select(a => a.Descriptor), routing, NullLogger<ZoomRouter>.Instance),
            adapters,
            _store,
            _windows,
            fallback,
            NullLogger<ZoomCoordinator>.Instance);
    }

    [Fact]
    public async Task First_trigger_zooms_in_second_zooms_out_with_the_saved_state()
    {
        var coordinator = Create();

        Assert.Equal(ZoomAction.ZoomedIn, (await coordinator.HandleTriggerAsync(IrfanView, Point, CancellationToken.None)).Action);
        Assert.Equal(ZoomAction.ZoomedOut, (await coordinator.HandleTriggerAsync(IrfanView, Point, CancellationToken.None)).Action);
        Assert.Equal(ZoomAction.ZoomedIn, (await coordinator.HandleTriggerAsync(IrfanView, Point, CancellationToken.None)).Action);

        Assert.Equal(["in", "out:state-1", "in"], _ctrlWheel.Calls);
        Assert.Equal(1, _store.Count);
    }

    [Fact]
    public async Task State_is_tracked_per_window()
    {
        var coordinator = Create();
        var otherWindow = IrfanView with { RootWindow = 0x110, HitWindow = 0x111 };

        await coordinator.HandleTriggerAsync(IrfanView, Point, CancellationToken.None);
        Assert.Equal(ZoomAction.ZoomedIn, (await coordinator.HandleTriggerAsync(otherWindow, Point, CancellationToken.None)).Action);
        Assert.Equal(ZoomAction.ZoomedOut, (await coordinator.HandleTriggerAsync(IrfanView, Point, CancellationToken.None)).Action);
        Assert.Equal(ZoomAction.ZoomedOut, (await coordinator.HandleTriggerAsync(otherWindow, Point, CancellationToken.None)).Action);
    }

    [Fact]
    public async Task Unknown_process_is_ignored()
    {
        Assert.Equal(ZoomAction.Ignored, (await Create().HandleTriggerAsync(Unknown, Point, CancellationToken.None)).Action);
        Assert.Empty(_ctrlWheel.Calls);
    }

    [Fact]
    public async Task Process_routed_to_an_adapter_this_build_does_not_have_falls_back_instead_of_doing_nothing()
    {
        // The PowerPoint bug class: settings named a strategy nobody implements, and a press there did
        // nothing at all - no zoom, no fallback, no warning.
        var apps = new Dictionary<string, AdapterId>(StringComparer.OrdinalIgnoreCase) { ["WINWORD"] = new("WordCom") };

        Assert.Equal(ZoomAction.ZoomedIn, (await Create(apps: apps).HandleTriggerAsync(Word, Point, CancellationToken.None)).Action);
        Assert.Equal(["in"], _ctrlWheel.Calls);
    }

    [Fact]
    public async Task An_application_switched_off_with_None_is_ignored()
    {
        var apps = new Dictionary<string, AdapterId>(StringComparer.OrdinalIgnoreCase) { ["chrome"] = AdapterId.None };

        Assert.Equal(ZoomAction.Ignored, (await Create(apps: apps).HandleTriggerAsync(Browser, Point, CancellationToken.None)).Action);
        Assert.Empty(_browser.Calls);
        Assert.Empty(_ctrlWheel.Calls);
    }

    [Fact]
    public async Task A_handled_result_is_not_remembered_so_the_next_press_asks_the_adapter_again()
    {
        _browser.Result = ZoomInResult.Handled(ZoomReason.AlreadyFits);
        var coordinator = Create();

        Assert.Equal(ZoomAction.Handled, (await coordinator.HandleTriggerAsync(Browser, Point, CancellationToken.None)).Action);
        Assert.Equal(ZoomAction.Handled, (await coordinator.HandleTriggerAsync(Browser, Point, CancellationToken.None)).Action);

        Assert.Equal(["in", "in"], _browser.Calls);
        Assert.Equal(0, _store.Count);
    }

    [Fact]
    public async Task An_adapter_that_did_nothing_is_not_reported_as_a_zoom()
    {
        _browser.Result = ZoomInResult.Handled(ZoomReason.NoBlock);
        var coordinator = Create();

        var outcome = await coordinator.HandleTriggerAsync(Browser, Point, CancellationToken.None);

        Assert.Equal(ZoomAction.Handled, outcome.Action);
        Assert.Equal(ZoomReason.NoBlock, outcome.Reason);
    }

    [Fact]
    public async Task Falls_back_to_ctrl_wheel_when_the_routed_adapter_cannot_handle_the_target()
    {
        _browser.Result = ZoomInResult.Unhandled;
        var coordinator = Create();

        Assert.Equal(ZoomAction.ZoomedIn, (await coordinator.HandleTriggerAsync(Browser, Point, CancellationToken.None)).Action);
        Assert.Equal(ZoomAction.ZoomedOut, (await coordinator.HandleTriggerAsync(Browser, Point, CancellationToken.None)).Action);

        Assert.Equal(["in"], _browser.Calls);
        Assert.Equal(["in", "out:state-1"], _ctrlWheel.Calls);
    }

    [Fact]
    public async Task Fallback_can_be_disabled()
    {
        _browser.Result = ZoomInResult.Unhandled;

        Assert.Equal(ZoomAction.Unhandled, (await Create(fallback: false).HandleTriggerAsync(Browser, Point, CancellationToken.None)).Action);
        Assert.Empty(_ctrlWheel.Calls);
    }

    [Fact]
    public async Task An_adapter_that_could_not_act_says_so_rather_than_leaving_the_reason_empty()
    {
        _browser.Result = ZoomInResult.Unhandled;

        var outcome = await Create(fallback: false).HandleTriggerAsync(Browser, Point, CancellationToken.None);

        // The reason is what the diagnostics record counts under. Only the browser and the Office adapters
        // ever supply one of their own, so without this every no-op press in the Reader and Ctrl+wheel paths
        // would be counted with no reason at all.
        Assert.Equal(ZoomAction.Unhandled, outcome.Action);
        Assert.Equal(ZoomReason.AdapterCouldNotAct, outcome.Reason);
    }

    [Fact]
    public async Task Every_outcome_that_zoomed_nothing_carries_a_reason()
    {
        _ctrlWheel.Result = ZoomInResult.Unhandled;
        var coordinator = Create();

        ZoomOutcome[] outcomes =
        [
            await coordinator.HandleTriggerAsync(IrfanView, Point, CancellationToken.None),
            await coordinator.HandleTriggerAsync(Unknown, Point, CancellationToken.None),
        ];

        Assert.All(outcomes, outcome =>
        {
            Assert.True(outcome.Action is ZoomAction.Unhandled or ZoomAction.Ignored or ZoomAction.Handled);
            Assert.NotNull(outcome.Reason);
        });
    }

    [Fact]
    public async Task Closed_window_state_is_pruned_so_the_next_trigger_zooms_in()
    {
        var coordinator = Create();
        await coordinator.HandleTriggerAsync(IrfanView, Point, CancellationToken.None);

        _windows.Dead.Add(IrfanView.RootWindow);

        Assert.Equal(ZoomAction.ZoomedIn, (await coordinator.HandleTriggerAsync(IrfanView, Point, CancellationToken.None)).Action);
        Assert.Equal(["in", "in"], _ctrlWheel.Calls);
    }

    [Fact]
    public async Task An_undo_that_fails_keeps_the_window_remembered_as_zoomed_so_the_next_press_tries_again()
    {
        var coordinator = Create();
        await coordinator.HandleTriggerAsync(IrfanView, Point, CancellationToken.None);
        _ctrlWheel.ZoomOutThrows = true;

        await Assert.ThrowsAsync<InvalidOperationException>(() => coordinator.HandleTriggerAsync(IrfanView, Point, CancellationToken.None));

        // The window is still zoomed, so its memory must survive the failure.
        Assert.Equal(1, _store.Count);

        _ctrlWheel.ZoomOutThrows = false;
        Assert.Equal(ZoomAction.ZoomedOut, (await coordinator.HandleTriggerAsync(IrfanView, Point, CancellationToken.None)).Action);
        Assert.Equal(["in", "out:state-1", "out:state-1"], _ctrlWheel.Calls);
        Assert.Equal(0, _store.Count);
    }

    [Fact]
    public async Task Ctrl_wheel_failure_leaves_no_state_behind()
    {
        _ctrlWheel.Result = ZoomInResult.Unhandled;

        Assert.Equal(ZoomAction.Unhandled, (await Create().HandleTriggerAsync(IrfanView, Point, CancellationToken.None)).Action);
        Assert.Equal(0, _store.Count);
    }

    private sealed class ScriptedAdapter(AdapterDescriptor descriptor) : IZoomAdapter
    {
        private int _zoomIns;

        public AdapterDescriptor Descriptor => descriptor;

        public Type RestoreType { get; set; } = typeof(string);

        /// <summary>Null means "Applied with a fresh state object each time".</summary>
        public ZoomInResult? Result { get; set; }

        /// <summary>Whether undoing throws, as a real adapter does when the application is gone or busy.</summary>
        public bool ZoomOutThrows { get; set; }

        public List<string> Calls { get; } = [];

        public Task<ZoomInResult> ZoomInAsync(TargetInfo target, ScreenPoint point, CancellationToken cancellationToken)
        {
            Calls.Add("in");
            return Task.FromResult(Result ?? ZoomInResult.Applied($"state-{++_zoomIns}"));
        }

        public Task ZoomOutAsync(TargetInfo target, object restoreState, CancellationToken cancellationToken)
        {
            Calls.Add($"out:{restoreState}");
            return ZoomOutThrows ? throw new InvalidOperationException("the undo failed") : Task.CompletedTask;
        }
    }

    private sealed class FakeWindowInspector : IWindowInspector
    {
        public HashSet<nint> Dead { get; } = [];

        public TargetInfo? GetTargetAt(ScreenPoint point) => throw new NotSupportedException();

        public bool IsWindowAlive(nint window, uint processId) => !Dead.Contains(window);
    }
}
