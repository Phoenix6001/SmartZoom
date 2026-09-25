using System.Diagnostics;

namespace SmartZoom.App.Ui.Applications;

/// <summary>
/// The applications running with a window of their own, so an application can be routed by picking it rather
/// than by knowing what its executable is called.
/// </summary>
/// <remarks>
/// <para>
/// Typing a process image name is the one thing on the settings window that asks the user to know something
/// only the developer does — whether it is "notepad", "Notepad" or "Notepad.exe". Almost every application
/// somebody wants to route is on their screen while they are looking for it, so the list is what is running.
/// </para>
/// <para>
/// Nothing in here throws. A process can exit between being listed and being asked about itself, and one
/// running at a higher integrity level refuses to name its executable at all; both mean "no icon for this
/// one", never a settings page that will not open. Everything it does touches the process table and the disk,
/// so it belongs on a background thread.
/// </para>
/// </remarks>
internal static class RunningApplications
{
    /// <summary>One entry per application with a visible window, by its own name, sorted for a list.</summary>
    /// <returns>What was found; never null, and never an exception.</returns>
    public static IReadOnlyList<InstalledApplication> List()
    {
        // Keyed by image name: a browser with six processes is one application to route.
        var found = new Dictionary<string, InstalledApplication>(StringComparer.OrdinalIgnoreCase);
        var self = Environment.ProcessId;

        foreach (var process in Process.GetProcesses())
        {
            try
            {
                if (process.Id == self || process.MainWindowHandle == 0 || process.MainWindowTitle.Length == 0)
                    continue;

                var image = process.ProcessName;
                if (found.ContainsKey(image))
                    continue;

                found[image] = Describe(process, image);
            }
            catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception or NotSupportedException)
            {
                // It exited while being read, or it will not be read by us. Either way there is nothing to show.
            }
            finally
            {
                process.Dispose();
            }
        }

        return [.. found.Values.OrderBy(a => a.DisplayName, StringComparer.CurrentCultureIgnoreCase)];
    }

    /// <summary>
    /// The running process's own executable where it will say, which is the icon and name people recognise;
    /// otherwise whatever the registry knows about that image name, which is what the built-in list uses.
    /// </summary>
    private static InstalledApplication Describe(Process process, string image)
    {
        string? executable;
        try
        {
            executable = process.MainModule?.FileName;
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException or NotSupportedException)
        {
            // A process at a higher integrity level than ours does not have to tell us where it came from.
            executable = null;
        }

        return executable is null
            ? InstalledApplications.Describe(image)
            : new InstalledApplication(
                image,
                InstalledApplications.TryReadName(executable) ?? image,
                InstalledApplications.TryLoadIcon(executable));
    }
}
