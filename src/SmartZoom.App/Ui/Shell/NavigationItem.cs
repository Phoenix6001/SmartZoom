namespace SmartZoom.App.Ui.Shell;

/// <summary>One row of the navigation rail, and the page it shows.</summary>
/// <param name="Id">Which section this is; what the rest of the window navigates by.</param>
/// <param name="Glyph">A Segoe Fluent Icons code point, as a one-character string.</param>
/// <param name="Label">The name shown beside the glyph.</param>
/// <param name="Content">
/// What the content area shows while this row is selected. A view, because the pages of this window are
/// plain <c>UserControl</c>s with a view model in their <c>DataContext</c>; the shell only has to place them.
/// </param>
internal sealed record NavigationItem(NavigationSection Id, string Glyph, string Label, object Content);
