using System.Diagnostics;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

using Microsoft.Win32;

namespace SmartZoom.App.Ui.Applications;

/// <summary>
/// Finds the applications an adapter handles on this machine, so the settings window can show their real
/// icons and names rather than a list of process image names.
/// </summary>
/// <remarks>
/// <para>
/// Paths come from the <c>App Paths</c> key, which is where an installer registers "run this by name". It is
/// asked in the order Windows itself does — the user's hive first, then the machine's, then the 32-bit view
/// of the machine's — so a per-user install of a browser is found as readily as a per-machine one, and no
/// install location is written down here.
/// </para>
/// <para>
/// Nothing in here throws: a machine is free to have a stale App Paths entry, a file it may not read, or an
/// executable with no icon, and none of those is worth failing a settings page over. Everything it does
/// touches the disk and the registry, so it belongs on a background thread; the images it produces are
/// frozen, which is what makes them safe to hand to the UI thread afterwards.
/// </para>
/// </remarks>
internal static class InstalledApplications
{
    private const string AppPaths = @"SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths";
    private const string AppPathsWow = @"SOFTWARE\Wow6432Node\Microsoft\Windows\CurrentVersion\App Paths";

    /// <summary>Looks one application up by the process image name the router uses.</summary>
    /// <param name="imageName">Image name, with or without ".exe".</param>
    /// <returns>What was found; never null, and never an exception.</returns>
    public static InstalledApplication Describe(string imageName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(imageName);

        var executable = FindExecutable(imageName);
        return executable is null
            ? new InstalledApplication(imageName, imageName, Icon: null)
            : new InstalledApplication(imageName, NameOf(executable) ?? imageName, TryLoadIcon(executable));
    }

    /// <summary>The full path an image name runs from, or null when nothing on this machine registers it.</summary>
    /// <param name="imageName">Image name, with or without ".exe".</param>
    public static string? FindExecutable(string imageName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(imageName);

        var file = imageName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? imageName : imageName + ".exe";

        return Registered(Registry.CurrentUser, AppPaths, file)
            ?? Registered(Registry.LocalMachine, AppPaths, file)
            ?? Registered(Registry.LocalMachine, AppPathsWow, file);
    }

    /// <summary>Reads an executable's own icon as something WPF can draw, or null when it has none.</summary>
    /// <param name="executable">Full path to the executable.</param>
    /// <returns>A frozen image, safe to use from any thread.</returns>
    public static ImageSource? TryLoadIcon(string executable)
    {
        try
        {
            using var icon = System.Drawing.Icon.ExtractAssociatedIcon(executable);
            if (icon is null)
                return null;

            var image = Imaging.CreateBitmapSourceFromHIcon(icon.Handle, System.Windows.Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
            image.Freeze();
            return image;
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception or NotSupportedException)
        {
            return null;
        }
    }

    private static string? Registered(RegistryKey hive, string path, string file)
    {
        try
        {
            using var key = hive.OpenSubKey($@"{path}\{file}");

            // The default value is the full path; installers write it quoted about as often as not.
            if (key?.GetValue(null) is not string raw)
                return null;

            var trimmed = raw.Trim().Trim('"');
            return trimmed.Length > 0 && File.Exists(trimmed) ? trimmed : null;
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException or ArgumentException)
        {
            return null;
        }
    }

    /// <summary>What the executable calls itself, e.g. "Google Chrome" for chrome.exe.</summary>
    private static string? NameOf(string executable)
    {
        try
        {
            var description = FileVersionInfo.GetVersionInfo(executable).FileDescription?.Trim();
            return string.IsNullOrEmpty(description) ? null : description;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception)
        {
            return null;
        }
    }
}
