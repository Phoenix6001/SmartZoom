using Microsoft.Extensions.Logging.Abstractions;

using SmartZoom.Core.Settings;
using SmartZoom.Core.Zoom;
using SmartZoom.Core.Zoom.Content;
using SmartZoom.Core.Zoom.Reader;

namespace SmartZoom.Core.Tests.Zoom.Reader;

public sealed class ReaderShortcutAdapterTests
{
    private readonly FakeInputInjector _injector = new();
    private readonly FakeActivator _activator = new();
    private readonly FakeReaderView _view = new();

    private IZoomAdapter Create(bool followCursor = false, bool withView = true) =>
        new ReaderShortcutAdapter(
            new ShortcutSender(_injector, _activator, TimeProvider.System, NullLogger<ShortcutSender>.Instance),
            withView ? _view : null,
            new ReaderZoomSettings { ZoomInKeys = "Ctrl+2", ZoomOutKeys = "Ctrl+0", FollowCursor = followCursor, TopGapPx = 16 },
            NullLogger<ReaderShortcutAdapter>.Instance);

    [Fact]
    public async Task Zoom_in_sends_the_fit_width_shortcut()
    {
        var result = await Create().ZoomInAsync(Reader.Acrobat, Reader.Cursor, CancellationToken.None);

        Assert.Equal(ZoomInStatus.Applied, result.Status);
        Assert.Equal("Ctrl+2", _injector.Script);
    }

    [Fact]
    public async Task Zoom_out_sends_the_fit_page_shortcut()
    {
        var adapter = Create();
        var result = await adapter.ZoomInAsync(Reader.Acrobat, Reader.Cursor, CancellationToken.None);

        await adapter.ZoomOutAsync(Reader.Acrobat, result.RestoreState!, CancellationToken.None);

        Assert.Equal("Ctrl+2 Ctrl+0", _injector.Script);
    }

    [Fact]
    public async Task Following_the_cursor_scrolls_its_content_to_the_top_before_zooming()
    {
        var result = await Create(followCursor: true).ZoomInAsync(Reader.Acrobat, Reader.Cursor, CancellationToken.None);

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
        var result = await adapter.ZoomInAsync(Reader.Acrobat, Reader.Cursor, CancellationToken.None);

        await adapter.ZoomOutAsync(Reader.Acrobat, result.RestoreState!, CancellationToken.None);

        Assert.Equal([484, -300], _view.Requests);
    }

    [Fact]
    public async Task The_shortcut_goes_out_before_the_view_is_scrolled_back()
    {
        var adapter = Create(followCursor: true);
        var result = await adapter.ZoomInAsync(Reader.Acrobat, Reader.Cursor, CancellationToken.None);
        _injector.Log.Clear();
        _view.Order.Clear();

        await adapter.ZoomOutAsync(Reader.Acrobat, result.RestoreState!, CancellationToken.None);

        Assert.Equal(["Ctrl+0", "scroll"], _injector.Log.Concat(_view.Order).ToArray());
    }

    [Fact]
    public async Task A_cursor_already_at_the_top_is_not_scrolled()
    {
        _view.ContentBounds = PixelRect.FromSize(0, Reader.Cursor.Y - 10, 2000, 2000);

        await Create(followCursor: true).ZoomInAsync(Reader.Acrobat, Reader.Cursor, CancellationToken.None);

        Assert.Empty(_view.Requests);
    }

    [Fact]
    public async Task A_point_outside_the_document_area_is_not_scrolled()
    {
        _view.ContentBounds = PixelRect.FromSize(0, 0, 100, 100);

        await Create(followCursor: true).ZoomInAsync(Reader.Acrobat, Reader.Cursor, CancellationToken.None);

        Assert.Empty(_view.Requests);
    }

    [Fact]
    public async Task Following_the_cursor_can_be_turned_off()
    {
        await Create(followCursor: false).ZoomInAsync(Reader.Acrobat, Reader.Cursor, CancellationToken.None);

        Assert.Empty(_view.Requests);
    }

    [Fact]
    public async Task A_scroll_is_put_back_when_the_shortcut_cannot_be_sent()
    {
        _injector.FailKeys = true;

        var result = await Create(followCursor: true).ZoomInAsync(Reader.Acrobat, Reader.Cursor, CancellationToken.None);

        Assert.Equal(ZoomInStatus.Unhandled, result.Status);
        Assert.Equal([484, -484], _view.Requests);
    }

    [Fact]
    public async Task A_window_with_no_content_area_is_still_zoomed_by_the_shortcut()
    {
        // The reader decides what ends up on screen; that is worse than following the cursor, not useless.
        _view.ContentBounds = null;

        var result = await Create(followCursor: true).ZoomInAsync(Reader.Acrobat, Reader.Cursor, CancellationToken.None);

        Assert.Equal(ZoomInStatus.Applied, result.Status);
        Assert.Equal("Ctrl+2", _injector.Script);
    }

    [Fact]
    public async Task Without_a_reader_view_the_shortcut_still_works()
    {
        var result = await Create(followCursor: true, withView: false).ZoomInAsync(Reader.Acrobat, Reader.Cursor, CancellationToken.None);

        Assert.Equal(ZoomInStatus.Applied, result.Status);
        Assert.Equal("Ctrl+2", _injector.Script);
    }

    [Fact]
    public async Task Rejected_keys_are_unhandled()
    {
        _injector.FailKeys = true;

        Assert.Equal(ZoomInStatus.Unhandled, (await Create().ZoomInAsync(Reader.Acrobat, Reader.Cursor, CancellationToken.None)).Status);
    }

    [Fact]
    public void Invalid_shortcut_text_is_rejected_at_construction() =>
        Assert.Throws<FormatException>(() => new ReaderShortcutAdapter(
            new ShortcutSender(_injector, _activator, TimeProvider.System, NullLogger<ShortcutSender>.Instance),
            _view,
            new ReaderZoomSettings { ZoomInKeys = "Ctrl+" },
            NullLogger<ReaderShortcutAdapter>.Instance));

    [Fact]
    public async Task Zoom_out_rejects_foreign_state() =>
        await Assert.ThrowsAsync<ArgumentException>(() => Create().ZoomOutAsync(Reader.Acrobat, "nope", CancellationToken.None));
}
