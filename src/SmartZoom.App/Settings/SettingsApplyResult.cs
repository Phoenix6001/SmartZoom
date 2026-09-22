using SmartZoom.Core.Settings;

namespace SmartZoom.App.Settings;

/// <summary>What came of trying to apply a set of settings.</summary>
/// <param name="Outcome">Whether they are in force, and whether they were written down.</param>
/// <param name="Problems">Everything found while checking them; warnings survive a successful apply.</param>
/// <param name="Detail">Why the file could not be written, when that is what happened.</param>
internal sealed record SettingsApplyResult(SettingsApplyOutcome Outcome, IReadOnlyList<SettingsProblem> Problems, string? Detail = null)
{
    public static SettingsApplyResult Applied(IReadOnlyList<SettingsProblem> problems) =>
        new(SettingsApplyOutcome.Applied, problems);

    public static SettingsApplyResult AppliedButNotSaved(IReadOnlyList<SettingsProblem> problems, string detail) =>
        new(SettingsApplyOutcome.AppliedButNotSaved, problems, detail);

    public static SettingsApplyResult Rejected(IReadOnlyList<SettingsProblem> problems) =>
        new(SettingsApplyOutcome.Rejected, problems);

    /// <summary>Whether the app is now running these settings.</summary>
    public bool InForce => Outcome != SettingsApplyOutcome.Rejected;
}
