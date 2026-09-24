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
    /// <remarks>
    /// Replaces all three separator forms of each folder path: backslash (<c>C:\Users\ada</c>),
    /// forward slash (<c>C:/Users/ada</c>), and doubled backslash (<c>C:\\Users\\ada</c>, as in JSON).
    /// The longest variant is replaced first to prevent shorter folders from shadowing longer ones.
    /// </remarks>
    public static string Paths(string text, string home, string localAppData, string appData)
    {
        ArgumentNullException.ThrowIfNull(text);

        foreach (var (folder, name) in VariantsLongestFirst(home, localAppData, appData))
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

    /// <summary>Every separator form of every folder, longest first, so a profile root never shadows a folder beneath it.</summary>
    private static IEnumerable<(string Folder, string Name)> VariantsLongestFirst(string home, string local, string roaming)
    {
        var variants = new List<(string path, string varName)>();

        foreach (var (folder, varName) in new[] { (local, "%LOCALAPPDATA%"), (roaming, "%APPDATA%"), (home, "%USERPROFILE%") })
        {
            if (!string.IsNullOrEmpty(folder))
            {
                variants.Add((folder, varName));                       // C:\Users\ada
                variants.Add((folder.Replace('\\', '/'), varName));    // C:/Users/ada
                variants.Add((folder.Replace(@"\", @"\\"), varName));  // C:\\Users\\ada, as in JSON
            }
        }

        return variants.OrderByDescending(p => p.path.Length);
    }
}
