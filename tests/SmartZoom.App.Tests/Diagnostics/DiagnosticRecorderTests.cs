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
    }

    /// <summary>A directory under the OS temp path, deleted on dispose, so store I/O in tests never touches
    /// the machine's real diagnostics file.</summary>
    private sealed class TempDirectory : IDisposable
    {
        public string Path { get; } = Directory.CreateTempSubdirectory("smartzoom-diag-test-").FullName;

        public void Dispose()
        {
            try
            {
                Directory.Delete(Path, recursive: true);
            }
            catch (IOException)
            {
                // Best effort; the OS temp folder gets cleaned up eventually regardless.
            }
        }
    }
}
