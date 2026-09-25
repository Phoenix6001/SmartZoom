using SmartZoom.App.Ui.Pages;

using SmartZoom.Core.Diagnostics;
using SmartZoom.Core.Zoom;

namespace SmartZoom.App.Tests.Ui;

/// <summary>
/// The sentences shown above the diagnostic report. They are the first thing somebody reads after the window
/// tells them there are issues, so they have to say what happened rather than name an enum — and there have to
/// be few enough of them to read.
/// </summary>
public sealed class DiagnosticIssueTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 25, 20, 0, 0, TimeSpan.Zero);

    public sealed class Grouping
    {
        [Fact]
        public void Ten_applications_that_failed_the_same_way_are_one_line()
        {
            // The case this exists for: the application is the only part of a key that grows with the machine,
            // so ten of them with one cause must not become ten lines saying the same sentence.
            var counters = Enumerable.Range(0, 10)
                .Select(i => Counter(DiagnosticKind.ZoomedNothing, $"app{i}", ZoomReason.NoBlock, count: 2))
                .ToList();

            var issues = DiagnosticIssue.Summarise(counters, Now);

            var issue = Assert.Single(issues);
            Assert.Equal("20 presses in 10 applications zoomed nothing", issue.Headline);
        }

        [Fact]
        public void Only_the_busiest_applications_are_named()
        {
            var counters = new List<DiagnosticCounter>
            {
                Counter(DiagnosticKind.ZoomedNothing, "quiet", ZoomReason.NoBlock, count: 1),
                Counter(DiagnosticKind.ZoomedNothing, "loudest", ZoomReason.NoBlock, count: 50),
                Counter(DiagnosticKind.ZoomedNothing, "second", ZoomReason.NoBlock, count: 30),
                Counter(DiagnosticKind.ZoomedNothing, "third", ZoomReason.NoBlock, count: 20),
                Counter(DiagnosticKind.ZoomedNothing, "also-quiet", ZoomReason.NoBlock, count: 1),
            };

            var issue = Assert.Single(DiagnosticIssue.Summarise(counters, Now));

            Assert.StartsWith("loudest, second, third and 2 more · ", issue.Where, StringComparison.Ordinal);
        }

        [Fact]
        public void Different_causes_stay_apart()
        {
            var counters = new List<DiagnosticCounter>
            {
                Counter(DiagnosticKind.ZoomedNothing, "brave", ZoomReason.NoBlock, count: 3),
                Counter(DiagnosticKind.ZoomedNothing, "brave", ZoomReason.NoContent, count: 1),
                Counter(DiagnosticKind.NoWindow, null, null, count: 2),
            };

            Assert.Equal(3, DiagnosticIssue.Summarise(counters, Now).Count);
        }

        [Fact]
        public void Failures_come_before_presses_that_found_nothing_however_many_of_those_there_are()
        {
            var counters = new List<DiagnosticCounter>
            {
                Counter(DiagnosticKind.ZoomedNothing, "brave", ZoomReason.NoBlock, count: 500),
                Counter(DiagnosticKind.AdapterThrew, "WINWORD", ZoomReason.AutomationFailed, count: 1),
            };

            var issues = DiagnosticIssue.Summarise(counters, Now);

            Assert.True(issues[0].IsError);
            Assert.Contains("WINWORD", issues[0].Headline, StringComparison.Ordinal);
        }

        [Fact]
        public void Nothing_recorded_is_no_issues()
        {
            Assert.Empty(DiagnosticIssue.Summarise([], Now));
        }
    }

    public sealed class Wording
    {
        [Fact]
        public void One_press_in_one_application_is_not_described_as_a_number()
        {
            var issue = Assert.Single(Summarise(DiagnosticKind.ZoomedNothing, "brave", ZoomReason.NoBlock, count: 1));

            Assert.Equal("A press in brave zoomed nothing", issue.Headline);
            Assert.False(issue.IsError);
        }

        [Fact]
        public void The_reason_is_explained_rather_than_named()
        {
            var issue = Assert.Single(Summarise(DiagnosticKind.ZoomedNothing, "explorer", ZoomReason.NoAdapter, count: 1));

            Assert.DoesNotContain("NoAdapter", issue.Detail, StringComparison.Ordinal);

            // It says what to do about it, which is the point of showing it here rather than in the table.
            Assert.Contains("Applications", issue.Detail, StringComparison.Ordinal);
        }

        [Fact]
        public void A_failure_is_an_error_and_a_press_that_found_nothing_is_not()
        {
            Assert.True(Summarise(DiagnosticKind.AdapterThrew, "WINWORD", ZoomReason.AutomationFailed, 1)[0].IsError);
            Assert.True(Summarise(DiagnosticKind.Crashed, null, null, 1)[0].IsError);
            Assert.False(Summarise(DiagnosticKind.ZoomedNothing, "brave", ZoomReason.NoBlock, 1)[0].IsError);
            Assert.False(Summarise(DiagnosticKind.NoWindow, null, null, 1)[0].IsError);
        }

        [Fact]
        public void A_crash_on_the_interface_is_told_apart_from_one_in_the_app()
        {
            var ui = Assert.Single(DiagnosticIssue.Summarise(
                [new DiagnosticCounter(new DiagnosticKey(DiagnosticKind.Crashed, null, "UiThread", null), 1, Now, Now)],
                Now));

            Assert.Contains("settings window", ui.Headline, StringComparison.Ordinal);
            Assert.DoesNotContain("UiThread", ui.Detail, StringComparison.Ordinal);
        }

        [Theory]
        [InlineData(0, "just now")]
        [InlineData(5, "5 minutes ago")]
        [InlineData(60, "1 hour ago")]
        [InlineData(60 * 26, "1 day ago")]
        public void When_it_last_happened_is_said_the_way_a_person_would(int minutesAgo, string expected)
        {
            var when = Now.AddMinutes(-minutesAgo);
            var issue = Assert.Single(DiagnosticIssue.Summarise(
                [new DiagnosticCounter(new DiagnosticKey(DiagnosticKind.ZoomedNothing, "brave", null, "NoBlock"), 1, when, when)],
                Now));

            Assert.EndsWith("last seen " + expected, issue.Where, StringComparison.Ordinal);
        }

        [Fact]
        public void The_most_recent_of_a_group_is_the_one_reported()
        {
            var old = Now.AddHours(-5);
            var recent = Now.AddMinutes(-2);
            var counters = new List<DiagnosticCounter>
            {
                new(new DiagnosticKey(DiagnosticKind.ZoomedNothing, "brave", null, "NoBlock"), 1, old, old),
                new(new DiagnosticKey(DiagnosticKind.ZoomedNothing, "chrome", null, "NoBlock"), 1, recent, recent),
            };

            var issue = Assert.Single(DiagnosticIssue.Summarise(counters, Now));

            Assert.EndsWith("last seen 2 minutes ago", issue.Where, StringComparison.Ordinal);
        }

        [Fact]
        public void A_reason_this_build_does_not_know_is_shown_rather_than_dropped()
        {
            var issue = Assert.Single(DiagnosticIssue.Summarise(
                [new DiagnosticCounter(new DiagnosticKey(DiagnosticKind.ZoomedNothing, "brave", null, "SomethingNewer"), 1, Now, Now)],
                Now));

            Assert.Contains("SomethingNewer", issue.Detail, StringComparison.Ordinal);
        }

        private static IReadOnlyList<DiagnosticIssue> Summarise(DiagnosticKind kind, string? process, ZoomReason? reason, int count) =>
            DiagnosticIssue.Summarise([Counter(kind, process, reason, count)], Now);
    }

    private static DiagnosticCounter Counter(DiagnosticKind kind, string? process, ZoomReason? reason, int count) =>
        new(new DiagnosticKey(kind, process, null, reason?.ToString()), count, Now, Now);
}
