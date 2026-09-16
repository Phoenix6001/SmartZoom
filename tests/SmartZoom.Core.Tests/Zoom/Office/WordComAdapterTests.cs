using Microsoft.Extensions.Logging.Abstractions;
using SmartZoom.Core.Input;
using SmartZoom.Core.Routing;
using SmartZoom.Core.Settings;
using SmartZoom.Core.Zoom;
using SmartZoom.Core.Zoom.Content;
using SmartZoom.Core.Zoom.Office;

namespace SmartZoom.Core.Tests.Zoom.Office;

public sealed class WordComAdapterTests : IDisposable
{
    private static readonly TargetInfo Word = new(0x100, 0x101, 9, "WINWORD", "OpusApp", "_WwG");
    private static readonly ScreenPoint Cursor = new(1000, 900);
    private static readonly PixelRect Pane = PixelRect.FromSize(200, 300, 1600, 1200);

    private readonly FakeWord _word = new();

    public void Dispose() => _word.Dispose();

    private WordComAdapter Create(bool animate = false) =>
        new(_word, pinch: null, new ZoomSettings { Animate = animate, Browser = new BrowserZoomSettings { AnimationMs = 100, MarginPx = 16 } }, TimeProvider.System, NullLogger<WordComAdapter>.Instance);

    [Fact]
    public async Task Zooms_the_paragraph_to_the_pane_width_and_remembers_the_view()
    {
        _word.Block = PixelRect.FromSize(600, 850, 768, 120);
        _word.State = new WordViewState(100, 40, 0);

        var result = await Create().ZoomInAsync(Word, Cursor, CancellationToken.None);

        Assert.Equal(ZoomInStatus.Applied, result.Status);
        Assert.Equal(new WordViewState(100, 40, 0), result.RestoreState);
        Assert.Equal(200, _word.Zoom); // 1600 / (768 + 32) = 2.0 -> 200%
        Assert.True(_word.ScrolledIntoView);
    }

    [Fact]
    public async Task Zoom_out_restores_the_exact_view()
    {
        _word.Block = PixelRect.FromSize(600, 850, 768, 120);
        _word.State = new WordViewState(120, 33, 5);
        var adapter = Create();
        var result = await adapter.ZoomInAsync(Word, Cursor, CancellationToken.None);

        await adapter.ZoomOutAsync(Word, result.RestoreState!, CancellationToken.None);

        Assert.Equal(new WordViewState(120, 33, 5), _word.Restored);
    }

    [Fact]
    public async Task Scale_is_capped_and_zoom_stays_within_word_limits()
    {
        _word.Block = PixelRect.FromSize(600, 850, 100, 20);
        _word.State = new WordViewState(300, 0, 0);

        await Create().ZoomInAsync(Word, Cursor, CancellationToken.None);

        Assert.Equal(500, _word.Zoom); // 300% * 3.0 = 900% -> clamped to Word's 500%
    }

    [Fact]
    public async Task Nothing_under_the_cursor_is_handled_without_zooming()
    {
        _word.Block = null;

        Assert.Equal(ZoomInStatus.SelfManaged, (await Create().ZoomInAsync(Word, Cursor, CancellationToken.None)).Status);
        Assert.Null(_word.Zoom);
    }

    [Fact]
    public async Task Block_that_already_fills_the_pane_is_left_alone()
    {
        _word.Block = PixelRect.FromSize(210, 850, 1500, 120);

        Assert.Equal(ZoomInStatus.SelfManaged, (await Create().ZoomInAsync(Word, Cursor, CancellationToken.None)).Status);
        Assert.Null(_word.Zoom);
    }

    [Fact]
    public async Task Not_a_word_window_is_unhandled()
    {
        _word.Attachable = false;

        Assert.Equal(ZoomInStatus.Unhandled, (await Create().ZoomInAsync(Word, Cursor, CancellationToken.None)).Status);
    }

    [Fact]
    public async Task Animation_steps_through_intermediate_zoom_levels()
    {
        _word.Block = PixelRect.FromSize(600, 850, 768, 120);
        _word.State = new WordViewState(100, 0, 0);

        await Create(animate: true).ZoomInAsync(Word, Cursor, CancellationToken.None);

        Assert.True(_word.ZoomHistory.Count > 3, "expected several intermediate steps");
        Assert.Equal(200, _word.ZoomHistory[^1]);
        Assert.True(_word.ZoomHistory.Zip(_word.ZoomHistory.Skip(1)).All(pair => pair.Second >= pair.First), "zoom should increase monotonically");
    }

    private sealed class FakeWord : IWordAutomation, IWordWindow
    {
        public bool Attachable { get; set; } = true;

        public PixelRect? Block { get; set; }

        public WordViewState State { get; set; } = new(100, 0, 0);

        public int? Zoom { get; private set; }

        public List<int> ZoomHistory { get; } = [];

        public bool ScrolledIntoView { get; private set; }

        public WordViewState? Restored { get; private set; }

        public PixelRect Viewport => Pane;

        public Task<IWordWindow?> AttachAsync(TargetInfo target, CancellationToken cancellationToken) =>
            Task.FromResult<IWordWindow?>(Attachable ? this : null);

        public WordViewState GetState() => Zoom is { } z ? State with { ZoomPercent = z } : State;

        public PixelRect? GetBlockAt(ScreenPoint point) => Block;

        public void SetZoom(int percent)
        {
            Zoom = percent;
            ZoomHistory.Add(percent);
        }

        public void ScrollBlockIntoView(ScreenPoint point) => ScrolledIntoView = true;

        public void Restore(WordViewState state)
        {
            Restored = state;
            Zoom = state.ZoomPercent;
        }

        public void Dispose()
        {
        }
    }
}
