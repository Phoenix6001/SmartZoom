using SmartZoom.App.Ui.Shell;

namespace SmartZoom.App.Ui.Pages;

/// <summary>One of the four cards under "Quick settings": a value worth seeing, and the page that changes it.</summary>
/// <param name="Glyph">A Segoe Fluent Icons code point, as a one-character string.</param>
/// <param name="Title">What the value is, e.g. "Largest zoom".</param>
/// <param name="Value">The value itself, written the way somebody would say it out loud.</param>
/// <param name="Description">One sentence saying what it does.</param>
/// <param name="Target">The page the card's chevron navigates to.</param>
internal sealed record QuickSetting(string Glyph, string Title, string Value, string Description, NavigationSection Target);
