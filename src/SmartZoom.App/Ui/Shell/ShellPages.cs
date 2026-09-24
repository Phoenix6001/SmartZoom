using SmartZoom.App.Ui.Pages;

namespace SmartZoom.App.Ui.Shell;

/// <summary>Builds the rail's rows and the pages behind them.</summary>
/// <remarks>
/// The one place a view is constructed. Keeping it out of <see cref="ShellViewModel"/> is what lets that
/// class stay a view model, and keeping it in one method is what makes adding a page in the second wave a
/// single edit.
/// </remarks>
internal static class ShellPages
{
    /// <summary>Creates the five sections, in the order the rail shows them.</summary>
    /// <param name="overview">The Overview page's view model.</param>
    /// <param name="about">The About page's view model.</param>
    /// <returns>The sections; the first is the one the window opens on.</returns>
    public static IReadOnlyList<NavigationItem> Build(OverviewViewModel overview, AboutViewModel about) =>
    [
        new(NavigationSection.Overview, "", "Overview", new OverviewPage { DataContext = overview }),
        new(NavigationSection.Triggers, "", "Triggers", new TriggersPage()),
        new(NavigationSection.Applications, "", "Applications", new ApplicationsPage()),
        new(NavigationSection.Advanced, "", "Advanced", new AdvancedPage()),
        new(NavigationSection.About, "", "About", new AboutPage { DataContext = about }),
    ];
}
