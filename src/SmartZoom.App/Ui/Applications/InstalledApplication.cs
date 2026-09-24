using System.Windows.Media;

namespace SmartZoom.App.Ui.Applications;

/// <summary>What the machine knows about one of the applications an adapter handles.</summary>
/// <param name="ImageName">The process image name the router matches on, without ".exe".</param>
/// <param name="DisplayName">
/// The name the executable gives itself, e.g. "Google Chrome"; the image name when it is not installed.
/// </param>
/// <param name="Icon">Its icon, or null when it is not installed or the icon could not be read.</param>
internal sealed record InstalledApplication(string ImageName, string DisplayName, ImageSource? Icon);
