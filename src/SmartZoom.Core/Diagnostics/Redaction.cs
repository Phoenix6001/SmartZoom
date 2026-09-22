using System.Globalization;

namespace SmartZoom.Core.Diagnostics;

/// <summary>
/// Removes the parts of a string that identify the person running SmartZoom.
/// </summary>
/// <remarks>
/// Applied to everything rendered into a report, exception messages included: an <see cref="IOException"/>
/// carries the path that failed, and that path usually contains a username and often a document name.
/// The longest folder is replaced first so that the profile root does not shadow the folders beneath it.
/// </remarks>
public static class Redaction
{
    /// <summary>Replaces well-known user folders with the variables that name them.</summary>
    /// <param name="text">The text to clean.</param>
    /// <param name="home">The user profile folder.</param>
    /// <param name="localAppData">The local application data folder.</param>
    /// <param name="appData">The roaming application data folder.</param>
    /// <returns>The text with those folders replaced.</returns>
    public static string Paths(string text, string home, string localAppData, string appData)
    {
        ArgumentNullException.ThrowIfNull(text);

        foreach (var (folder, name) in Longest(home, localAppData, appData))
        {
            if (!string.IsNullOrEmpty(folder))
                text = text.Replace(folder, name, StringComparison.OrdinalIgnoreCase);
        }

        return text;
    }

    /// <summary>Cuts text to a maximum length, saying so where it was cut.</summary>
    /// <param name="text">The text.</param>
    /// <param name="max">The most characters to keep.</param>
    /// <returns>The text, cut if it was longer.</returns>
    public static string Truncate(string text, int max)
    {
        ArgumentNullException.ThrowIfNull(text);

        return text.Length <= max
            ? text
            : string.Create(CultureInfo.InvariantCulture, $"{text[..max]}… [truncated, {text.Length} characters]");
    }

    private static IEnumerable<(string Folder, string Name)> Longest(string home, string local, string roaming) =>
        new[] { (local, "%LOCALAPPDATA%"), (roaming, "%APPDATA%"), (home, "%USERPROFILE%") }
            .OrderByDescending(p => p.Item1?.Length ?? 0);
}
