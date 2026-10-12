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
    /// <summary>
    /// A whole settings object, for writing a file to reload from. Reload is the one caller that legitimately
    /// deals in whole objects, because the file IS the new state.
    /// </summary>
    private static SmartZoomSettings OnDisk(MouseButton button = MouseButton.Middle) => new()
    {
        Triggers = [new TriggerSettings { Mouse = button, TapCount = 1 }],
    };

    /// <summary>The ordinary change a test makes: put one trigger in force.</summary>
    private static Action<SmartZoomSettings> Using(MouseButton button = MouseButton.Middle) =>
        settings => settings.Triggers = [new TriggerSettings { Mouse = button, TapCount = 1 }];

    public sealed class ApplyAsync
    {
        [Fact]
        public async Task Rejects_settings_with_an_error_and_changes_nothing()
        {
            using var harness = new Harness();

            var result = await harness.Applier.ApplyAsync(settings => settings.Triggers.Clear());

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

            var result = await harness.Applier.ApplyAsync(settings =>
            {
                Using(MouseButton.XButton1)(settings);
                settings.Enabled = false;
                settings.Logging.Level = LogLevel.Warning;
                settings.Diagnostics.Enabled = false;
            });

            Assert.Equal(SettingsApplyOutcome.Applied, result.Outcome);
            Assert.False(harness.Holder.Current.Enabled);
            Assert.Equal(MouseButton.XButton1, Assert.Single(harness.Holder.Current.Triggers).Mouse);
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

            var result = await harness.Applier.ApplyAsync(Using());

            Assert.Equal(SettingsApplyOutcome.AppliedButNotSaved, result.Outcome);
            Assert.True(result.InForce);
            Assert.NotNull(result.Detail);
            Assert.Equal(MouseButton.Middle, Assert.Single(harness.Holder.Current.Triggers).Mouse);
            Assert.NotNull(harness.Triggers.CurrentTriggers);
        }

        [Fact]
        public async Task A_change_is_handed_a_copy_and_never_the_settings_in_force()
        {
            // Whatever a change does to what it is handed, the object other parts of the app are reading
            // must come through untouched. This is why the quick-settings helpers may edit in place.
            using var harness = new Harness();
            var inForce = harness.Holder.Current;
            var triggersBefore = inForce.Triggers.Count;
            SmartZoomSettings? handed = null;

            await harness.Applier.ApplyAsync(settings =>
            {
                handed = settings;
                settings.Triggers.Add(new TriggerSettings { Mouse = MouseButton.XButton1, TapCount = 1 });
            });

            Assert.NotNull(handed);
            Assert.NotSame(inForce, handed);
            Assert.Equal(triggersBefore, inForce.Triggers.Count);
            Assert.Equal(triggersBefore + 1, harness.Holder.Current.Triggers.Count);
        }

        [Fact]
        public async Task A_change_made_from_a_copy_read_earlier_does_not_undo_one_made_since()
        {
            using var harness = new Harness();

            // A page is opened and reads the settings to fill its controls in.
            _ = SettingsStore.Clone(harness.Holder.Current);

            // Zooming is switched off from the tray while that page is on screen.
            await harness.Applier.SetEnabledAsync(false);
            Assert.False(harness.Holder.Current.Enabled);

            // The page's own control is then moved. It says what to change, not what the result should be.
            await harness.Applier.ApplyAsync(settings => settings.Zoom.MaxScale = 2.5);

            Assert.Equal(2.5, harness.Holder.Current.Zoom.MaxScale);

            // What it read still said Enabled; only the change it asked for may be applied.
            Assert.False(harness.Holder.Current.Enabled);
            Assert.False(harness.Triggers.Enabled);
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

            var result = await harness.Applier.ApplyAsync(Using());

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
            harness.Store.Save(OnDisk(MouseButton.XButton1));

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
            harness.Store.Save(OnDisk());
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
                work.Add(Task.Run(() => harness.Applier.ApplyAsync(Using(button))));
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
    /// <summary>
    /// After a startup on defaults because the settings file was locked, the user's own file is still the truth:
    /// nothing may be written over it, and it is put into force as soon as it can be read.
    /// </summary>
    public sealed class While_the_file_could_not_be_read
    {
        private const string UsersFile = """{ "Zoom": { "MaxScale": 2.5 } }""";

        // Read, but refused by the settings check.
        private const string Refused = """{ "Zoom": { "Smart": { "MarginPx": -1 } } }""";

        [Fact]
        public async Task A_change_is_put_into_force_but_not_written_over_the_users_file()
        {
            using var harness = new Harness();
            harness.FallBackOnALockedFile(UsersFile);

            var result = await harness.Applier.ApplyAsync(Using(MouseButton.XButton1));

            Assert.Equal(SettingsApplyOutcome.AppliedButNotSaved, result.Outcome);
            Assert.Equal(MouseButton.XButton1, Assert.Single(harness.Holder.Current.Triggers).Mouse);
            Assert.Equal(UsersFile, File.ReadAllText(harness.Paths.SettingsFile));
        }

        [Fact]
        public async Task The_users_settings_are_put_into_force_once_the_file_can_be_read()
        {
            using var harness = new Harness();
            harness.FallBackOnALockedFile(UsersFile);
            using var retry = new SettingsFileRetry(harness.Store, harness.Applier, NullLogger<SettingsFileRetry>.Instance, interval: TimeSpan.FromMilliseconds(20));

            await retry.StartAsync(CancellationToken.None);
            await retry.ExecuteTask!.WaitAsync(TimeSpan.FromSeconds(10));

            Assert.Equal(2.5, harness.Holder.Current.Zoom.MaxScale);
            Assert.False(harness.Store.FileUnreadable);
        }

        [Fact]
        public async Task Nothing_is_reloaded_when_the_file_was_read_at_startup()
        {
            using var harness = new Harness();
            using var retry = new SettingsFileRetry(harness.Store, harness.Applier, NullLogger<SettingsFileRetry>.Instance, interval: TimeSpan.FromMilliseconds(20));

            await retry.StartAsync(CancellationToken.None);
            await retry.ExecuteTask!.WaitAsync(TimeSpan.FromSeconds(10));

            Assert.Same(harness.Initial, harness.Holder.Current);
        }

        [Fact]
        public async Task Putting_the_file_into_force_does_not_write_it_back()
        {
            // The file is the user's, comments and all; reading it is not a reason to reformat it.
            using var harness = new Harness();
            const string commented = """
                // mine
                { "Zoom": { "MaxScale": 2.5 } }
                """;
            harness.FallBackOnALockedFile(commented);

            var result = await harness.Applier.ReloadAsync();

            Assert.Equal(SettingsApplyOutcome.Applied, result.Outcome);
            Assert.Equal(2.5, harness.Holder.Current.Zoom.MaxScale);
            Assert.False(harness.Store.FileUnreadable);
            Assert.Equal(commented, File.ReadAllText(harness.Paths.SettingsFile));
        }

        [Fact]
        public async Task A_file_that_is_read_but_refused_is_still_not_written_over()
        {
            using var harness = new Harness();
            harness.FallBackOnALockedFile(UsersFile);
            File.WriteAllText(harness.Paths.SettingsFile, Refused);

            var reload = await harness.Applier.ReloadAsync();
            var change = await harness.Applier.ApplyAsync(Using(MouseButton.XButton1));

            Assert.Equal(SettingsApplyOutcome.Rejected, reload.Outcome);
            Assert.True(harness.Store.FileUnreadable);
            Assert.Equal(SettingsApplyOutcome.AppliedButNotSaved, change.Outcome);
            Assert.Equal(Refused, File.ReadAllText(harness.Paths.SettingsFile));
        }

        [Fact]
        public async Task A_file_deleted_meanwhile_is_written_again_from_the_settings_in_force()
        {
            // Nothing is left to protect, so holding writes back would only lose this session's changes too.
            using var harness = new Harness();
            harness.FallBackOnALockedFile(UsersFile);
            await harness.Applier.ApplyAsync(Using(MouseButton.XButton1));
            File.Delete(harness.Paths.SettingsFile);

            // Missing twice in a row: once could be an editor between deleting it and putting its new copy in place.
            await harness.Applier.ReloadAsync();
            var result = await harness.Applier.ReloadAsync();

            Assert.Equal(SettingsApplyOutcome.Applied, result.Outcome);
            Assert.False(harness.Store.FileUnreadable);
            Assert.True(harness.Store.TryLoad(out var written, out _));
            Assert.Equal(MouseButton.XButton1, Assert.Single(written.Triggers).Mouse);
        }

        [Fact]
        public async Task A_refused_file_is_kept_out_of_force_until_it_is_corrected()
        {
            using var harness = new Harness();
            harness.FallBackOnALockedFile(UsersFile);
            File.WriteAllText(harness.Paths.SettingsFile, Refused);
            using var retry = new SettingsFileRetry(harness.Store, harness.Applier, NullLogger<SettingsFileRetry>.Instance, interval: TimeSpan.FromMilliseconds(20));

            await retry.StartAsync(CancellationToken.None);
            await Task.Delay(200);
            Assert.Same(harness.Initial, harness.Holder.Current);
            Assert.True(harness.Store.FileUnreadable);
            Assert.Equal(Refused, File.ReadAllText(harness.Paths.SettingsFile));

            File.WriteAllText(harness.Paths.SettingsFile, UsersFile);
            await retry.ExecuteTask!.WaitAsync(TimeSpan.FromSeconds(10));

            Assert.Equal(2.5, harness.Holder.Current.Zoom.MaxScale);
            Assert.False(harness.Store.FileUnreadable);
        }

        [Fact]
        public async Task After_the_file_is_refused_the_reason_given_says_it_needs_correcting()
        {
            // It can be read now, so "SmartZoom will use it as soon as it can be read" would no longer be true.
            using var harness = new Harness();
            harness.FallBackOnALockedFile(UsersFile);
            File.WriteAllText(harness.Paths.SettingsFile, Refused);
            await harness.Applier.ReloadAsync();

            var result = await harness.Applier.ApplyAsync(Using(MouseButton.XButton1));

            Assert.Equal(SettingsApplyOutcome.AppliedButNotSaved, result.Outcome);
            Assert.Contains("as soon as it is corrected", result.Detail, StringComparison.Ordinal);
        }

        [Fact]
        public async Task The_user_is_told_once_while_the_file_cannot_be_read_and_again_when_it_is_refused()
        {
            using var harness = new Harness();
            harness.FallBackOnALockedFile(UsersFile);
            var notices = new List<string>();
            harness.Applier.SavingHeldBack += (_, text) => notices.Add(text);

            await harness.Applier.ApplyAsync(Using(MouseButton.XButton1));
            await harness.Applier.SetEnabledAsync(false);
            Assert.Single(notices);

            File.WriteAllText(harness.Paths.SettingsFile, Refused);
            await harness.Applier.ReloadAsync();
            await harness.Applier.SetEnabledAsync(true);

            Assert.Equal(2, notices.Count);
            Assert.Contains("refused", notices[1], StringComparison.Ordinal);
        }

        [Fact]
        public async Task A_file_that_is_not_valid_json_once_it_can_be_read_is_refused_and_left_alone_but_still_tried()
        {
            using var harness = new Harness();
            harness.FallBackOnALockedFile(UsersFile);
            const string broken = """{ "Zoom": { "MaxScale": 2.5 """;
            File.WriteAllText(harness.Paths.SettingsFile, broken);

            // Not given up on: a file read while it was half written is complete by the next attempt.
            var done = await harness.Applier.RetryUnreadableFileAsync();
            var change = await harness.Applier.ApplyAsync(Using(MouseButton.XButton1));

            Assert.False(done);
            Assert.True(harness.Store.FileUnreadable);
            Assert.Equal(SettingsApplyOutcome.AppliedButNotSaved, change.Outcome);
            Assert.Equal(broken, File.ReadAllText(harness.Paths.SettingsFile));
        }

        [Fact]
        public async Task A_file_missing_only_while_an_editor_puts_its_new_copy_in_place_is_not_taken_as_deleted()
        {
            using var harness = new Harness();
            harness.FallBackOnALockedFile(UsersFile);
            File.Delete(harness.Paths.SettingsFile);

            var missing = await harness.Applier.RetryUnreadableFileAsync();
            File.WriteAllText(harness.Paths.SettingsFile, UsersFile);
            var back = await harness.Applier.RetryUnreadableFileAsync();

            Assert.False(missing);
            Assert.True(back);
            Assert.Equal(2.5, harness.Holder.Current.Zoom.MaxScale);
            Assert.Equal(UsersFile, File.ReadAllText(harness.Paths.SettingsFile));
        }

        [Fact]
        public async Task A_file_missing_on_every_other_attempt_is_never_taken_as_deleted()
        {
            // A slow sync client: gone at one attempt, back at the next. Only two misses in a row count.
            using var harness = new Harness();
            harness.FallBackOnALockedFile(UsersFile);
            using (new FileStream(harness.Paths.SettingsFile, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            {
                await harness.Applier.RetryUnreadableFileAsync();
            }

            for (var i = 0; i < 3; i++)
            {
                File.Move(harness.Paths.SettingsFile, harness.Paths.SettingsFile + ".moving");
                Assert.False(await harness.Applier.RetryUnreadableFileAsync());
                File.Move(harness.Paths.SettingsFile + ".moving", harness.Paths.SettingsFile);
                using (new FileStream(harness.Paths.SettingsFile, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
                    Assert.False(await harness.Applier.RetryUnreadableFileAsync());
            }

            Assert.True(harness.Store.FileUnreadable);
            Assert.Equal(UsersFile, File.ReadAllText(harness.Paths.SettingsFile));
        }

        [Fact]
        public async Task A_file_saved_again_between_two_attempts_that_both_find_it_missing_is_not_taken_as_deleted()
        {
            // Each attempt sees one instant. An editor saving by delete and rename, over and over, can be missing
            // at both; what it cannot hide is that a file appeared in between.
            using var harness = new Harness();
            harness.FallBackOnALockedFile(UsersFile);
            File.Delete(harness.Paths.SettingsFile);

            Assert.False(await harness.Applier.RetryUnreadableFileAsync());
            File.WriteAllText(harness.Paths.SettingsFile, UsersFile);
            File.Delete(harness.Paths.SettingsFile);
            await Task.Delay(500); // the watcher reports on its own thread
            Assert.False(await harness.Applier.RetryUnreadableFileAsync());

            Assert.False(File.Exists(harness.Paths.SettingsFile));
            Assert.True(harness.Store.FileUnreadable);
        }

        [Fact]
        public async Task The_retry_leaves_alone_a_file_already_put_into_force_from_the_tray()
        {
            // Reload settings file as soon as the lock clears, before the retry's next attempt: that attempt must
            // not reload it again as an ordinary reload, which writes the file back and drops its comments.
            using var harness = new Harness();
            const string commented = """
                // mine
                { "Zoom": { "MaxScale": 2.5 } }
                """;
            harness.FallBackOnALockedFile(commented);
            await harness.Applier.ReloadAsync();
            var inForce = harness.Holder.Current;

            Assert.True(await harness.Applier.RetryUnreadableFileAsync());

            Assert.Same(inForce, harness.Holder.Current);
            Assert.Equal(commented, File.ReadAllText(harness.Paths.SettingsFile));
        }

        [Fact]
        public async Task A_listener_that_calls_back_into_the_settings_does_not_deadlock()
        {
            using var harness = new Harness();
            harness.FallBackOnALockedFile(UsersFile);
            Task<SettingsApplyResult>? inner = null;
            harness.Applier.SavingHeldBack += (_, _) => inner = harness.Applier.SetEnabledAsync(true);

            await harness.Applier.SetEnabledAsync(false).WaitAsync(TimeSpan.FromSeconds(10));

            Assert.NotNull(inner);
            await inner.WaitAsync(TimeSpan.FromSeconds(10));
        }

        [Fact]
        public async Task A_change_that_is_not_saved_says_why()
        {
            using var harness = new Harness();
            harness.FallBackOnALockedFile(UsersFile);

            var result = await harness.Applier.ApplyAsync(Using(MouseButton.XButton1));

            Assert.Contains("could not be read", result.Detail, StringComparison.Ordinal);
        }
    }

    private sealed class Harness : IDisposable
    {
        private readonly TempDirectory _temp = new();

        public Harness(IZoomAdapter? initialAdapter = null, TimeSpan? replaceTimeout = null)
        {
            Paths = DiagnosticFixtures.Paths(_temp.Path);
            Store = new SettingsStore(Paths, NullLogger<SettingsStore>.Instance, attempts: 2, retryDelay: TimeSpan.FromMilliseconds(10));
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

        /// <summary>Starts the way a locked settings file leaves it: defaults in memory, the user's file on disk.</summary>
        public void FallBackOnALockedFile(string contents)
        {
            Directory.CreateDirectory(Paths.SettingsDirectory);
            File.WriteAllText(Paths.SettingsFile, contents);
            using (new FileStream(Paths.SettingsFile, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
                Store.Load();
        }

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
