using System.Globalization;

using SmartZoom.Core.Diagnostics;
using SmartZoom.Core.Zoom;

namespace SmartZoom.App.Ui.Pages;

/// <summary>One thing that has gone wrong, said in a sentence instead of in a table row.</summary>
/// <param name="IsError">Whether this is a failure rather than a press that found nothing to do.</param>
/// <param name="Headline">What happened and how often, e.g. "17 presses in 6 applications zoomed nothing".</param>
/// <param name="Detail">Why, in the words the rest of the settings window uses.</param>
/// <param name="Where">Which applications, and when it last happened.</param>
/// <param name="Application">
/// The busiest application of the group, for filling in a bug report, or null when none was identified.
/// </param>
/// <remarks>
/// The report below says the same things as <c>ZoomedNothing/NoBlock</c>, which is the right shape to paste
/// into a bug report and the wrong one to meet when the window has just told you there are issues.
/// </remarks>
internal sealed record DiagnosticIssue(bool IsError, string Headline, string Detail, string Where, string? Application)
{
    /// <summary>How many applications are named before the rest become "and N more".</summary>
    private const int NamedApplications = 3;

    /// <summary>
    /// Groups the record's counters into the few things actually worth reading.
    /// </summary>
    /// <param name="counters">Every counter in the record.</param>
    /// <param name="now">What to measure "last seen" against.</param>
    /// <returns>Failures first, then whatever happened most.</returns>
    /// <remarks>
    /// Grouped by everything in a <see cref="DiagnosticKey"/> except the application. Kind, strategy and reason
    /// all come from enums or the registered adapters, so the number of groups is bounded by the build and is a
    /// handful in practice; the application is the one part that grows with the machine. Ten applications that
    /// all found nothing to magnify are one finding and one line, not ten lines of the same sentence — and that
    /// grouping is also the answer to the question "which of these is worth doing something about".
    /// </remarks>
    public static IReadOnlyList<DiagnosticIssue> Summarise(IReadOnlyList<DiagnosticCounter> counters, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(counters);

        return
        [
            .. counters
                .GroupBy(c => (c.Key.Kind, c.Key.Adapter, c.Key.Reason))
                .Select(group => Describe(group.Key.Kind, group.Key.Adapter, group.Key.Reason, [.. group], now))
                .OrderByDescending(described => described.Issue.IsError)
                .ThenByDescending(described => described.Total)
                .Select(described => described.Issue),
        ];
    }

    private static (DiagnosticIssue Issue, int Total) Describe(
        DiagnosticKind kind,
        string? adapter,
        string? reason,
        IReadOnlyList<DiagnosticCounter> group,
        DateTimeOffset now)
    {
        var total = group.Sum(c => c.Count);
        var lastSeen = group.Max(c => c.LastSeen);

        // Busiest first, because if only three are named those are the three worth naming.
        var applications = group
            .Where(c => c.Key.Process is { Length: > 0 })
            .GroupBy(c => c.Key.Process!, StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(a => a.Sum(c => c.Count))
            .Select(a => a.Key)
            .ToList();

        var issue = new DiagnosticIssue(
            kind is DiagnosticKind.Crashed or DiagnosticKind.AdapterThrew,
            Sentence(kind, adapter, total, applications),
            Why(kind, adapter, reason),
            Name(applications) + "last seen " + Ago(lastSeen, now),
            applications.FirstOrDefault());

        return (issue, total);
    }

    /// <summary>What happened, how often, and in how many places.</summary>
    private static string Sentence(DiagnosticKind kind, string? adapter, int total, List<string> applications)
    {
        if (kind == DiagnosticKind.Crashed)
        {
            return adapter == "UiThread"
                ? "The settings window hit an error it survived"
                : "SmartZoom hit an error it survived";
        }

        // One application is named; several are counted, because naming ten of them is the wall this avoids.
        var where = applications.Count switch
        {
            0 => string.Empty,
            1 => $" in {applications[0]}",
            _ => $" in {applications.Count} applications",
        };

        if (kind == DiagnosticKind.AdapterThrew)
            return total == 1 ? $"Zooming{where} failed" : $"Zooming{where} failed {total} times";

        var presses = total == 1 ? "A press" : string.Create(CultureInfo.CurrentCulture, $"{total} presses");

        return kind == DiagnosticKind.NoWindow
            ? $"{presses} found no window under the cursor"
            : $"{presses}{where} zoomed nothing";
    }

    /// <summary>The reason in the words the rest of the window uses, or the raw label if it is not one of ours.</summary>
    private static string Why(DiagnosticKind kind, string? adapter, string? reason)
    {
        var strategy = adapter is { Length: > 0 } named && named != "UiThread" ? named : null;

        if (kind == DiagnosticKind.Crashed)
            return "It was recorded here rather than being lost; the report below has the details.";

        if (kind == DiagnosticKind.AdapterThrew)
        {
            // A zoom that ran past its deadline is recorded as a failure like any other, but it is the one
            // kind with an answer the user can act on, so it does not get the generic sentence.
            if (reason == nameof(TimeoutException))
                return "The application did not answer in time and the zoom was abandoned. It was most likely showing a dialog.";

            return strategy is null ? "The strategy handling it raised an error." : $"The {strategy} strategy raised an error.";
        }

        if (!Enum.TryParse<ZoomReason>(reason, out var parsed))
            return reason is { Length: > 0 } raw ? raw : "No reason was recorded.";

        return parsed switch
        {
            ZoomReason.NoAdapter => "No strategy is set for this, so nothing happened. Add it under Applications to give it a zoom.",
            ZoomReason.NoContent => "The application exposed nothing under the cursor. Often an empty area, a canvas, or a window that does not describe itself.",
            ZoomReason.NoBlock => "Something was found under the cursor, but nothing there was a sensible thing to magnify. Often blank page area.",
            ZoomReason.AlreadyFits => "What was under the cursor already filled the width, so there was nothing to zoom to.",
            ZoomReason.GestureRefused => "The application refused the zoom gesture.",
            ZoomReason.AutomationFailed => "The application's own automation interface refused the zoom or could not be reached.",
            _ => strategy is null ? "The strategy could not act." : $"The {strategy} strategy could not act.",
        };
    }

    /// <summary>"brave, chrome, msedge and 3 more · ", or nothing at all when no application was identified.</summary>
    private static string Name(List<string> applications)
    {
        if (applications.Count == 0)
            return string.Empty;

        var named = string.Join(", ", applications.Take(NamedApplications));
        var rest = applications.Count - NamedApplications;

        return (rest > 0 ? $"{named} and {rest} more" : named) + " · ";
    }

    /// <summary>"3 minutes ago", the way the tray tooltip says it.</summary>
    private static string Ago(DateTimeOffset when, DateTimeOffset now)
    {
        var elapsed = now - when;
        if (elapsed < TimeSpan.Zero)
            elapsed = TimeSpan.Zero;

        return elapsed switch
        {
            { TotalMinutes: < 1 } => "just now",
            { TotalMinutes: < 60 } => Count((int)elapsed.TotalMinutes, "minute") + " ago",
            { TotalHours: < 24 } => Count((int)elapsed.TotalHours, "hour") + " ago",
            _ => Count((int)elapsed.TotalDays, "day") + " ago",
        };
    }

    private static string Count(int value, string unit) =>
        string.Create(CultureInfo.CurrentCulture, $"{value} {unit}{(value == 1 ? string.Empty : "s")}");
}
