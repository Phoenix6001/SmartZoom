using SmartZoom.Core.Diagnostics;

namespace SmartZoom.Core.Tests.Diagnostics;

public class GestureHealthTests
{
    public sealed class Several_gestures
    {
        [Fact]
        public void Add_up_and_keep_the_worst_lateness()
        {
            var health = new GestureHealth();

            health.Add(frames: 18, intervalMs: 17, lateFrames: 0, worstLateMs: 12.4);
            health.Add(frames: 18, intervalMs: 17, lateFrames: 3, worstLateMs: 21.9);
            health.Add(frames: 18, intervalMs: 17, lateFrames: 1, worstLateMs: 9.0);

            Assert.Equal(3, health.Gestures);
            Assert.Equal(54, health.Frames);
            Assert.Equal(4, health.LateFrames);
            Assert.Equal(21.9, health.WorstLateMs);
            Assert.Equal(17, health.IntervalMs);
        }

        [Fact]
        public void Are_absent_from_the_report_until_one_has_happened()
        {
            var record = new DiagnosticRecord("0.1.0");

            var report = DiagnosticReport.Render(record, new FakeMachineFacts(), "{}", null, t => t);

            Assert.DoesNotContain("Gesture health", report, StringComparison.Ordinal);
        }

        [Fact]
        public void Appear_in_the_report_once_one_has()
        {
            var record = new DiagnosticRecord("0.1.0");
            record.Gestures.Add(frames: 18, intervalMs: 17, lateFrames: 3, worstLateMs: 21.9);

            var report = DiagnosticReport.Render(record, new FakeMachineFacts(), "{}", null, t => t);

            Assert.Contains("Gesture health", report, StringComparison.Ordinal);
            Assert.Contains("17 ms", report, StringComparison.Ordinal);
        }
    }
}
