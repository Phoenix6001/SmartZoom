using SmartZoom.Core.Diagnostics;

namespace SmartZoom.Core.Tests.Diagnostics;

public class DiagnosticRecordTests
{
    private static readonly DateTimeOffset Noon = new(2026, 9, 22, 12, 0, 0, TimeSpan.Zero);

    private static DiagnosticKey Key(string process = "msedge") =>
        new(DiagnosticKind.ZoomedNothing, process, "Browser", "NoBlock");

    public sealed class The_same_thing_happening_twice
    {
        [Fact]
        public void Is_one_counter_with_a_count_of_two()
        {
            var record = new DiagnosticRecord("0.1.0");

            record.Note(Key(), Noon);
            record.Note(Key(), Noon.AddMinutes(5));

            var counter = Assert.Single(record.Counters);
            Assert.Equal(2, counter.Count);
            Assert.Equal(Noon, counter.FirstSeen);
            Assert.Equal(Noon.AddMinutes(5), counter.LastSeen);
        }
    }

    public sealed class Counters_with_the_same_count
    {
        [Fact]
        public void Come_back_in_the_same_order_every_time()
        {
            var first = new DiagnosticRecord("0.1.0");
            var second = new DiagnosticRecord("0.1.0");

            string[] processes = ["winword", "msedge", "excel", "acrobat", "brave", "chrome"];
            foreach (var process in processes)
                first.Note(Key(process), Noon);
            foreach (var process in processes.Reverse())
                second.Note(Key(process), Noon);

            // Same data, inserted in the opposite order: without a tiebreak the rows can differ, and a user
            // comparing two reports sees a difference that is not one.
            Assert.Equal(
                first.Counters.Select(c => c.Key.Process),
                second.Counters.Select(c => c.Key.Process));
        }

        [Fact]
        public void Still_come_after_a_counter_that_happened_more_often()
        {
            var record = new DiagnosticRecord("0.1.0");
            record.Note(Key("aaa-rare"), Noon);
            record.Note(Key("zzz-common"), Noon);
            record.Note(Key("zzz-common"), Noon);

            Assert.Equal("zzz-common", record.Counters[0].Key.Process);
        }
    }

    public sealed class A_restored_counter
    {
        [Fact]
        public void Carries_its_stored_count_and_both_timestamps()
        {
            var record = new DiagnosticRecord("0.1.0");

            record.Restore(Key(), count: 43, firstSeen: Noon, lastSeen: Noon.AddHours(3));

            var counter = Assert.Single(record.Counters);
            Assert.Equal(43, counter.Count);
            Assert.Equal(Noon, counter.FirstSeen);
            Assert.Equal(Noon.AddHours(3), counter.LastSeen);
        }

        [Fact]
        public void Is_clamped_so_a_hand_edited_file_cannot_cost_unbounded_work()
        {
            var record = new DiagnosticRecord("0.1.0");

            record.Restore(Key(), count: int.MaxValue, firstSeen: Noon, lastSeen: Noon);

            Assert.Equal(DiagnosticRecord.MaxRestoredCount, Assert.Single(record.Counters).Count);
        }

        [Fact]
        public void Still_obeys_the_key_cap()
        {
            var record = new DiagnosticRecord("0.1.0");

            for (var i = 0; i < DiagnosticRecord.MaxKeys + 4; i++)
                record.Restore(Key($"app{i}"), count: 2, firstSeen: Noon, lastSeen: Noon);

            Assert.Equal(DiagnosticRecord.MaxKeys, record.Counters.Count);
            Assert.Equal(4, record.OmittedKeys);
        }
    }

    public sealed class More_keys_than_the_cap
    {
        [Fact]
        public void Keeps_the_first_two_hundred_and_counts_the_rest()
        {
            var record = new DiagnosticRecord("0.1.0");

            for (var i = 0; i < 205; i++)
                record.Note(Key($"app{i}"), Noon);

            Assert.Equal(200, record.Counters.Count);
            Assert.Equal(5, record.OmittedKeys);
        }

        [Fact]
        public void Still_counts_a_key_it_already_knows()
        {
            var record = new DiagnosticRecord("0.1.0");
            for (var i = 0; i < 205; i++)
                record.Note(Key($"app{i}"), Noon);

            record.Note(Key("app0"), Noon);

            Assert.Equal(2, record.Counters.Single(c => c.Key.Process == "app0").Count);
        }
    }

    public sealed class More_samples_than_the_ring
    {
        [Fact]
        public void Keeps_only_the_most_recent_twenty()
        {
            var record = new DiagnosticRecord("0.1.0");

            for (var i = 0; i < 25; i++)
                record.Sample(new DiagnosticSample(Key(), Noon, $"shape{i}", null));

            Assert.Equal(20, record.Samples.Count);
            Assert.Equal("shape24", record.Samples[^1].Detail);
            Assert.DoesNotContain(record.Samples, s => s.Detail == "shape4");
        }
    }

    public sealed class A_record_from_another_version
    {
        [Fact]
        public void Carries_its_version_so_a_loader_can_reject_it()
        {
            Assert.Equal("0.2.0", new DiagnosticRecord("0.2.0").Version);
        }
    }

    public sealed class A_copy_made_with_the_copy_constructor
    {
        [Fact]
        public void Is_unaffected_by_later_mutation_of_the_source()
        {
            var source = new DiagnosticRecord("0.1.0");
            source.Note(Key(), Noon);
            source.Sample(new DiagnosticSample(Key(), Noon, "before", null));
            source.Gestures.Add(frames: 18, intervalMs: 17, lateFrames: 1, worstLateMs: 9.0);

            var copy = new DiagnosticRecord(source);

            source.Note(Key(), Noon.AddMinutes(5));
            source.Note(Key("other"), Noon);
            source.Sample(new DiagnosticSample(Key(), Noon.AddMinutes(5), "after", null));
            source.Gestures.Add(frames: 18, intervalMs: 17, lateFrames: 5, worstLateMs: 40.0);

            var counter = Assert.Single(copy.Counters);
            Assert.Equal(1, counter.Count);
            Assert.Equal(Noon, counter.LastSeen);
            var sample = Assert.Single(copy.Samples);
            Assert.Equal("before", sample.Detail);
            Assert.Equal(1, copy.Gestures.Gestures);
            Assert.Equal(18, copy.Gestures.Frames);
            Assert.Equal(1, copy.Gestures.LateFrames);
            Assert.Equal(9.0, copy.Gestures.WorstLateMs);
        }

        [Fact]
        public void Carries_the_same_version_and_omitted_count()
        {
            var source = new DiagnosticRecord("0.3.0");
            for (var i = 0; i < 201; i++)
                source.Note(Key($"app{i}"), Noon);

            var copy = new DiagnosticRecord(source);

            Assert.Equal("0.3.0", copy.Version);
            Assert.Equal(1, copy.OmittedKeys);
            Assert.Equal(200, copy.Counters.Count);
        }
    }
}
