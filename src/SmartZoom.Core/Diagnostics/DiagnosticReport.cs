using System.Globalization;
using System.Text;

namespace SmartZoom.Core.Diagnostics;

/// <summary>Cleans one piece of text before it is rendered.</summary>
/// <param name="text">The text.</param>
/// <returns>The text with identifying parts removed.</returns>
public delegate string Redactor(string text);

/// <summary>
/// Renders a record as markdown meant to be read by the person who produced it, then pasted into an issue.
/// </summary>
/// <remarks>
/// Human-readable rather than encoded, because showing the report is the whole consent mechanism: consent
/// to something unreadable is not consent. A section that throws degrades to "unavailable" rather than
/// costing the reader the rest of the report.
/// </remarks>
public static class DiagnosticReport
{
    /// <summary>Builds the report.</summary>
    /// <param name="record">What went wrong.</param>
    /// <param name="facts">What the machine is.</param>
    /// <param name="settingsJson">The settings file's contents.</param>
    /// <param name="logTail">Recent log lines, or null when the user did not ask for them.</param>
    /// <param name="redact">Applied to every piece of free text.</param>
    /// <returns>Markdown.</returns>
    public static string Render(
        DiagnosticRecord record,
        IMachineFacts facts,
        string settingsJson,
        string? logTail,
        Redactor redact)
    {
        ArgumentNullException.ThrowIfNull(record);
        ArgumentNullException.ThrowIfNull(facts);
        ArgumentNullException.ThrowIfNull(redact);

        var text = new StringBuilder();
        text.AppendLine("## SmartZoom diagnostic report").AppendLine();

        Section(text, "Version", () =>
            text.AppendLine(CultureInfo.InvariantCulture, $"- SmartZoom {facts.AppVersion}")
                .AppendLine(CultureInfo.InvariantCulture, $"- {facts.OperatingSystem}"));

        Section(text, "Displays", () =>
        {
            foreach (var d in facts.Displays)
            {
                var role = d.Primary ? " (primary)" : string.Empty;
                text.AppendLine(CultureInfo.InvariantCulture,
                    $"- {d.Width}x{d.Height} at {d.RefreshHz} Hz, {d.Scale * 100:0}%{role}");
            }
        });

        Section(text, "Settings", () =>
            text.AppendLine("```json").AppendLine(redact(settingsJson)).AppendLine("```"));

        Section(text, "What didn't work", () =>
        {
            if (record.Counters.Count == 0)
            {
                text.AppendLine("Nothing recorded.");
                return;
            }

            text.AppendLine("| Count | Application | Strategy | What happened | Last seen |");
            text.AppendLine("|---|---|---|---|---|");
            foreach (var c in record.Counters)
            {
                text.AppendLine(CultureInfo.InvariantCulture,
                    $"| {c.Count} | {c.Key.Process ?? "?"} | {c.Key.Adapter ?? "-"} | {c.Key.Kind}/{c.Key.Reason ?? "-"} | {c.LastSeen:u} |");
            }

            if (record.OmittedKeys > 0)
            {
                text.AppendLine(CultureInfo.InvariantCulture,
                    $"{Environment.NewLine}{record.OmittedKeys} further distinct events were not counted (cap reached).");
            }
        });

        Section(text, "Recent details", () =>
        {
            foreach (var s in record.Samples)
            {
                text.AppendLine(CultureInfo.InvariantCulture, $"- **{s.Key.Process ?? "?"}** {s.Key.Kind}/{s.Key.Reason ?? "-"} at {s.When:u}");
                if (s.Detail is { } detail)
                    text.AppendLine(CultureInfo.InvariantCulture, $"  - path: `{redact(detail)}`");
                if (s.Exception is { } exception)
                    text.AppendLine("  - ```").AppendLine(redact(exception)).AppendLine("    ```");
            }
        });

        if (logTail is { Length: > 0 })
            Section(text, "Recent log", () => text.AppendLine("```").AppendLine(redact(logTail)).AppendLine("```"));

        return text.ToString();
    }

    private static void Section(StringBuilder text, string title, Action body)
    {
        text.AppendLine(CultureInfo.InvariantCulture, $"### {title}").AppendLine();
        try
        {
            body();
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            text.AppendLine(CultureInfo.InvariantCulture, $"unavailable ({ex.GetType().Name})");
        }

        text.AppendLine();
    }
}
