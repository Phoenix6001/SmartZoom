using System.Diagnostics;
using System.Text;
using System.Text.Json;

using SmartZoom.App.Diagnostics;
using SmartZoom.Core.Diagnostics;

namespace SmartZoom.App.Tests.Diagnostics;

/// <summary>
/// The store is what makes the record survive a restart, and what has to refuse a file it cannot trust.
/// Every behaviour here is one docs/diagnostics-design.md names in its Testing section.
/// </summary>
public sealed class DiagnosticStoreTests
{
    private const string Version = DiagnosticFixtures.Version;

    private static DiagnosticStore CreateStore(string directory) => DiagnosticFixtures.CreateStore(directory);

    private static AppPaths Paths(string directory) => DiagnosticFixtures.Paths(directory);

    private static DiagnosticKey Key(string process = "msedge", string reason = "NoBlock") =>
        new(DiagnosticKind.ZoomedNothing, process, "Browser", reason);

    public sealed class Version_scoping
    {
        [Fact]
        public void Resumes_a_file_written_by_this_version()
        {
            using var temp = new TempDirectory();
            var store = CreateStore(temp.Path);

            var record = new DiagnosticRecord(Version);
            record.Note(Key(), DateTimeOffset.UnixEpoch);
            record.Note(Key(), DateTimeOffset.UnixEpoch.AddMinutes(1));
            Assert.True(store.Save(record));

            var counter = Assert.Single(store.Load(Version).Counters);
            Assert.Equal(Key(), counter.Key);
            Assert.Equal(2, counter.Count);
        }

        [Fact]
        public void Discards_a_file_written_by_another_version()
        {
            using var temp = new TempDirectory();
            var store = CreateStore(temp.Path);

            var record = new DiagnosticRecord("0.0.9-previous");
            record.Note(Key(), DateTimeOffset.UnixEpoch);
            Assert.True(store.Save(record));

            // "43 no-op presses in msedge" is misleading if 40 of them predate the fix.
            var loaded = store.Load(Version);
            Assert.Empty(loaded.Counters);
            Assert.Equal(Version, loaded.Version);
        }
    }

    public sealed class A_file_that_cannot_be_read
    {
        [Fact]
        public void Is_discarded_rather_than_thrown_out_of_Load()
        {
            using var temp = new TempDirectory();
            var paths = Paths(temp.Path);
            Directory.CreateDirectory(Path.GetDirectoryName(paths.DiagnosticsFile)!);
            File.WriteAllText(paths.DiagnosticsFile, "{ this is not json");

            var loaded = CreateStore(temp.Path).Load(Version);

            Assert.Empty(loaded.Counters);
            Assert.Empty(loaded.Samples);
            Assert.Equal(0, loaded.Gestures.Gestures);
        }

        [Fact]
        public void Survives_a_file_whose_lists_are_null()
        {
            using var temp = new TempDirectory();
            var paths = Paths(temp.Path);
            Directory.CreateDirectory(Path.GetDirectoryName(paths.DiagnosticsFile)!);
            File.WriteAllText(
                paths.DiagnosticsFile,
                $$"""{"version":"{{Version}}","counters":null,"samples":null,"gestures":null}""");

            var loaded = CreateStore(temp.Path).Load(Version);

            Assert.Empty(loaded.Counters);
        }
    }

    public sealed class The_256_KB_ceiling
    {
        [Fact]
        public void Drops_the_worked_examples_before_it_drops_a_counter()
        {
            using var temp = new TempDirectory();
            var store = CreateStore(temp.Path);

            var record = new DiagnosticRecord(Version);
            for (var i = 0; i < DiagnosticRecord.MaxKeys; i++)
                record.Note(Key($"process-{i}"), DateTimeOffset.UnixEpoch);

            // Each sample carries a stack trace at the documented 4 000-character cap; twenty of those are
            // comfortably more than the ceiling on their own, so the samples are what has to give.
            for (var i = 0; i < DiagnosticRecord.MaxSamples; i++)
            {
                record.Sample(new DiagnosticSample(
                    Key($"process-{i}"), DateTimeOffset.UnixEpoch, new string('d', 20_000), new string('e', 20_000)));
            }

            Assert.True(store.Save(record));

            var written = new FileInfo(Paths(temp.Path).DiagnosticsFile).Length;
            Assert.True(written <= DiagnosticStore.MaxBytes, $"File was {written} bytes.");

            var loaded = store.Load(Version);
            Assert.Empty(loaded.Samples);
            Assert.Equal(DiagnosticRecord.MaxKeys, loaded.Counters.Count);
        }

        [Fact]
        public void Is_measured_in_bytes_so_non_ascii_text_cannot_slip_past_it()
        {
            // A character count is not a byte count: one CJK character is three bytes of UTF-8, and the store
            // writes it as that character rather than as a six-character escape, so the gap is real on disk.
            // Samples sized to sit under the ceiling in characters, and over it in bytes, must still be dropped.
            using var temp = new TempDirectory();
            var store = CreateStore(temp.Path);
            var text = new string('漢', DiagnosticText.MaxExceptionCharacters);

            var small = new DiagnosticRecord(Version);
            small.Note(Key(), DateTimeOffset.UnixEpoch);
            small.Sample(new DiagnosticSample(Key(), DateTimeOffset.UnixEpoch, text, text));
            Assert.True(store.Save(small));

            var json = File.ReadAllText(Paths(temp.Path).DiagnosticsFile);
            Assert.Contains(text, json, StringComparison.Ordinal);
            Assert.True(Encoding.UTF8.GetByteCount(json) > json.Length, "The file must be wider in bytes than in characters.");

            // 20 samples x 2 x 4 000 x 3 bytes = 480 KB, against 160 000 characters, which is under 256 K.
            var record = new DiagnosticRecord(Version);
            record.Note(Key(), DateTimeOffset.UnixEpoch);
            for (var i = 0; i < DiagnosticRecord.MaxSamples; i++)
                record.Sample(new DiagnosticSample(Key($"process-{i}"), DateTimeOffset.UnixEpoch, text, text));

            Assert.True(store.Save(record));

            var written = new FileInfo(Paths(temp.Path).DiagnosticsFile).Length;
            Assert.True(written <= DiagnosticStore.MaxBytes, $"File was {written} bytes.");
            Assert.Empty(store.Load(Version).Samples);
        }
    }

    public sealed class Gesture_health
    {
        [Fact]
        public void Survives_a_save_and_a_load()
        {
            using var temp = new TempDirectory();
            var store = CreateStore(temp.Path);

            var record = new DiagnosticRecord(Version);
            record.Gestures.Add(frames: 18, intervalMs: 17, lateFrames: 1, worstLateMs: 9.5);
            record.Gestures.Add(frames: 18, intervalMs: 17, lateFrames: 0, worstLateMs: 2.0);
            Assert.True(store.Save(record));

            var loaded = store.Load(Version).Gestures;

            Assert.Equal(2, loaded.Gestures);
            Assert.Equal(36, loaded.Frames);
            Assert.Equal(1, loaded.LateFrames);
            Assert.Equal(9.5, loaded.WorstLateMs);
            Assert.Equal(17, loaded.IntervalMs);
        }
    }

    public sealed class A_stored_count_that_is_absurd
    {
        [Fact]
        public void Is_clamped_instead_of_replayed_one_occurrence_at_a_time()
        {
            using var temp = new TempDirectory();
            var paths = Paths(temp.Path);
            Directory.CreateDirectory(Path.GetDirectoryName(paths.DiagnosticsFile)!);
            File.WriteAllText(
                paths.DiagnosticsFile,
                $$"""
                {
                  "version": "{{Version}}",
                  "omittedKeys": 0,
                  "counters": [
                    {
                      "key": { "kind": "ZoomedNothing", "process": "msedge", "adapter": "Browser", "reason": "NoBlock" },
                      "count": 2000000000,
                      "firstSeen": "1970-01-01T00:00:00+00:00",
                      "lastSeen": "1970-01-01T00:01:00+00:00"
                    }
                  ],
                  "samples": []
                }
                """);

            // Load runs inside DiagnosticRecorder's field initialiser, which runs during DI construction:
            // a replay proportional to this number would stop SmartZoom starting.
            var clock = Stopwatch.StartNew();
            var counter = Assert.Single(CreateStore(temp.Path).Load(Version).Counters);
            clock.Stop();

            Assert.Equal(DiagnosticRecord.MaxRestoredCount, counter.Count);
            Assert.True(clock.ElapsedMilliseconds < 1000, $"Load took {clock.ElapsedMilliseconds} ms.");
        }
    }

    public sealed class The_file_on_disk
    {
        [Fact]
        public void Names_the_kind_instead_of_numbering_it()
        {
            using var temp = new TempDirectory();
            var record = new DiagnosticRecord(Version);
            record.Note(new DiagnosticKey(DiagnosticKind.AdapterThrew, "winword", null, "COMException"), DateTimeOffset.UnixEpoch);
            Assert.True(CreateStore(temp.Path).Save(record));

            var json = File.ReadAllText(Paths(temp.Path).DiagnosticsFile);

            // The record is meant to be auditable by hand; "kind": 1 is not.
            Assert.Contains("\"AdapterThrew\"", json, StringComparison.Ordinal);
        }

        [Fact]
        public void Is_still_read_when_an_older_build_numbered_the_kind()
        {
            using var temp = new TempDirectory();
            var paths = Paths(temp.Path);
            Directory.CreateDirectory(Path.GetDirectoryName(paths.DiagnosticsFile)!);
            File.WriteAllText(
                paths.DiagnosticsFile,
                $$"""
                {
                  "version": "{{Version}}",
                  "counters": [
                    {
                      "key": { "kind": 1, "process": "winword", "adapter": null, "reason": "COMException" },
                      "count": 3,
                      "firstSeen": "1970-01-01T00:00:00+00:00",
                      "lastSeen": "1970-01-01T00:01:00+00:00"
                    }
                  ]
                }
                """);

            var counter = Assert.Single(CreateStore(temp.Path).Load(Version).Counters);

            Assert.Equal(DiagnosticKind.AdapterThrew, counter.Key.Kind);
            Assert.Equal(3, counter.Count);
        }
    }

    public sealed class Save
    {
        [Fact]
        public void Reports_failure_when_the_file_cannot_be_written()
        {
            using var temp = new TempDirectory();
            var paths = Paths(temp.Path);

            // A directory where the file belongs: File.WriteAllText cannot replace it.
            Directory.CreateDirectory(paths.DiagnosticsFile);

            var record = new DiagnosticRecord(Version);
            record.Note(Key(), DateTimeOffset.UnixEpoch);

            Assert.False(CreateStore(temp.Path).Save(record));
        }
    }

    public sealed class Round_tripping
    {
        [Fact]
        public void Keeps_the_omitted_key_count_and_the_worked_examples()
        {
            using var temp = new TempDirectory();
            var store = CreateStore(temp.Path);

            var record = new DiagnosticRecord(Version);
            for (var i = 0; i < DiagnosticRecord.MaxKeys + 3; i++)
                record.Note(Key($"process-{i}"), DateTimeOffset.UnixEpoch);
            record.Sample(new DiagnosticSample(Key(), DateTimeOffset.UnixEpoch, "Group 949x79", null));

            Assert.True(store.Save(record));
            var loaded = store.Load(Version);

            Assert.Equal(DiagnosticRecord.MaxKeys, loaded.Counters.Count);
            Assert.Equal(3, loaded.OmittedKeys);
            Assert.Equal("Group 949x79", Assert.Single(loaded.Samples).Detail);
        }

        [Fact]
        public void Does_not_smuggle_more_keys_back_in_than_the_cap_allows()
        {
            using var temp = new TempDirectory();
            var paths = Paths(temp.Path);
            Directory.CreateDirectory(Path.GetDirectoryName(paths.DiagnosticsFile)!);

            var counters = string.Join(",", Enumerable.Range(0, DiagnosticRecord.MaxKeys + 10).Select(i =>
                $$"""
                {"key":{"kind":"ZoomedNothing","process":"p{{i}}","adapter":"Browser","reason":"NoBlock"},
                 "count":1,"firstSeen":"1970-01-01T00:00:00+00:00","lastSeen":"1970-01-01T00:00:00+00:00"}
                """));
            File.WriteAllText(paths.DiagnosticsFile, $$"""{"version":"{{Version}}","counters":[{{counters}}]}""");

            var loaded = CreateStore(temp.Path).Load(Version);

            Assert.Equal(DiagnosticRecord.MaxKeys, loaded.Counters.Count);
            Assert.Equal(10, loaded.OmittedKeys);
        }
    }

    public sealed class A_saved_file
    {
        [Fact]
        public void Is_valid_json_a_person_can_read()
        {
            using var temp = new TempDirectory();
            var record = new DiagnosticRecord(Version);
            record.Note(Key(), DateTimeOffset.UnixEpoch);
            Assert.True(CreateStore(temp.Path).Save(record));

            var json = File.ReadAllText(Paths(temp.Path).DiagnosticsFile);

            using var parsed = JsonDocument.Parse(json);
            Assert.Equal(Version, parsed.RootElement.GetProperty("version").GetString());
            Assert.Contains("\n", json, StringComparison.Ordinal);
        }
    }
}
