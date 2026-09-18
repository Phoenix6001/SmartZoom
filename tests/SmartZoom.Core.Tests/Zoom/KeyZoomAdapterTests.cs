using Microsoft.Extensions.Logging.Abstractions;
using SmartZoom.Core.Input;
using SmartZoom.Core.Routing;
using SmartZoom.Core.Settings;
using SmartZoom.Core.Zoom;

namespace SmartZoom.Core.Tests.Zoom;

public sealed class KeyZoomAdapterTests
{
    private static readonly TargetInfo Acrobat = new(0x300, 0x301, 13, "Acrobat", "AcrobatSDIWindow", "AVL_AVView");
    private static readonly ScreenPoint Cursor = new(800, 600);

    private readonly FakeInputInjector _injector = new();
    private readonly FakeActivator _activator = new();

    private KeyZoomAdapter Create() =>
        new(_injector, _activator, new KeyZoomSettings { ZoomInKeys = "Ctrl+2", ZoomOutKeys = "Ctrl+0" }, TimeProvider.System, NullLogger<KeyZoomAdapter>.Instance);

    [Fact]
    public async Task Modifiers_the_user_still_holds_from_a_hotkey_trigger_are_neither_pressed_nor_released()
    {
        _injector.HeldModifiers = KeyModifiers.Control;

        var result = await Create().ZoomInAsync(Acrobat, Cursor, CancellationToken.None);

        Assert.Equal(ZoomInStatus.Applied, result.Status);
        Assert.Equal("2", _injector.Script);
    }

    [Fact]
    public async Task Waits_for_a_foreign_modifier_to_be_released_before_sending()
    {
        _injector.HeldModifiers = KeyModifiers.Control | KeyModifiers.Alt;
        _injector.ReleaseHeldAfterPolls = 3;

        var result = await Create().ZoomInAsync(Acrobat, Cursor, CancellationToken.None);

        Assert.Equal(ZoomInStatus.Applied, result.Status);
        Assert.Equal("Ctrl+2", _injector.Script);
        Assert.True(_injector.ModifierPolls >= 4);
    }

    [Fact]
    public async Task Foreign_modifier_held_past_the_timeout_means_nothing_is_sent()
    {
        _injector.HeldModifiers = KeyModifiers.Alt;
        var adapter = new KeyZoomAdapter(_injector, _activator, new KeyZoomSettings(), TimeProvider.System, NullLogger<KeyZoomAdapter>.Instance, TimeSpan.FromMilliseconds(50));

        var result = await adapter.ZoomInAsync(Acrobat, Cursor, CancellationToken.None);

        Assert.Equal(ZoomInStatus.Unhandled, result.Status);
        Assert.Empty(_injector.Log);
    }

    [Fact]
    public async Task Zoom_in_focuses_the_window_and_sends_the_fit_width_shortcut()
    {
        var result = await Create().ZoomInAsync(Acrobat, Cursor, CancellationToken.None);

        Assert.Equal(ZoomInStatus.Applied, result.Status);
        Assert.Equal([0x300L], _activator.Activated);
        Assert.Equal("Ctrl+2", _injector.Script);
    }

    [Fact]
    public async Task The_window_that_was_in_front_is_brought_back_after_the_shortcut()
    {
        // Otherwise the reader stays on top of an overlapping browser and catches the user's next trigger.
        _activator.ForegroundWindow = 0x999;

        await Create().ZoomInAsync(Acrobat, Cursor, CancellationToken.None);

        Assert.Equal([0x300L, 0x999L], _activator.Activated);
        Assert.Equal(0x999, _activator.ForegroundWindow);
        Assert.Equal("Ctrl+2", _injector.Script);
    }

    [Fact]
    public async Task Zoom_out_brings_the_previous_window_back_too()
    {
        var adapter = Create();
        var result = await adapter.ZoomInAsync(Acrobat, Cursor, CancellationToken.None);
        _activator.ForegroundWindow = 0x777;

        await adapter.ZoomOutAsync(Acrobat, result.RestoreState!, CancellationToken.None);

        Assert.Equal([0x300L, 0x300L, 0x777L], _activator.Activated);
    }

    [Fact]
    public async Task A_target_already_in_front_is_left_in_front()
    {
        _activator.ForegroundWindow = 0x300;

        await Create().ZoomInAsync(Acrobat, Cursor, CancellationToken.None);

        Assert.Equal([0x300L], _activator.Activated);
    }

    [Fact]
    public async Task No_foreground_window_means_nothing_to_bring_back()
    {
        _activator.ForegroundWindow = 0;

        await Create().ZoomInAsync(Acrobat, Cursor, CancellationToken.None);

        Assert.Equal([0x300L], _activator.Activated);
    }

    [Fact]
    public async Task The_previous_window_is_brought_back_even_when_the_shortcut_is_rejected()
    {
        _activator.ForegroundWindow = 0x999;
        _injector.FailKeys = true;

        var result = await Create().ZoomInAsync(Acrobat, Cursor, CancellationToken.None);

        Assert.Equal(ZoomInStatus.Unhandled, result.Status);
        Assert.Equal([0x300L, 0x999L], _activator.Activated);
    }

    [Fact]
    public async Task Failing_to_bring_the_previous_window_back_does_not_undo_the_zoom()
    {
        _activator.ForegroundWindow = 0x999;
        _activator.RestoreSucceeds = false;

        var result = await Create().ZoomInAsync(Acrobat, Cursor, CancellationToken.None);

        Assert.Equal(ZoomInStatus.Applied, result.Status);
        Assert.Equal([0x300L, 0x999L], _activator.Activated);
    }

    [Fact]
    public async Task Zoom_out_sends_the_fit_page_shortcut()
    {
        var adapter = Create();
        var result = await adapter.ZoomInAsync(Acrobat, Cursor, CancellationToken.None);

        await adapter.ZoomOutAsync(Acrobat, result.RestoreState!, CancellationToken.None);

        Assert.Equal("Ctrl+2 Ctrl+0", _injector.Script);
    }

    [Fact]
    public async Task Window_that_cannot_be_focused_is_unhandled_and_nothing_is_sent()
    {
        _activator.Succeeds = false;

        var result = await Create().ZoomInAsync(Acrobat, Cursor, CancellationToken.None);

        Assert.Equal(ZoomInStatus.Unhandled, result.Status);
        Assert.Empty(_injector.Log);
    }

    [Fact]
    public async Task Rejected_keys_are_unhandled()
    {
        _injector.FailKeys = true;

        Assert.Equal(ZoomInStatus.Unhandled, (await Create().ZoomInAsync(Acrobat, Cursor, CancellationToken.None)).Status);
    }

    [Fact]
    public void Invalid_shortcut_text_is_rejected_at_construction() =>
        Assert.Throws<FormatException>(() => new KeyZoomAdapter(_injector, _activator, new KeyZoomSettings { ZoomInKeys = "Ctrl+" }, TimeProvider.System, NullLogger<KeyZoomAdapter>.Instance));

    [Fact]
    public async Task Zoom_out_rejects_foreign_state() =>
        await Assert.ThrowsAsync<ArgumentException>(() => Create().ZoomOutAsync(Acrobat, "nope", CancellationToken.None));

    private sealed class FakeActivator : IWindowActivator
    {
        public bool Succeeds { get; set; } = true;

        /// <summary>Whether bringing back a window other than the target succeeds.</summary>
        public bool RestoreSucceeds { get; set; } = true;

        public List<long> Activated { get; } = [];

        public nint ForegroundWindow { get; set; }

        public bool TryActivate(nint rootWindow)
        {
            Activated.Add(rootWindow);
            var ok = rootWindow == Acrobat.RootWindow ? Succeeds : RestoreSucceeds;
            if (ok)
                ForegroundWindow = rootWindow;
            return ok;
        }
    }
}
