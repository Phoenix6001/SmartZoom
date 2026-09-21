using Microsoft.Extensions.Logging.Abstractions;

using SmartZoom.Core.Input;
using SmartZoom.Core.Settings;
using SmartZoom.Core.Zoom;
using SmartZoom.Core.Zoom.Content;
using SmartZoom.Core.Zoom.Reader;

namespace SmartZoom.Core.Tests.Zoom.Reader;

public sealed class ReaderPinchAdapterTests
{
    private readonly FakeInputInjector _injector = new();
    private readonly FakeActivator _activator = new();
    private readonly FakeReaderView _view = new();
    private readonly FakePinchInjector _pinch = new();

    private IZoomAdapter Create(double scale = 2.0) =>
        new ReaderPinchAdapter(
            _pinch,
            _view,
            new ShortcutSender(_injector, _activator, TimeProvider.System, NullLogger<ShortcutSender>.Instance),
            new ReaderZoomSettings { ZoomInKeys = "Ctrl+2", ZoomOutKeys = "Ctrl+0", Magnification = scale },
            TimeSpan.FromMilliseconds(200),
            NullLogger<ReaderPinchAdapter>.Instance);

    [Fact]
    public async Task A_gesture_magnifies_around_the_cursor_and_sends_no_shortcut()
    {
        var result = await Create().ZoomInAsync(Reader.Acrobat, Reader.Cursor, CancellationToken.None);

        Assert.Equal(ZoomInStatus.Applied, result.Status);
        Assert.Equal([(Reader.Cursor, 2.0)], _pinch.Gestures);
        Assert.Empty(_injector.Log);
        Assert.Empty(_view.Requests);
    }

    [Fact]
    public async Task The_second_press_takes_the_magnification_off_and_lands_on_the_readers_own_zoom()
    {
        // The closing gesture alone is not zoom-neutral: measured at 0.980 per toggle, it compounds to -10%
        // over five. Landing on fit page names a state instead of a change, so it cannot drift.
        var adapter = Create(scale: 2.5);
        var result = await adapter.ZoomInAsync(Reader.Acrobat, Reader.Cursor, CancellationToken.None);

        await adapter.ZoomOutAsync(Reader.Acrobat, result.RestoreState!, CancellationToken.None);

        Assert.Equal([(Reader.Cursor, 2.5), (Reader.Cursor, 1 / 2.5)], _pinch.Gestures);
        Assert.Equal("Ctrl+0", _injector.Script);
    }

    [Fact]
    public async Task The_view_is_put_back_where_the_gesture_found_it()
    {
        var adapter = Create();
        var result = await adapter.ZoomInAsync(Reader.Acrobat, Reader.Cursor, CancellationToken.None);

        await adapter.ZoomOutAsync(Reader.Acrobat, result.RestoreState!, CancellationToken.None);

        Assert.Equal(1, _view.Snapshots);
        Assert.Equal(1, _view.Alignments);
    }

    [Fact]
    public async Task A_gesture_the_system_refuses_is_unhandled()
    {
        _pinch.Succeeds = false;

        Assert.Equal(ZoomInStatus.Unhandled, (await Create().ZoomInAsync(Reader.Acrobat, Reader.Cursor, CancellationToken.None)).Status);
    }

    [Fact]
    public async Task A_cursor_outside_the_content_area_is_unhandled()
    {
        _view.ContentBounds = PixelRect.FromSize(0, 0, 100, 100);

        var result = await Create().ZoomInAsync(Reader.Acrobat, new ScreenPoint(900, 900), CancellationToken.None);

        Assert.Equal(ZoomInStatus.Unhandled, result.Status);
        Assert.Empty(_pinch.Gestures);
    }

    [Fact]
    public async Task A_window_with_no_content_area_is_unhandled()
    {
        _view.ContentBounds = null;

        var result = await Create().ZoomInAsync(Reader.Acrobat, Reader.Cursor, CancellationToken.None);

        Assert.Equal(ZoomInStatus.Unhandled, result.Status);
        Assert.Empty(_pinch.Gestures);
    }

    [Fact]
    public async Task The_gesture_comes_back_around_a_point_kept_inside_the_content_area()
    {
        var adapter = Create();
        var result = await adapter.ZoomInAsync(Reader.Acrobat, Reader.Cursor, CancellationToken.None);
        _view.ContentBounds = PixelRect.FromSize(0, 0, Reader.Cursor.X - 100, Reader.Cursor.Y - 100);

        await adapter.ZoomOutAsync(Reader.Acrobat, result.RestoreState!, CancellationToken.None);

        Assert.Equal(2, _pinch.Gestures.Count);
        Assert.Equal(new ScreenPoint(Reader.Cursor.X - 101, Reader.Cursor.Y - 101), _pinch.Gestures[1].Anchor);
    }

    [Fact]
    public async Task A_view_that_cannot_be_read_afterwards_still_leaves_the_magnification_undone()
    {
        _view.Aligns = false;
        var adapter = Create();
        var result = await adapter.ZoomInAsync(Reader.Acrobat, Reader.Cursor, CancellationToken.None);

        await adapter.ZoomOutAsync(Reader.Acrobat, result.RestoreState!, CancellationToken.None);

        Assert.Equal([(Reader.Cursor, 2.0), (Reader.Cursor, 0.5)], _pinch.Gestures);
    }

    [Fact]
    public async Task A_reader_that_refuses_the_zoom_command_still_has_the_magnification_taken_off()
    {
        _injector.FailKeys = true;
        var adapter = Create();
        var result = await adapter.ZoomInAsync(Reader.Acrobat, Reader.Cursor, CancellationToken.None);

        await adapter.ZoomOutAsync(Reader.Acrobat, result.RestoreState!, CancellationToken.None);

        Assert.Equal([(Reader.Cursor, 2.0), (Reader.Cursor, 0.5)], _pinch.Gestures);
    }

    [Theory]
    [InlineData(1.0)]
    [InlineData(0.5)]
    public void A_magnification_of_one_or_less_is_rejected_at_construction(double scale) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => Create(scale));

    [Fact]
    public async Task Zoom_out_rejects_foreign_state() =>
        await Assert.ThrowsAsync<ArgumentException>(() => Create().ZoomOutAsync(Reader.Acrobat, "nope", CancellationToken.None));
}
