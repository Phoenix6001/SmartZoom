using Microsoft.Extensions.Logging.Abstractions;
using SmartZoom.Core.Input;
using SmartZoom.Core.Routing;
using SmartZoom.Core.Settings;
using SmartZoom.Core.Zoom;
using SmartZoom.Core.Zoom.Content;

namespace SmartZoom.Core.Tests.Zoom;

public sealed class KeyZoomAdapterTests
{
    private static readonly TargetInfo Acrobat = new(0x300, 0x301, 13, "Acrobat", "AcrobatSDIWindow", "AVL_AVView");
    private static readonly ScreenPoint Cursor = new(800, 600);

    private readonly FakeInputInjector _injector = new();
    private readonly FakeActivator _activator = new();
    private readonly FakeReaderView _view = new();
    private readonly FakePinchInjector _pinch = new();

    /// <summary>The shortcut adapter: gestures off, which is how a reader that ignores touch is configured.</summary>
    private KeyZoomAdapter Create(bool followCursor = false) =>
        new(_injector, _activator, _view, _pinch,
            new ReaderZoomSettings { ZoomInKeys = "Ctrl+2", ZoomOutKeys = "Ctrl+0", Gesture = false, FollowCursor = followCursor, MarginPx = 16 },
            TimeSpan.Zero, TimeProvider.System, NullLogger<KeyZoomAdapter>.Instance);

    private KeyZoomAdapter CreateGesturing(double scale = 2.0) =>
        new(_injector, _activator, _view, _pinch,
            new ReaderZoomSettings { ZoomInKeys = "Ctrl+2", ZoomOutKeys = "Ctrl+0", Gesture = true, Scale = scale },
            TimeSpan.FromMilliseconds(200), TimeProvider.System, NullLogger<KeyZoomAdapter>.Instance);

    [Fact]
    public async Task A_gesture_magnifies_around_the_cursor_and_sends_no_shortcut()
    {
        var result = await CreateGesturing().ZoomInAsync(Acrobat, Cursor, CancellationToken.None);

        Assert.Equal(ZoomInStatus.Applied, result.Status);
        Assert.Equal([(Cursor, 2.0)], _pinch.Gestures);
        Assert.Empty(_injector.Log);
        Assert.Empty(_view.Requests);
    }

    [Fact]
    public async Task The_second_press_takes_the_same_magnification_back_off()
    {
        var adapter = CreateGesturing(scale: 2.5);
        var result = await adapter.ZoomInAsync(Acrobat, Cursor, CancellationToken.None);

        await adapter.ZoomOutAsync(Acrobat, result.RestoreState!, CancellationToken.None);

        Assert.Equal([(Cursor, 2.5), (Cursor, 1 / 2.5)], _pinch.Gestures);
    }

    [Fact]
    public async Task The_view_is_put_back_where_the_gesture_found_it()
    {
        var adapter = CreateGesturing();
        var result = await adapter.ZoomInAsync(Acrobat, Cursor, CancellationToken.None);

        await adapter.ZoomOutAsync(Acrobat, result.RestoreState!, CancellationToken.None);

        Assert.Equal(1, _view.Snapshots);
        Assert.Equal(1, _view.Alignments);
    }

    [Fact]
    public async Task A_gesture_the_system_refuses_is_unhandled()
    {
        _pinch.Succeeds = false;

        Assert.Equal(ZoomInStatus.Unhandled, (await CreateGesturing().ZoomInAsync(Acrobat, Cursor, CancellationToken.None)).Status);
    }

    [Fact]
    public async Task A_cursor_outside_the_content_area_is_unhandled()
    {
        _view.ContentBounds = PixelRect.FromSize(0, 0, 100, 100);

        var result = await CreateGesturing().ZoomInAsync(Acrobat, new ScreenPoint(900, 900), CancellationToken.None);

        Assert.Equal(ZoomInStatus.Unhandled, result.Status);
        Assert.Empty(_pinch.Gestures);
    }

    [Fact]
    public async Task A_window_with_no_content_area_falls_back_to_the_shortcut()
    {
        _view.ContentBounds = null;

        var result = await Create().ZoomInAsync(Acrobat, Cursor, CancellationToken.None);

        Assert.Equal(ZoomInStatus.Applied, result.Status);
        Assert.Equal("Ctrl+2", _injector.Script);
    }

    [Fact]
    public async Task Following_the_cursor_scrolls_its_content_to_the_top_before_zooming()
    {
        var result = await Create(followCursor: true).ZoomInAsync(Acrobat, Cursor, CancellationToken.None);

        Assert.Equal(ZoomInStatus.Applied, result.Status);
        Assert.Equal([484], _view.Requests);              // 500 px down to the top, less the 16 px margin
        Assert.Equal("Ctrl+2", _injector.Script);
    }

    [Fact]
    public async Task Zoom_out_undoes_the_distance_the_view_really_moved_not_the_distance_asked_for()
    {
        // Near the end of a document the reader scrolls less than asked; undoing the request would overshoot.
        _view.Moves = 300;
        var adapter = Create(followCursor: true);
        var result = await adapter.ZoomInAsync(Acrobat, Cursor, CancellationToken.None);

        await adapter.ZoomOutAsync(Acrobat, result.RestoreState!, CancellationToken.None);

        Assert.Equal([484, -300], _view.Requests);
    }

    [Fact]
    public async Task The_shortcut_goes_out_before_the_view_is_scrolled_back()
    {
        var adapter = Create(followCursor: true);
        var result = await adapter.ZoomInAsync(Acrobat, Cursor, CancellationToken.None);
        _injector.Log.Clear();
        _view.Order.Clear();

        await adapter.ZoomOutAsync(Acrobat, result.RestoreState!, CancellationToken.None);

        Assert.Equal(["Ctrl+0", "scroll"], _injector.Log.Concat(_view.Order).ToArray());
    }

    [Fact]
    public async Task A_cursor_already_at_the_top_is_not_scrolled()
    {
        _view.ContentBounds = PixelRect.FromSize(0, Cursor.Y - 10, 2000, 2000);

        await Create(followCursor: true).ZoomInAsync(Acrobat, Cursor, CancellationToken.None);

        Assert.Empty(_view.Requests);
    }

    [Fact]
    public async Task A_point_outside_the_document_area_is_not_scrolled()
    {
        _view.ContentBounds = PixelRect.FromSize(0, 0, 100, 100);

        await Create(followCursor: true).ZoomInAsync(Acrobat, Cursor, CancellationToken.None);

        Assert.Empty(_view.Requests);
    }

    [Fact]
    public async Task A_scroll_is_put_back_when_the_shortcut_cannot_be_sent()
    {
        _injector.FailKeys = true;

        var result = await Create(followCursor: true).ZoomInAsync(Acrobat, Cursor, CancellationToken.None);

        Assert.Equal(ZoomInStatus.Unhandled, result.Status);
        Assert.Equal([484, -484], _view.Requests);
    }

    [Fact]
    public async Task A_magnification_of_one_or_less_is_not_worth_a_gesture_and_falls_back_to_the_shortcut()
    {
        var adapter = new KeyZoomAdapter(_injector, _activator, _view, _pinch,
            new ReaderZoomSettings { ZoomInKeys = "Ctrl+2", ZoomOutKeys = "Ctrl+0", Gesture = true, Scale = 1.0 },
            TimeSpan.Zero, TimeProvider.System, NullLogger<KeyZoomAdapter>.Instance);

        var result = await adapter.ZoomInAsync(Acrobat, Cursor, CancellationToken.None);

        Assert.Equal(ZoomInStatus.Applied, result.Status);
        Assert.Empty(_pinch.Gestures);
        Assert.Equal("Ctrl+2", _injector.Script);
    }

    [Fact]
    public async Task Without_a_pinch_injector_the_gesture_setting_falls_back_to_the_shortcut()
    {
        var adapter = new KeyZoomAdapter(_injector, _activator, _view, pinch: null,
            new ReaderZoomSettings { ZoomInKeys = "Ctrl+2", ZoomOutKeys = "Ctrl+0", Gesture = true },
            TimeSpan.Zero, TimeProvider.System, NullLogger<KeyZoomAdapter>.Instance);

        var result = await adapter.ZoomInAsync(Acrobat, Cursor, CancellationToken.None);

        Assert.Equal(ZoomInStatus.Applied, result.Status);
        Assert.Equal("Ctrl+2", _injector.Script);
    }

    [Fact]
    public async Task Without_a_reader_view_the_gesture_setting_falls_back_to_the_shortcut()
    {
        var adapter = new KeyZoomAdapter(_injector, _activator, view: null, _pinch,
            new ReaderZoomSettings { ZoomInKeys = "Ctrl+2", ZoomOutKeys = "Ctrl+0", Gesture = true },
            TimeSpan.Zero, TimeProvider.System, NullLogger<KeyZoomAdapter>.Instance);

        var result = await adapter.ZoomInAsync(Acrobat, Cursor, CancellationToken.None);

        Assert.Equal(ZoomInStatus.Applied, result.Status);
        Assert.Empty(_pinch.Gestures);
        Assert.Equal("Ctrl+2", _injector.Script);
    }

    [Fact]
    public async Task The_gesture_comes_back_around_a_point_kept_inside_the_content_area()
    {
        var adapter = CreateGesturing();
        var result = await adapter.ZoomInAsync(Acrobat, Cursor, CancellationToken.None);
        _view.ContentBounds = PixelRect.FromSize(0, 0, Cursor.X - 100, Cursor.Y - 100);

        await adapter.ZoomOutAsync(Acrobat, result.RestoreState!, CancellationToken.None);

        Assert.Equal(2, _pinch.Gestures.Count);
        Assert.Equal(new ScreenPoint(Cursor.X - 101, Cursor.Y - 101), _pinch.Gestures[1].Anchor);
    }

    [Fact]
    public async Task A_view_that_cannot_be_read_afterwards_still_leaves_the_magnification_undone()
    {
        _view.Aligns = false;
        var adapter = CreateGesturing();
        var result = await adapter.ZoomInAsync(Acrobat, Cursor, CancellationToken.None);

        await adapter.ZoomOutAsync(Acrobat, result.RestoreState!, CancellationToken.None);

        Assert.Equal([(Cursor, 2.0), (Cursor, 0.5)], _pinch.Gestures);
    }

    [Fact]
    public async Task Following_the_cursor_can_be_turned_off()
    {
        await Create(followCursor: false).ZoomInAsync(Acrobat, Cursor, CancellationToken.None);

        Assert.Empty(_view.Requests);
    }

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
        var adapter = new KeyZoomAdapter(_injector, _activator, _view, _pinch, new ReaderZoomSettings { Gesture = false }, TimeSpan.Zero, TimeProvider.System, NullLogger<KeyZoomAdapter>.Instance, TimeSpan.FromMilliseconds(50));

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
    public async Task A_window_that_loses_the_foreground_while_we_wait_is_not_sent_the_shortcut()
    {
        // These shortcuts are destructive in the wrong application: Ctrl+0 hides the selected column in Excel.
        _view.ContentBounds = PixelRect.FromSize(0, 100, 2000, 2000);
        _activator.StealForegroundAfterActivating = 0x999;

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
        Assert.Throws<FormatException>(() => new KeyZoomAdapter(_injector, _activator, _view, _pinch, new ReaderZoomSettings { ZoomInKeys = "Ctrl+" }, TimeSpan.Zero, TimeProvider.System, NullLogger<KeyZoomAdapter>.Instance));

    [Fact]
    public async Task Zoom_out_rejects_foreign_state() =>
        await Assert.ThrowsAsync<ArgumentException>(() => Create().ZoomOutAsync(Acrobat, "nope", CancellationToken.None));

    private sealed class FakeReaderView : IReaderView
    {
        /// <summary>The content area; the cursor at (800, 600) sits 500 px below its top.</summary>
        public PixelRect? ContentBounds { get; set; } = PixelRect.FromSize(0, 100, 2000, 2000);

        /// <summary>How far the view really moves, when that differs from the request.</summary>
        public int? Moves { get; set; }

        public List<int> Requests { get; } = [];

        public List<string> Order { get; } = [];

        public int Snapshots { get; private set; }

        public int Alignments { get; private set; }

        /// <summary>Whether the view can be read well enough to put it back.</summary>
        public bool Aligns { get; set; } = true;

        public PixelRect? Bounds(TargetInfo target) => ContentBounds;

        public int ScrollBy(TargetInfo target, int pixels, CancellationToken cancellationToken)
        {
            Requests.Add(pixels);
            Order.Add("scroll");
            return Moves is { } moved ? Math.Sign(pixels) * Math.Abs(moved) : pixels;
        }

        public object? Snapshot(TargetInfo target)
        {
            Snapshots++;
            return "mark";
        }

        public bool ScrollBackTo(TargetInfo target, object snapshot, CancellationToken cancellationToken)
        {
            Alignments++;
            return Aligns;
        }
    }

    private sealed class FakePinchInjector : IPinchInjector
    {
        public bool Succeeds { get; set; } = true;

        public List<(ScreenPoint Anchor, double Factor)> Gestures { get; } = [];

        public Task<bool> PinchAsync(ScreenPoint anchor, double factor, TimeSpan duration, PixelRect bounds, CancellationToken cancellationToken)
        {
            if (Succeeds)
                Gestures.Add((anchor, factor));

            return Task.FromResult(Succeeds);
        }
    }

    private sealed class FakeActivator : IWindowActivator
    {
        public bool Succeeds { get; set; } = true;

        /// <summary>Whether bringing back a window other than the target succeeds.</summary>
        public bool RestoreSucceeds { get; set; } = true;

        public List<long> Activated { get; } = [];

        public nint ForegroundWindow { get; set; }

        /// <summary>Another window takes the foreground the moment the target has been activated.</summary>
        public nint StealForegroundAfterActivating { get; set; }

        public bool TryActivate(nint rootWindow)
        {
            Activated.Add(rootWindow);
            var ok = rootWindow == Acrobat.RootWindow ? Succeeds : RestoreSucceeds;
            if (ok)
                ForegroundWindow = StealForegroundAfterActivating == 0 ? rootWindow : StealForegroundAfterActivating;
            return ok;
        }
    }
}
