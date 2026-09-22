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
}
