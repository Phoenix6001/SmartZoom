namespace SmartZoom.App.Ui.Pages;

/// <summary>One of the libraries SmartZoom ships, and the licence it is under.</summary>
/// <param name="Name">The package name, as it appears in <c>Directory.Packages.props</c>.</param>
/// <param name="Licence">
/// The SPDX expression the package declares, read from its own <c>.nuspec</c> rather than from memory.
/// </param>
internal sealed record Dependency(string Name, string Licence);
