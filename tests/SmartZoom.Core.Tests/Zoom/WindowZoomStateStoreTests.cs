using SmartZoom.Core.Routing;
using SmartZoom.Core.Zoom;

namespace SmartZoom.Core.Tests.Zoom;

public sealed class WindowZoomStateStoreTests
{
    private static readonly AdapterId CtrlWheel = new("CtrlWheel");
    private static readonly AdapterId Word = new("WordCom");

    private static TargetInfo Target(nint root = 0x10, uint pid = 7) => new(root, root + 1, pid, "app", "Root", "Hit");

    private readonly WindowZoomStateStore _store = new();

    [Fact]
    public void Take_returns_what_was_saved_and_removes_it()
    {
        _store.Save(Target(), CtrlWheel, "state");

        Assert.True(_store.TryTake(Target(), out var adapter, out var state));
        Assert.Equal(CtrlWheel, adapter);
        Assert.Equal("state", state);
        Assert.False(_store.TryTake(Target(), out _, out _));
        Assert.Equal(0, _store.Count);
    }

    [Fact]
    public void Take_ignores_and_discards_a_recycled_handle_owned_by_another_process()
    {
        _store.Save(Target(pid: 7), CtrlWheel, "state");

        Assert.False(_store.TryTake(Target(pid: 8), out _, out _));
        Assert.Equal(0, _store.Count);
    }

    [Fact]
    public void Save_overwrites_an_existing_entry_for_the_same_window()
    {
        _store.Save(Target(), CtrlWheel, "first");
        _store.Save(Target(), Word, "second");

        Assert.True(_store.TryTake(Target(), out var adapter, out var state));
        Assert.Equal(Word, adapter);
        Assert.Equal("second", state);
    }

    [Fact]
    public void Prune_removes_only_dead_windows()
    {
        _store.Save(Target(root: 0x10), CtrlWheel, "a");
        _store.Save(Target(root: 0x20), CtrlWheel, "b");
        _store.Save(Target(root: 0x30, pid: 9), CtrlWheel, "c");

        var removed = _store.Prune((window, pid) => window == 0x20 || pid == 9);

        Assert.Equal(1, removed);
        Assert.Equal(2, _store.Count);
        Assert.False(_store.TryTake(Target(root: 0x10), out _, out _));
        Assert.True(_store.TryTake(Target(root: 0x20), out _, out _));
        Assert.True(_store.TryTake(Target(root: 0x30, pid: 9), out _, out _));
    }
}
