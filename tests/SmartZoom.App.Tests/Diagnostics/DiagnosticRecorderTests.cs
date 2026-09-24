using SmartZoom.App.Diagnostics;
using SmartZoom.Core.Diagnostics;

namespace SmartZoom.App.Tests.Diagnostics;

public class DiagnosticRecorderTests
{
    private static DiagnosticKey Key(string process = "msedge") =>
        new(DiagnosticKind.ZoomedNothing, process, "Browser", "NoBlock");

    public sealed class The_Snapshot_method
    {
        [Fact]
        public void Is_unaffected_by_mutation_of_the_recorder_after_it_was_taken()
        {
            using var temp = new TempDirectory();
            var recorder = DiagnosticFixtures.CreateRecorder(temp.Path);
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
            var recorder = DiagnosticFixtures.CreateRecorder(temp.Path);

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
            var recorder = DiagnosticFixtures.CreateRecorder(temp.Path);

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

    public sealed class The_Flush_method
    {
        [Fact]
        public void Keeps_the_record_pending_when_the_write_failed()
        {
            using var temp = new TempDirectory();
            var paths = DiagnosticFixtures.Paths(temp.Path);
            var recorder = DiagnosticFixtures.CreateRecorder(temp.Path);
            recorder.Note(Key());

            // A directory standing where the file belongs: the write fails and the store says so.
            Directory.CreateDirectory(paths.DiagnosticsFile);
            recorder.Flush();
            Assert.False(File.Exists(paths.DiagnosticsFile));

            // A transient failure must not cost the session its record: the shutdown flush still has work.
            Directory.Delete(paths.DiagnosticsFile);
            recorder.Flush();

            Assert.Equal(Key(), Assert.Single(DiagnosticFixtures.CreateStore(temp.Path).Load(DiagnosticFixtures.Version).Counters).Key);
        }

        [Fact]
        public void Does_not_write_again_when_nothing_changed()
        {
            using var temp = new TempDirectory();
            var paths = DiagnosticFixtures.Paths(temp.Path);
            var recorder = DiagnosticFixtures.CreateRecorder(temp.Path);
            recorder.Note(Key());
            recorder.Flush();

            var written = File.GetLastWriteTimeUtc(paths.DiagnosticsFile);
            File.Delete(paths.DiagnosticsFile);
            recorder.Flush();

            Assert.False(File.Exists(paths.DiagnosticsFile), "A clean record must not be written again.");
            Assert.NotEqual(default, written);
        }

        [Fact]
        public void Serialises_concurrent_flushes_so_that_nothing_recorded_is_lost()
        {
            // The timer's flush and a crash's flush can run at the same moment. The recorder holds one lock
            // across snapshot, save and mark, so an older snapshot can never be written after a newer one
            // while the newer one's changes are marked as saved. Without that, the file would sit one
            // snapshot behind and the next flush would see nothing to do.
            using var temp = new TempDirectory();
            var recorder = DiagnosticFixtures.CreateRecorder(temp.Path);

            const int Writers = 8;
            const int NotesPerWriter = 25;
            Parallel.For(0, Writers, writer =>
            {
                for (var i = 0; i < NotesPerWriter; i++)
                {
                    recorder.Note(Key($"process-{writer}-{i}"));
                    recorder.Flush();
                }
            });

            // Everything is flushed by now if and only if the recorder's own bookkeeping is right: this
            // flush must find nothing to do, and the file must already hold every key.
            recorder.Flush();

            var stored = DiagnosticFixtures.CreateStore(temp.Path).Load(DiagnosticFixtures.Version);
            Assert.Equal(Writers * NotesPerWriter, stored.Counters.Count);
        }
    }

    public sealed class Enabled
    {
        [Fact]
        public void Defaults_to_true()
        {
            using var temp = new TempDirectory();
            Assert.True(DiagnosticFixtures.CreateRecorder(temp.Path).Enabled);
        }

        [Fact]
        public void When_false_Note_and_Sample_do_nothing()
        {
            using var temp = new TempDirectory();
            var recorder = DiagnosticFixtures.CreateRecorder(temp.Path);
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
            var recorder = DiagnosticFixtures.CreateRecorder(temp.Path);
            recorder.Enabled = false;

            recorder.Paced(frames: 18, intervalMs: 17, lateFrames: 1, worstLateMs: 9.0);

            var snapshot = recorder.Snapshot();
            Assert.Equal(0, snapshot.Gestures.Gestures);
        }
    }
}
