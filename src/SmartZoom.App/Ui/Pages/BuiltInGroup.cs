using SmartZoom.App.Ui.Applications;

namespace SmartZoom.App.Ui.Pages;

/// <summary>The applications one strategy claims out of the box, shown so nobody adds a route it already has.</summary>
/// <param name="DisplayName">The strategy's name.</param>
/// <param name="Description">What it does, in the adapter's own sentence.</param>
/// <param name="Applications">
/// Its default applications. Named and pictured as they name and picture themselves where this machine has
/// them installed, and by their process image name where it does not.
/// </param>
internal sealed record BuiltInGroup(string DisplayName, string Description, IReadOnlyList<InstalledApplication> Applications);
