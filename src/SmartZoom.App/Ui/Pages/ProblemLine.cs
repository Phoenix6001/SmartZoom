using SmartZoom.Core.Settings;

namespace SmartZoom.App.Ui.Pages;

/// <summary>One thing the settings check found, ready to be shown next to what the user was editing.</summary>
/// <param name="Text">The section and the message, as the check words them.</param>
/// <param name="IsError">
/// Whether this stopped the change. Errors are shown in the error colour and warnings in the warn colour: a
/// change that went through with a caveat must not look like one that was refused.
/// </param>
internal sealed record ProblemLine(string Text, bool IsError)
{
    /// <summary>The lines for a set of problems, in the order the check reported them.</summary>
    /// <param name="problems">What the check found; may be empty.</param>
    public static IReadOnlyList<ProblemLine> From(IReadOnlyList<SettingsProblem> problems) =>
        [.. problems.Select(p => new ProblemLine(p.ToString(), p.Severity == SettingsProblemSeverity.Error))];

    /// <summary>A single message of our own, in the same shape as the check's.</summary>
    /// <param name="text">What to say.</param>
    public static IReadOnlyList<ProblemLine> Error(string text) => [new ProblemLine(text, IsError: true)];
}
