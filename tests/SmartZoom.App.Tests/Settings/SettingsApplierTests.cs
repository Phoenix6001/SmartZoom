using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

using Serilog.Core;
using Serilog.Events;

using SmartZoom.App.Diagnostics;
using SmartZoom.App.Hosting;
using SmartZoom.App.Settings;
using SmartZoom.App.Tests.Diagnostics;
using SmartZoom.App.Tests.Hosting;
using SmartZoom.Core.Input;
using SmartZoom.Core.Settings;
using SmartZoom.Core.Zoom;

namespace SmartZoom.App.Tests.Settings;

/// <summary>
/// The one place settings change, run against the real store, engine, factory and recorder over a directory
/// of the test's own; only the input hook and the Win32 seams are fakes.
/// </summary>
public sealed class SettingsApplierTests
{
    private static SmartZoomSettings Candidate(MouseButton button = MouseButton.Middle) => new()
    {
        Triggers = [new TriggerSettings { Mouse = button, TapCount = 1 }],
    };

    public sealed class ApplyAsync
    {
        [Fact]
        public async Task Rejects_settings_with_an_error_and_changes_nothing()
        {
            using var harness = new Harness();
            var candidate = Candidate();
            candidate.Triggers.Clear();

            var result = await harness.Applier.ApplyAsync(candidate);

            Assert.Equal(SettingsApplyOutcome.Rejected, result.Outcome);
            Assert.Contains(result.Problems, p => p.Section == "Triggers" && p.Severity == SettingsProblemSeverity.Error);
            Assert.Same(harness.Initial, harness.Holder.Current);
            Assert.Null(harness.Triggers.CurrentTriggers);
            Assert.False(File.Exists(harness.Paths.SettingsFile));
        }

        [Fact]
        public async Task Puts_every_part_of_the_settings_into_force_and_then_writes_the_file()
        {
            using var harness = new Harness();
            var candidate = Candidate(MouseButton.XButton1);
            candidate.Enabled = false;
            candidate.Logging.Level = LogLevel.Warning;
            candidate.Diagnostics.Enabled = false;

            var result = await harness.Applier.ApplyAsync(candidate);

            Assert.Equal(SettingsApplyOutcome.Applied, result.Outcome);
            Assert.Same(candidate, harness.Holder.Current);
            var trigger = Assert.IsType<MouseButtonTrigger>(Assert.Single(harness.Triggers.CurrentTriggers!));
            Assert.Equal(MouseButton.XButton1, trigger.Button);
            Assert.False(harness.Triggers.Enabled);
            Assert.Equal(LogEventLevel.Warning, harness.LogLevel.MinimumLevel);
            Assert.False(harness.Recorder.Enabled);

            Assert.True(harness.Store.TryLoad(out var written, out _));
            Assert.Equal(MouseButton.XButton1, Assert.Single(written.Triggers).Mouse);
            Assert.False(written.Enabled);
        }

        [Fact]
        public async Task Reports_settings_that_are_in_force_but_could_not_be_written()
        {
            using var harness = new Harness();

            // A directory where the file belongs: the temp file is written and the replace fails.
            Directory.CreateDirectory(harness.Paths.SettingsFile);
            var candidate = Candidate();

            var result = await harness.Applier.ApplyAsync(candidate);

            Assert.Equal(SettingsApplyOutcome.AppliedButNotSaved, result.Outcome);
            Assert.True(result.InForce);
            Assert.NotNull(result.Detail);
            Assert.Same(candidate, harness.Holder.Current);
            Assert.NotNull(harness.Triggers.CurrentTriggers);
        }

        [Fact]
        public async Task Warns_when_a_zoom_in_flight_forced_the_swap()
        {
            // The engine's answer is the user's business: a window zoomed by a strategy that changed will
            // zoom in again rather than come back, and the person who pressed Save is the one to tell.
            var blocking = new BlockingAdapter();
            using var harness = new Harness(blocking, replaceTimeout: TimeSpan.FromMilliseconds(50));
            var zoom = harness.Engine.HandleTriggerAsync(PipelineFixtures.Target(BlockingAdapter.Process), new ScreenPoint(1, 1), CancellationToken.None);
            await blocking.Entered;

            var result = await harness.Applier.ApplyAsync(Candidate());

            Assert.Equal(SettingsApplyOutcome.Applied, result.Outcome);
            Assert.Contains(result.Problems, p => p.Section == "Zoom" && p.Severity == SettingsProblemSeverity.Warning);

            blocking.Release();
            await zoom;
        }
    }

    public sealed class ToSerilogLevel
    {
        [Fact]
        public void None_turns_the_log_off_rather_than_keeping_fatal_errors() =>
            Assert.Equal(LevelAlias.Off, SettingsApplier.ToSerilogLevel(LogLevel.None));

        [Theory]
        [InlineData(LogLevel.Trace, LogEventLevel.Verbose)]
        [InlineData(LogLevel.Debug, LogEventLevel.Debug)]
        [InlineData(LogLevel.Information, LogEventLevel.Information)]
        [InlineData(LogLevel.Warning, LogEventLevel.Warning)]
        [InlineData(LogLevel.Error, LogEventLevel.Error)]
        [InlineData(LogLevel.Critical, LogEventLevel.Fatal)]
        public void Maps_each_level_to_its_serilog_counterpart(LogLevel level, LogEventLevel expected) =>
            Assert.Equal(expected, SettingsApplier.ToSerilogLevel(level));
    }

    public sealed class ReloadAsync
    {
        [Fact]
        public async Task Applies_the_file_as_it_stands()
        {
            using var harness = new Harness();
            harness.Store.Save(Candidate(MouseButton.XButton1));

            var result = await harness.Applier.ReloadAsync();

            Assert.Equal(SettingsApplyOutcome.Applied, result.Outcome);
            Assert.Equal(MouseButton.XButton1, Assert.Single(harness.Holder.Current.Triggers).Mouse);
        }

        [Fact]
        public async Task Rejects_a_malformed_file_and_leaves_it_exactly_as_the_user_left_it()
        {
            // The user's edit is the point of a reload. A file that cannot be read is reported, never
            // replaced with defaults, and the settings in force stay in force.
            using var harness = new Harness();
            const string Broken = "{ \"Triggers\": [ this is not json";
            Directory.CreateDirectory(harness.Paths.SettingsDirectory);
            File.WriteAllText(harness.Paths.SettingsFile, Broken);

            var result = await harness.Applier.ReloadAsync();

            Assert.Equal(SettingsApplyOutcome.Rejected, result.Outcome);
            var problem = Assert.Single(result.Problems);
            Assert.Equal("Settings file", problem.Section);
            Assert.Contains("not valid JSON", problem.Message, StringComparison.Ordinal);
            Assert.Equal(Broken, File.ReadAllText(harness.Paths.SettingsFile));
            Assert.Same(harness.Initial, harness.Holder.Current);
            Assert.Null(harness.Triggers.CurrentTriggers);
        }

        [Fact]
        public async Task Rejects_a_missing_file_without_creating_one()
        {
            using var harness = new Harness();

            var result = await harness.Applier.ReloadAsync();

            Assert.Equal(SettingsApplyOutcome.Rejected, result.Outcome);
            Assert.Contains("no settings file", Assert.Single(result.Problems).Message, StringComparison.Ordinal);
            Assert.False(File.Exists(harness.Paths.SettingsFile));
        }

        [Fact]
        public async Task Rejects_a_file_that_cannot_be_read_rather_than_faulting()
        {
            // An editor holding the file exclusively is an IOException, which is an answer for the user,
            // not a task that dies quietly on a thread-pool thread.
            using var harness = new Harness();
            harness.Store.Save(Candidate());
            using var locked = new FileStream(harness.Paths.SettingsFile, FileMode.Open, FileAccess.ReadWrite, FileShare.None);

            var result = await harness.Applier.ReloadAsync();

            Assert.Equal(SettingsApplyOutcome.Rejected, result.Outcome);
            Assert.Contains("could not be read", Assert.Single(result.Problems).Message, StringComparison.Ordinal);
            Assert.Same(harness.Initial, harness.Holder.Current);
        }
    }

    public sealed class SetEnabledAsync
    {
        [Fact]
        public async Task Publishes_a_copy_with_the_switch_flipped_and_never_edits_the_settings_already_published()
        {
            using var harness = new Harness();

            var result = await harness.Applier.SetEnabledAsync(false);

            Assert.Equal(SettingsApplyOutcome.Applied, result.Outcome);
            Assert.NotSame(harness.Initial, harness.Holder.Current);
            Assert.True(harness.Initial.Enabled);
            Assert.False(harness.Holder.Current.Enabled);
            Assert.False(harness.Triggers.Enabled);

            Assert.True(harness.Store.TryLoad(out var written, out _));
            Assert.False(written.Enabled);
        }

        [Fact]
        public async Task Interleaved_with_applies_leaves_a_file_that_matches_what_is_in_force()
        {
            // Both entry points write the same file through the same temp file; without one gate the two
            // writers could replace each other's temp file or publish out of order. Every apply and every
            // toggle here is one atomic publish-then-write, so the file at the end describes the settings
            // in force at the end.
            using var harness = new Harness();
            var work = new List<Task>();
            for (var i = 0; i < 20; i++)
            {
                var button = i % 2 == 0 ? MouseButton.Middle : MouseButton.XButton1;
                var enabled = i % 3 == 0;
                work.Add(Task.Run(() => harness.Applier.ApplyAsync(Candidate(button))));
                work.Add(Task.Run(() => harness.Applier.SetEnabledAsync(enabled)));
            }

            await Task.WhenAll(work);

            Assert.True(harness.Store.TryLoad(out var written, out var error), error);
            Assert.Equal(harness.Holder.Current.Enabled, written.Enabled);
            Assert.Equal(harness.Holder.Current.Triggers[0].Mouse, written.Triggers[0].Mouse);
            Assert.Equal(harness.Holder.Current.Enabled, harness.Triggers.Enabled);
            Assert.False(File.Exists(harness.Paths.SettingsFile + ".tmp"), "No write may leave its temp file behind.");
        }
    }

    /// <summary>Everything the applier is built from, with the initial settings already in force.</summary>
    private sealed class Harness : IDisposable
    {
        private readonly TempDirectory _temp = new();

        public Harness(IZoomAdapter? initialAdapter = null, TimeSpan? replaceTimeout = null)
        {
            Paths = DiagnosticFixtures.Paths(_temp.Path);
            Store = new SettingsStore(Paths, NullLogger<SettingsStore>.Instance);
            Holder = new SettingsHolder(Initial);
            Recorder = DiagnosticFixtures.CreateRecorder(_temp.Path);

            var state = new WindowZoomStateStore();
            var factory = PipelineFixtures.CreateFactory(state);
            var initialPipeline = initialAdapter is null ? factory.Build(Initial) : PipelineFixtures.Pipeline(state, initialAdapter);
            Engine = new ZoomEngine(initialPipeline, state, NullLogger<ZoomEngine>.Instance, replaceTimeout ?? ZoomEngine.ReplaceTimeout);

            Applier = new SettingsApplier(
                Holder, Store, factory, Engine, Triggers, new FixedSystemInput(), Recorder, LogLevel, NullLogger<SettingsApplier>.Instance);
        }

        public SmartZoomSettings Initial { get; } = new();

        public AppPaths Paths { get; }

        public SettingsStore Store { get; }

        public SettingsHolder Holder { get; }

        public FakeTriggerSource Triggers { get; } = new();

        public DiagnosticRecorder Recorder { get; }

        public LoggingLevelSwitch LogLevel { get; } = new(LogEventLevel.Debug);

        public ZoomEngine Engine { get; }

        public SettingsApplier Applier { get; }

        public void Dispose()
        {
            Applier.Dispose();
            Engine.Dispose();
            _temp.Dispose();
        }
    }

    private sealed class FixedSystemInput : ISystemInput
    {
        public uint DoubleClickTimeMs => 500;
    }
}
