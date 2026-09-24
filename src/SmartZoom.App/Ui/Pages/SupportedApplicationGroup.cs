using System.Windows.Media;

using SmartZoom.App.Ui.Mvvm;
using SmartZoom.App.Ui.Shell;

namespace SmartZoom.App.Ui.Pages;

/// <summary>
/// One tile under "Supported applications": a family of applications one strategy handles.
/// </summary>
/// <remarks>
/// The icon and the list of names arrive later than the tile does. Finding them means reading the registry
/// and opening executables, which must not happen on the UI thread, so the tile is created with its fallback
/// glyph and fills itself in when the lookup comes back - hence a view model rather than a record.
/// </remarks>
/// <param name="glyph">The Segoe Fluent Icons code point shown until an application's own icon is found.</param>
/// <param name="title">The family's name, taken from the adapter that handles it.</param>
/// <param name="target">The page the tile navigates to.</param>
internal sealed class SupportedApplicationGroup(string glyph, string title, NavigationSection target) : ObservableObject
{
    private ImageSource? _icon;
    private string _applications = "Looking…";

    /// <summary>The Segoe Fluent Icons code point, shown whenever <see cref="Icon"/> is null.</summary>
    public string Glyph { get; } = glyph;

    /// <summary>The family's name.</summary>
    public string Title { get; } = title;

    /// <summary>The page the tile navigates to.</summary>
    public NavigationSection Target { get; } = target;

    /// <summary>The icon of the first of these applications that is installed, or null while none was found.</summary>
    public ImageSource? Icon
    {
        get => _icon;
        set
        {
            if (Set(ref _icon, value))
                Raise(nameof(HasIcon));
        }
    }

    /// <summary>Whether a real icon was found, which is what hides the fallback glyph.</summary>
    public bool HasIcon => _icon is not null;

    /// <summary>The applications in this family, named the way they name themselves.</summary>
    public string Applications
    {
        get => _applications;
        set => Set(ref _applications, value);
    }
}
