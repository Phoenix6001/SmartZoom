using SmartZoom.Core.Input;
using SmartZoom.Core.Routing;
using SmartZoom.Core.Settings;
using SmartZoom.Core.Zoom;

namespace SmartZoom.Core.Tests.Zoom;

public sealed class CtrlWheelAdapterTests
{
    private static readonly TargetInfo Target = new(0x1000, 0x1001, 42, "SumatraPDF", "SUMATRA_PDF_FRAME", "SUMATRA_PDF_CANVAS");
    private static readonly ScreenPoint Point = new(100, 200);

    private readonly FakeInputInjector _injector = new();

    private CtrlWheelAdapter Create(int ticks = 3) =>
        new(_injector, new CtrlWheelSettings { Ticks = ticks, IntervalMs = 0 }, TimeProvider.System);

    [Fact]
    public async Task Zoom_in_holds_ctrl_around_a_burst_of_upward_ticks()
    {
        var result = await Create().ZoomInAsync(Target, Point, CancellationToken.None);

        Assert.Equal("Ctrl↓ +1 +1 +1 Ctrl↑", _injector.Script);
        Assert.Equal(ZoomInStatus.Applied, result.Status);
        Assert.Equal(new CtrlWheelAdapter.RestoreState(3), result.RestoreState);
    }

    [Fact]
    public async Task Zoom_out_reverses_exactly_the_ticks_that_were_delivered()
    {
        var adapter = Create(4);
        var result = await adapter.ZoomInAsync(Target, Point, CancellationToken.None);
        _injector.Log.Clear();

        await adapter.ZoomOutAsync(Target, result.RestoreState!, CancellationToken.None);

        Assert.Equal("Ctrl↓ -1 -1 -1 -1 Ctrl↑", _injector.Script);
    }

    [Fact]
    public async Task Ctrl_is_released_even_when_a_tick_fails_mid_burst()
    {
        _injector.FailWheelAt.Add(1);

        var result = await Create(3).ZoomInAsync(Target, Point, CancellationToken.None);

        Assert.Equal("Ctrl↓ +1 Ctrl↑", _injector.Script);
        Assert.Equal(new CtrlWheelAdapter.RestoreState(1), result.RestoreState);
    }

    [Fact]
    public async Task Reports_unhandled_when_nothing_could_be_delivered()
    {
        _injector.FailWheelAt.Add(0);

        var result = await Create().ZoomInAsync(Target, Point, CancellationToken.None);

        Assert.Equal(ZoomInStatus.Unhandled, result.Status);
        Assert.Equal("Ctrl↓ Ctrl↑", _injector.Script);
    }

    [Fact]
    public async Task Reports_unhandled_and_sends_nothing_when_ctrl_cannot_be_pressed()
    {
        _injector.FailModifierDown = true;

        var result = await Create().ZoomInAsync(Target, Point, CancellationToken.None);

        Assert.Equal(ZoomInStatus.Unhandled, result.Status);
        Assert.Empty(_injector.Log);
    }

    [Fact]
    public async Task Does_not_touch_ctrl_when_the_user_is_already_holding_it()
    {
        _injector.ControlPhysicallyDown = true;

        await Create(2).ZoomInAsync(Target, Point, CancellationToken.None);

        Assert.Equal("+1 +1", _injector.Script);
    }

    [Fact]
    public async Task Cancellation_between_ticks_still_releases_ctrl()
    {
        using var cts = new CancellationTokenSource();
        var adapter = new CtrlWheelAdapter(_injector, new CtrlWheelSettings { Ticks = 5, IntervalMs = 10_000 }, TimeProvider.System);
        cts.CancelAfter(50);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => adapter.ZoomInAsync(Target, Point, cts.Token));

        Assert.Equal("Ctrl↓ +1 Ctrl↑", _injector.Script);
    }

    [Fact]
    public async Task Zoom_out_rejects_foreign_restore_state()
    {
        await Assert.ThrowsAsync<ArgumentException>(() => Create().ZoomOutAsync(Target, "not ours", CancellationToken.None));
        Assert.Empty(_injector.Log);
    }
}
