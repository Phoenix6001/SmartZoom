using SmartZoom.Core.Diagnostics;

namespace SmartZoom.Core.Tests.Diagnostics;

public class DiagnosticReportTests
{
    private static readonly DateTimeOffset Noon = new(2026, 9, 22, 12, 0, 0, TimeSpan.Zero);

    private static string Render(DiagnosticRecord record, string settings = "{}", string? logTail = null) =>
        DiagnosticReport.Render(record, new FakeMachineFacts(), settings, logTail, t => t);

    public sealed class Whatever_it_is_given
    {
        // The privacy contract lives at the PRODUCER: DiagnosticKey and DiagnosticSample have no title,
        // page-text, URL or coordinate field by construction (Task 2), so nothing of that shape can ever
        // reach the renderer. What the renderer itself can get wrong is failing to redact the free text it
        // *is* given, so that is what these tests exercise.
        [Fact]
        public void Every_free_text_field_is_redacted_before_it_is_rendered()
        {
            const string SettingsSentinel = "SETTINGS-SENTINEL";
            const string DetailSentinel = "DETAIL-SENTINEL";
            const string ExceptionSentinel = "EXCEPTION-SENTINEL";
            const string LogSentinel = "LOG-SENTINEL";

            var record = new DiagnosticRecord("0.1.0");
            record.Sample(new DiagnosticSample(
                new DiagnosticKey(DiagnosticKind.ZoomedNothing, "msedge", "Browser", "NoBlock"),
                Noon,
                Detail: DetailSentinel,
                Exception: ExceptionSentinel));

            static string Redact(string text) => text
                .Replace(SettingsSentinel, "[REDACTED]", StringComparison.Ordinal)
                .Replace(DetailSentinel, "[REDACTED]", StringComparison.Ordinal)
                .Replace(ExceptionSentinel, "[REDACTED]", StringComparison.Ordinal)
                .Replace(LogSentinel, "[REDACTED]", StringComparison.Ordinal);

            var report = DiagnosticReport.Render(record, new FakeMachineFacts(), SettingsSentinel, LogSentinel, Redact);

            Assert.DoesNotContain(SettingsSentinel, report, StringComparison.Ordinal);
            Assert.DoesNotContain(DetailSentinel, report, StringComparison.Ordinal);
            Assert.DoesNotContain(ExceptionSentinel, report, StringComparison.Ordinal);
            Assert.DoesNotContain(LogSentinel, report, StringComparison.Ordinal);
            Assert.Contains("[REDACTED]", report, StringComparison.Ordinal);
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

            Assert.DoesNotContain("ada", report, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("%LOCALAPPDATA%", report, StringComparison.Ordinal);
        }
    }

    public sealed class A_section_that_throws
    {
        // The type's own <remarks> promise this: "A section that throws degrades to 'unavailable' rather
        // than costing the reader the rest of the report."
        [Fact]
        public void Renders_unavailable_without_losing_the_rest_of_the_report()
        {
            var record = new DiagnosticRecord("0.1.0");
            record.Note(new DiagnosticKey(DiagnosticKind.ZoomedNothing, "common", "Browser", "NoBlock"), Noon);

            var facts = new FakeMachineFacts { ThrowOnDisplays = true };

            var report = DiagnosticReport.Render(record, facts, "{}", null, t => t);

            Assert.Contains("unavailable", report, StringComparison.Ordinal);
            Assert.Contains("common", report, StringComparison.Ordinal);
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
