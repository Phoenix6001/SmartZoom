using SmartZoom.Core.Diagnostics;

namespace SmartZoom.Core.Tests.Diagnostics;

public class DiagnosticReportTests
{
    private static readonly DateTimeOffset Noon = new(2026, 9, 22, 12, 0, 0, TimeSpan.Zero);

    private static string Render(DiagnosticRecord record, string settings = "{}", string? logTail = null) =>
        DiagnosticReport.Render(record, new FakeMachineFacts(), settings, logTail, t => t);

    public sealed class Whatever_it_is_given
    {
        // The contract in docs/diagnostics-design.md, as a test. A comment promising this is worth little
        // to somebody reviewing from outside; a failing build when a field is added in a year is worth much.
        [Fact]
        public void A_report_never_contains_a_window_title()
        {
            var record = new DiagnosticRecord("0.1.0");
            record.Sample(new DiagnosticSample(
                new DiagnosticKey(DiagnosticKind.ZoomedNothing, "msedge", "Browser", "NoBlock"),
                Noon,
                Detail: "Group 949x79 < Document 3832x2074",
                Exception: null));

            var report = Render(record);

            Assert.DoesNotContain("Quarterly results - Microsoft Edge", report, StringComparison.Ordinal);
            Assert.Contains("Group 949x79", report, StringComparison.Ordinal);
        }

        [Fact]
        public void A_report_contains_no_absolute_user_path()
        {
            var record = new DiagnosticRecord("0.1.0");
            var settings = """{"LogDirectory":"C:\\Users\\ada\\AppData\\Local\\SmartZoom\\logs"}""";

            var report = DiagnosticReport.Render(
                record,
                new FakeMachineFacts(),
                settings,
                logTail: null,
                redact: t => Redaction.Paths(t, @"C:\Users\ada", @"C:\Users\ada\AppData\Local", @"C:\Users\ada\AppData\Roaming"));

            Assert.DoesNotContain(@"C:\Users\ada", report, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("%LOCALAPPDATA%", report, StringComparison.Ordinal);
        }
    }

    public sealed class The_machine_section
    {
        [Fact]
        public void Names_the_refresh_rate_because_that_is_what_gesture_pacing_depends_on()
        {
            var report = Render(new DiagnosticRecord("0.1.0"));

            Assert.Contains("3840x2160", report, StringComparison.Ordinal);
            Assert.Contains("59", report, StringComparison.Ordinal);
            Assert.Contains("200%", report, StringComparison.Ordinal);
        }
    }

    public sealed class The_counter_table
    {
        [Fact]
        public void Puts_the_most_frequent_first()
        {
            var record = new DiagnosticRecord("0.1.0");
            record.Note(new DiagnosticKey(DiagnosticKind.ZoomedNothing, "rare", "Browser", "NoBlock"), Noon);
            for (var i = 0; i < 9; i++)
                record.Note(new DiagnosticKey(DiagnosticKind.ZoomedNothing, "common", "Browser", "NoBlock"), Noon);

            var report = Render(record);

            Assert.True(
                report.IndexOf("common", StringComparison.Ordinal) < report.IndexOf("rare", StringComparison.Ordinal),
                "the nine-times entry should be listed above the once entry");
        }
    }

    public sealed class The_log_tail
    {
        [Fact]
        public void Is_absent_unless_it_was_asked_for()
        {
            Assert.DoesNotContain("Recent log", Render(new DiagnosticRecord("0.1.0")), StringComparison.Ordinal);
            Assert.Contains("Recent log", Render(new DiagnosticRecord("0.1.0"), logTail: "a line"), StringComparison.Ordinal);
        }
    }
}
