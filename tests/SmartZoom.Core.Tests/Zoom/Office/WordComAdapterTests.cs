using System.Diagnostics;

using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

using SmartZoom.Core.Input;
using SmartZoom.Core.Routing;
using SmartZoom.Core.Settings;
using SmartZoom.Core.Tests.Zoom.Reader;
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
    private readonly FakePinchInjector _pinch = new();
    private readonly FakeTimeProvider _time = new();

    public void Dispose() => _word.Dispose();

    private IZoomAdapter Create(bool animate = false, bool gesture = true) =>
        new WordComAdapter(
            _word,
            gesture ? _pinch : null,
            new ZoomSettings { Animate = animate, Smart = new SmartZoomTuning { AnimationMs = 100, MarginPx = 16 } },
            _time,
            NullLogger<WordComAdapter>.Instance);

    /// <summary>Runs an animated zoom to completion, stepping the fake clock through each animation delay.</summary>
    private async Task Animate(Task zoom)
    {
        var patience = Stopwatch.StartNew();
        while (!zoom.IsCompleted)
        {
            // The adapter's continuations run on the pool; give them real time, bounded by the wall clock rather
            // than by a step count, so a loaded test run cannot starve them into a false failure.
            Assert.True(patience.Elapsed < TimeSpan.FromSeconds(10), "the animation did not complete");
            _time.Advance(TimeSpan.FromMilliseconds(10));
            await Task.WhenAny(zoom, Task.Delay(1));
        }

        await zoom;
    }

    [Fact]
    public async Task Zooms_the_paragraph_to_the_pane_width_and_remembers_the_view()
    {
        _word.Block = new WordBlock(PixelRect.FromSize(600, 850, 768, 120), Start: 4200);
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
        _word.Block = new WordBlock(PixelRect.FromSize(600, 850, 768, 120), Start: 4200);
        _word.State = new WordViewState(120, 33, 5);
        var adapter = Create();
        var result = await adapter.ZoomInAsync(Word, Cursor, CancellationToken.None);

        await adapter.ZoomOutAsync(Word, result.RestoreState!, CancellationToken.None);

        Assert.Equal(new WordViewState(120, 33, 5), _word.Restored);
    }

    [Fact]
    public async Task Scale_is_capped_and_zoom_stays_within_word_limits()
    {
        _word.Block = new WordBlock(PixelRect.FromSize(600, 850, 100, 20), Start: 4200);
        _word.State = new WordViewState(300, 0, 0);

        await Create().ZoomInAsync(Word, Cursor, CancellationToken.None);

        Assert.Equal(500, _word.Zoom); // 300% * 3.0 = 900% -> clamped to Word's 500%
    }

    [Fact]
    public async Task Nothing_under_the_cursor_is_handled_without_zooming()
    {
        _word.Block = null;

        var result = await Create().ZoomInAsync(Word, Cursor, CancellationToken.None);

        Assert.Equal(ZoomInStatus.Handled, result.Status);
        Assert.Equal(ZoomReason.NoBlock, result.Reason);
        Assert.Null(_word.Zoom);
    }

    [Fact]
    public async Task Block_that_already_fills_the_pane_is_left_alone()
    {
        _word.Block = new WordBlock(PixelRect.FromSize(210, 850, 1500, 120), Start: 4200);

        var result = await Create().ZoomInAsync(Word, Cursor, CancellationToken.None);

        Assert.Equal(ZoomInStatus.Handled, result.Status);
        Assert.Equal(ZoomReason.AlreadyFits, result.Reason);
        Assert.Null(_word.Zoom);
    }

    [Fact]
    public async Task The_motion_is_a_gesture_Word_renders_itself_and_the_zoom_still_lands_exactly()
    {
        // Why this is not a sequence of SetZoom steps: Word re-lays the whole document out for every zoom
        // value it is given, whereas it animates a pinch itself — which is what makes the PDF readers smooth.
        _word.Block = new WordBlock(PixelRect.FromSize(600, 850, 768, 120), Start: 4200);

        await Animate(Create(animate: true).ZoomInAsync(Word, Cursor, CancellationToken.None));

        // Asked for more than the zoom wanted, because the recognizer delivers about 93% of what it is given.
        var gesture = Assert.Single(_pinch.Gestures);
        Assert.Equal(Cursor, gesture.Anchor);
        Assert.InRange(gesture.Factor, 1.5, 2.5);

        // And the zoom itself is exact regardless of what the gesture actually delivered.
        Assert.Equal(_word.ZoomHistory[^1], _word.Zoom);
        Assert.Single(_word.ZoomHistory);
    }

    [Fact]
    public async Task Zooming_out_sends_no_gesture_so_the_document_comes_back_to_exactly_where_it_started()
    {
        // The regression this guards against, measured live: Word commits an OPENING pinch about 150 ms after
        // the gesture ends but a CLOSING one about a second after, long past any settle worth waiting for. The
        // exact zoom set in between was then overwritten by Word's own result, and since that result is only
        // part of the way back, every toggle left the document about 20% bigger: 100 -> 300 -> 119 -> 316 -> 144.
        _word.Block = new WordBlock(PixelRect.FromSize(600, 850, 768, 120), Start: 4200);
        var adapter = Create(animate: true);

        await Animate(adapter.ZoomInAsync(Word, Cursor, CancellationToken.None));
        Assert.Single(_pinch.Gestures);

        await Animate(adapter.ZoomOutAsync(Word, new WordViewState(100, 0, 0), CancellationToken.None));

        // Still one: the way out is stepped through the object model, which has nothing to commit late.
        Assert.Single(_pinch.Gestures);
        Assert.Equal(100, _word.Zoom);
    }

    [Fact]
    public async Task A_refused_gesture_falls_back_to_stepping_the_zoom()
    {
        // The gesture is the motion, not the zoom: without one the press behaves exactly as it used to.
        _pinch.Succeeds = false;
        _word.Block = new WordBlock(PixelRect.FromSize(600, 850, 768, 120), Start: 4200);

        await Animate(Create(animate: true).ZoomInAsync(Word, Cursor, CancellationToken.None));

        Assert.Empty(_pinch.Gestures);
        Assert.True(_word.ZoomHistory.Count > 3, "expected the stepped animation to take over");
    }

    [Fact]
    public async Task The_view_is_scrolled_to_the_block_that_was_found_not_to_whatever_the_zoom_left_under_the_pointer()
    {
        // Reproduced on a real document: a press on the last part of page 2 ended up showing page 1. Word
        // re-lays the document out for the new zoom, so asking what is under the original pixel afterwards
        // answers with completely different text. The block is scrolled to by its position in the text.
        _word.Block = new WordBlock(PixelRect.FromSize(600, 850, 768, 120), Start: 9137);

        await Create().ZoomInAsync(Word, Cursor, CancellationToken.None);

        Assert.Equal(9137, _word.ScrolledTo);
    }

    [Fact]
    public async Task A_pane_with_no_size_is_reported_as_automation_failed_rather_than_already_fitting()
    {
        // Word reports an empty viewport when the pane is gone; a zero-width fit must not read as "already fits".
        _word.Block = new WordBlock(PixelRect.FromSize(600, 850, 768, 120), Start: 4200);
        _word.Viewport = default;

        var result = await Create().ZoomInAsync(Word, Cursor, CancellationToken.None);

        Assert.Equal(ZoomInStatus.Handled, result.Status);
        Assert.Equal(ZoomReason.AutomationFailed, result.Reason);
        Assert.Null(_word.Zoom);
    }

    [Fact]
    public async Task An_object_model_failure_is_reported_as_automation_failed()
    {
        _word.Block = new WordBlock(PixelRect.FromSize(600, 850, 768, 120), Start: 4200);
        _word.State = new WordViewState(100, 40, 0);
        _word.FailZoom = true;

        var result = await Create().ZoomInAsync(Word, Cursor, CancellationToken.None);

        Assert.Equal(ZoomInStatus.Handled, result.Status);
        Assert.Equal(ZoomReason.AutomationFailed, result.Reason);
    }

    [Fact]
    public async Task An_object_model_failure_part_way_through_puts_the_view_back()
    {
        // The zoom has already been applied when the scroll fails; the user must not be left at 200 %.
        _word.Block = new WordBlock(PixelRect.FromSize(600, 850, 768, 120), Start: 4200);
        _word.State = new WordViewState(100, 40, 0);
        _word.FailScroll = true;

        var result = await Create().ZoomInAsync(Word, Cursor, CancellationToken.None);

        Assert.Equal(ZoomInStatus.Handled, result.Status);
        Assert.Equal(ZoomReason.AutomationFailed, result.Reason);
        Assert.Equal(new WordViewState(100, 40, 0), _word.Restored);
        Assert.Equal(100, _word.Zoom);
    }

    [Fact]
    public async Task A_failure_before_the_view_is_read_has_nothing_to_put_back()
    {
        _word.FailBlock = true;

        var result = await Create().ZoomInAsync(Word, Cursor, CancellationToken.None);

        Assert.Equal(ZoomReason.AutomationFailed, result.Reason);
        Assert.Null(_word.Restored);
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
        // The fallback, for when there is no gesture to be had; with one, Word animates the motion itself.
        _word.Block = new WordBlock(PixelRect.FromSize(600, 850, 768, 120), Start: 4200);
        _word.State = new WordViewState(100, 0, 0);

        await Animate(Create(animate: true, gesture: false).ZoomInAsync(Word, Cursor, CancellationToken.None));

        Assert.True(_word.ZoomHistory.Count > 3, "expected several intermediate steps");
        Assert.Equal(200, _word.ZoomHistory[^1]);
        Assert.True(_word.ZoomHistory.Zip(_word.ZoomHistory.Skip(1)).All(pair => pair.Second >= pair.First), "zoom should increase monotonically");
    }

    private sealed class FakeWord : IWordAutomation, IWordWindow
    {
        public bool Attachable { get; set; } = true;

        public bool FailZoom { get; set; }

        public bool FailScroll { get; set; }

        public bool FailBlock { get; set; }

        public WordBlock? Block { get; set; }

        /// <summary>The character position the view was scrolled to, or null if it never was.</summary>
        public int? ScrolledTo { get; private set; }

        public WordViewState State { get; set; } = new(100, 0, 0);

        public int? Zoom { get; private set; }

        public List<int> ZoomHistory { get; } = [];

        public bool ScrolledIntoView { get; private set; }

        public WordViewState? Restored { get; private set; }

        public PixelRect Viewport { get; set; } = Pane;

        public Task<IWordWindow?> AttachAsync(TargetInfo target, CancellationToken cancellationToken) =>
            Task.FromResult<IWordWindow?>(Attachable ? this : null);

        public WordViewState GetState() => Zoom is { } z ? State with { ZoomPercent = z } : State;

        public WordBlock? GetBlockAt(ScreenPoint point) => FailBlock ? throw Busy() : Block;

        public void SetZoom(int percent)
        {
            if (FailZoom)
                throw Busy();

            Zoom = percent;
            ZoomHistory.Add(percent);
        }

        public void ScrollIntoView(int start)
        {
            if (FailScroll)
                throw Busy();

            ScrolledIntoView = true;
            ScrolledTo = start;
        }

        // Exactly what Word's object model throws when it is busy; the adapter catches this type.
#pragma warning disable CA2201
        private static System.Runtime.InteropServices.COMException Busy() =>
            new("Word is busy.", unchecked((int)0x800AC472));
#pragma warning restore CA2201

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
