using Microsoft.Extensions.Logging.Abstractions;

using SmartZoom.App.Diagnostics;
using SmartZoom.Core.Diagnostics;

namespace SmartZoom.App.Tests.Diagnostics;

public class DiagnosticRecorderTests
{
    private static DiagnosticKey Key(string process = "msedge") =>
        new(DiagnosticKind.ZoomedNothing, process, "Browser", "NoBlock");

    private static DiagnosticRecorder CreateRecorder(string directory) =>
        new(
            new DiagnosticStore(
                new AppPaths(SettingsDirectory: directory, LogDirectory: Path.Combine(directory, "logs")),
                NullLogger<DiagnosticStore>.Instance),
            TimeProvider.System,
            version: "0.1.0-test");

    public sealed class The_Snapshot_method
    {
        [Fact]
        public void Is_unaffected_by_mutation_of_the_recorder_after_it_was_taken()
        {
            using var temp = new TempDirectory();
            var recorder = CreateRecorder(temp.Path);
            recorder.Note(Key());

            var snapshot = recorder.Snapshot();

            // Mutate the live recorder after the snapshot was taken. None of this may be visible through it -
            // that is the entire reason Snapshot exists instead of handing out the live record.
            recorder.Note(Key());
            recorder.Note(Key("other-process"));
            recorder.Sample(new DiagnosticSample(Key(), TimeProvider.System.GetUtcNow(), "after the snapshot", null));

            var counter = Assert.Single(snapshot.Counters);
            Assert.Equal(Key(), counter.Key);
            Assert.Equal(1, counter.Count);
            Assert.Empty(snapshot.Samples);
        }

        [Fact]
        public void Reflects_everything_noted_before_it_was_taken()
        {
            using var temp = new TempDirectory();
            var recorder = CreateRecorder(temp.Path);

            recorder.Note(Key());
            recorder.Note(Key());
            recorder.Sample(new DiagnosticSample(Key(), TimeProvider.System.GetUtcNow(), "before the snapshot", null));

            var snapshot = recorder.Snapshot();

            var counter = Assert.Single(snapshot.Counters);
            Assert.Equal(2, counter.Count);
            var sample = Assert.Single(snapshot.Samples);
            Assert.Equal("before the snapshot", sample.Detail);
        }

        [Fact]
        public void Reflects_gesture_totals_recorded_before_it_and_is_unaffected_by_pacing_after()
        {
            using var temp = new TempDirectory();
            var recorder = CreateRecorder(temp.Path);

            recorder.Paced(frames: 18, intervalMs: 17, lateFrames: 1, worstLateMs: 9.0);

            var snapshot = recorder.Snapshot();

            // Mutate the live recorder after the snapshot was taken, same as the counters/samples test above -
            // none of this may be visible through the snapshot.
            recorder.Paced(frames: 18, intervalMs: 17, lateFrames: 5, worstLateMs: 40.0);

            Assert.Equal(1, snapshot.Gestures.Gestures);
            Assert.Equal(18, snapshot.Gestures.Frames);
            Assert.Equal(1, snapshot.Gestures.LateFrames);
            Assert.Equal(9.0, snapshot.Gestures.WorstLateMs);
        }
    }

    public sealed class Enabled
    {
        [Fact]
        public void Defaults_to_true()
        {
            using var temp = new TempDirectory();
            Assert.True(CreateRecorder(temp.Path).Enabled);
        }

        [Fact]
        public void When_false_Note_and_Sample_do_nothing()
        {
            using var temp = new TempDirectory();
            var recorder = CreateRecorder(temp.Path);
            recorder.Enabled = false;

            recorder.Note(Key());
            recorder.Sample(new DiagnosticSample(Key(), TimeProvider.System.GetUtcNow(), "detail", null));

            var snapshot = recorder.Snapshot();
            Assert.Empty(snapshot.Counters);
            Assert.Empty(snapshot.Samples);
        }

        [Fact]
        public void When_false_Paced_does_nothing()
        {
            using var temp = new TempDirectory();
            var recorder = CreateRecorder(temp.Path);
            recorder.Enabled = false;

            recorder.Paced(frames: 18, intervalMs: 17, lateFrames: 1, worstLateMs: 9.0);

            var snapshot = recorder.Snapshot();
            Assert.Equal(0, snapshot.Gestures.Gestures);
        }
    }
}
