namespace SmartZoom.App.Ui.Pages;

/// <summary>The applications one strategy claims out of the box, shown so nobody adds a route it already has.</summary>
/// <param name="DisplayName">The strategy's name.</param>
/// <param name="Description">What it does, in the adapter's own sentence.</param>
/// <param name="Processes">Its default process names, as one line.</param>
internal sealed record BuiltInGroup(string DisplayName, string Description, string Processes);
