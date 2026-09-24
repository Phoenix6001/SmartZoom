using Microsoft.Extensions.Logging.Abstractions;

using SmartZoom.App.Hosting;
using SmartZoom.Core.Input;
using SmartZoom.Core.Routing;
using SmartZoom.Core.Zoom;

namespace SmartZoom.App.Tests.Hosting;

/// <summary>
/// The engine is what lets settings change while the app runs: it swaps the pipeline behind the gate a
/// zoom holds, and it forgets remembered zooms the new pipeline could not undo.
/// </summary>
public sealed class ZoomEngineTests
{
    private static readonly ScreenPoint Point = new(10, 20);

    private static ZoomEngine CreateEngine(ZoomPipeline initial, WindowZoomStateStore state, TimeSpan? replaceTimeout = null) =>
        new(initial, state, NullLogger<ZoomEngine>.Instance, replaceTimeout ?? ZoomEngine.ReplaceTimeout);

    public sealed class ReplaceAsync
    {
        [Fact]
        public async Task Puts_the_new_pipeline_in_force_and_reports_a_clean_swap()
        {
            var state = new WindowZoomStateStore();
            var before = PipelineFixtures.Pipeline(state, new StringAdapter("A"));
            var after = PipelineFixtures.Pipeline(state, new StringAdapter("A"));
            var engine = CreateEngine(before, state);

            var clean = await engine.ReplaceAsync(after);

            Assert.True(clean);
            Assert.Same(after, engine.Current);
        }

        [Fact]
        public async Task Forgets_the_zooms_of_a_strategy_whose_restore_state_changed_and_keeps_the_others()
        {
            // A restore state saved by one adapter reaching a different adapter behind the same id is the
            // reader's two modes; the coordinator would refuse it and zoom in again. Dropping the entry is
            // the honest thing, and only that entry: a strategy that did not change still undoes its zooms.
            var state = new WindowZoomStateStore();
            state.Save(PipelineFixtures.Target("a", window: 0x1), new AdapterId("A"), "a's state");
            state.Save(PipelineFixtures.Target("b", window: 0x2), new AdapterId("B"), "b's state");
            var before = PipelineFixtures.Pipeline(state, new StringAdapter("A"), new StringAdapter("B"));
            var after = PipelineFixtures.Pipeline(state, new StringAdapter("A"), new IntAdapter("B"));
            var engine = CreateEngine(before, state);

            await engine.ReplaceAsync(after);

            Assert.Equal(1, state.Count);
            Assert.True(state.TryTake(PipelineFixtures.Target("a", window: 0x1), out var kept, out _));
            Assert.Equal(new AdapterId("A"), kept);
        }

        [Fact]
        public async Task Forgets_the_zooms_of_a_strategy_the_new_pipeline_does_not_have()
        {
            var state = new WindowZoomStateStore();
            state.Save(PipelineFixtures.Target("b", window: 0x2), new AdapterId("B"), "b's state");
            var before = PipelineFixtures.Pipeline(state, new StringAdapter("A"), new StringAdapter("B"));
            var after = PipelineFixtures.Pipeline(state, new StringAdapter("A"));
            var engine = CreateEngine(before, state);

            await engine.ReplaceAsync(after);

            Assert.Equal(0, state.Count);
        }

        [Fact]
        public async Task Swaps_anyway_when_a_zoom_holds_the_gate_too_long_but_leaves_the_remembered_zooms_alone()
        {
            // The zoom in flight finishes on the pipeline it started with and then saves its restore state;
            // clearing entries underneath it would race that save. So a forced swap changes the pipeline,
            // says so, and forgets nothing.
            var state = new WindowZoomStateStore();
            state.Save(PipelineFixtures.Target("b", window: 0x2), new AdapterId("B"), "b's state");
            var blocking = new BlockingAdapter();
            var before = PipelineFixtures.Pipeline(state, blocking, new StringAdapter("B"));
            var after = PipelineFixtures.Pipeline(state, new IntAdapter("B"));
            var engine = CreateEngine(before, state, replaceTimeout: TimeSpan.FromMilliseconds(50));

            var zoom = engine.HandleTriggerAsync(PipelineFixtures.Target(BlockingAdapter.Process), Point, CancellationToken.None);
            await blocking.Entered;

            var clean = await engine.ReplaceAsync(after);

            Assert.False(clean);
            Assert.Same(after, engine.Current);
            Assert.Equal(1, state.Count);

            blocking.Release();
            var outcome = await zoom;
            Assert.Equal(ZoomAction.ZoomedIn, outcome.Action);
        }

        [Fact]
        public async Task Waits_for_a_zoom_in_flight_and_then_swaps_cleanly()
        {
            var state = new WindowZoomStateStore();
            var blocking = new BlockingAdapter();
            var before = PipelineFixtures.Pipeline(state, blocking);
            var after = PipelineFixtures.Pipeline(state, new StringAdapter("A"));

            // Far longer than the test can take: a stalled runner must not turn the wait into a forced swap.
            var engine = CreateEngine(before, state, replaceTimeout: TimeSpan.FromMinutes(5));

            var zoom = engine.HandleTriggerAsync(PipelineFixtures.Target(BlockingAdapter.Process), Point, CancellationToken.None);
            await blocking.Entered;

            var replacing = engine.ReplaceAsync(after);
            await Task.Delay(50);
            Assert.False(replacing.IsCompleted, "A replacement must wait for the zoom that holds the gate.");

            blocking.Release();
            await zoom;

            Assert.True(await replacing);
            Assert.Same(after, engine.Current);
        }
    }

    public sealed class ObsoletedBy
    {
        [Fact]
        public void Names_the_ids_whose_restore_type_changed_or_that_are_gone()
        {
            var state = new WindowZoomStateStore();
            var previous = PipelineFixtures.Pipeline(state, new StringAdapter("Same"), new StringAdapter("Changed"), new StringAdapter("Gone"));
            var current = PipelineFixtures.Pipeline(state, new StringAdapter("Same"), new IntAdapter("Changed"), new StringAdapter("New"));

            var obsolete = current.ObsoletedBy(previous).ToList();

            Assert.Equal([new AdapterId("Changed"), new AdapterId("Gone")], obsolete);
        }
    }

    private sealed class StringAdapter(string id) : ZoomAdapter<string>(new AdapterDescriptor(id, [id.ToLowerInvariant()], id, "Restores from a string."))
    {
        protected override Task<ZoomInResult> ZoomInAsync(TargetInfo target, ScreenPoint point, CancellationToken cancellationToken) =>
            Task.FromResult(Applied("state"));

        protected override Task ZoomOutAsync(TargetInfo target, string restoreState, CancellationToken cancellationToken) =>
            Task.CompletedTask;
    }

    private sealed class IntAdapter(string id) : ZoomAdapter<int>(new AdapterDescriptor(id, [id.ToLowerInvariant()], id, "Restores from an int."))
    {
        protected override Task<ZoomInResult> ZoomInAsync(TargetInfo target, ScreenPoint point, CancellationToken cancellationToken) =>
            Task.FromResult(Applied(1));

        protected override Task ZoomOutAsync(TargetInfo target, int restoreState, CancellationToken cancellationToken) =>
            Task.CompletedTask;
    }
}
