using System.Diagnostics;
using System.Globalization;
using System.Windows.Input;

using Microsoft.Extensions.Logging;

using SmartZoom.App.Ui.Mvvm;
using SmartZoom.App.Ui.Shell;
using SmartZoom.Core.Diagnostics;

namespace SmartZoom.App.Ui.Pages;

/// <summary>What the About page shows: which build this is, where to find the project, and what it records.</summary>
/// <remarks>
/// <para>
/// Nothing here touches the network, and nothing here may. SmartZoom makes no connections at all and
/// SECURITY.md states that as a promise, so there is no update check: the Releases link hands a URL to the
/// shell and the user's browser does the rest.
/// </para>
/// <para>
/// The version and the operating system come from <see cref="IMachineFacts"/>, the same source the
/// diagnostic report uses, so the two can never disagree about which build a user is running.
/// </para>
/// </remarks>
internal sealed partial class AboutViewModel : ObservableObject
{
    /// <summary>Where the source lives.</summary>
    private const string ProjectUrl = "https://github.com/Phoenix6001/SmartZoom";

    /// <summary>The issue form, rather than the issue list: a report is what this link is for.</summary>
    private const string IssuesUrl = "https://github.com/Phoenix6001/SmartZoom/issues/new/choose";

    /// <summary>Where a newer build would be, if the user goes looking for one.</summary>
    private const string ReleasesUrl = "https://github.com/Phoenix6001/SmartZoom/releases";

    private readonly AppPaths _paths;
    private readonly ILogger<AboutViewModel> _logger;

    /// <summary>Creates the page's view model.</summary>
    /// <param name="facts">Which build this is and what it is running on.</param>
    /// <param name="paths">For naming where the diagnostics record is kept.</param>
    /// <param name="logger">Logger.</param>
    public AboutViewModel(IMachineFacts facts, AppPaths paths, ILogger<AboutViewModel> logger)
    {
        ArgumentNullException.ThrowIfNull(facts);

        _paths = paths;
        _logger = logger;

        Version = string.Create(CultureInfo.CurrentCulture, $"Version {facts.AppVersion}");
        OperatingSystem = facts.OperatingSystem;

        OpenProject = new RelayCommand(() => Open(ProjectUrl));
        ReportProblem = new RelayCommand(() => Open(IssuesUrl));
        OpenReleases = new RelayCommand(() => Open(ReleasesUrl));
        GoToAdvanced = new RelayCommand(() => NavigationRequested?.Invoke(this, NavigationSection.Advanced));
    }

    /// <summary>Raised when one of the page's links asks for another page.</summary>
    public event EventHandler<NavigationSection>? NavigationRequested;

    /// <summary>The one-line description under the name.</summary>
    public static string Tagline => "Bringing macOS's smart zoom to Windows.";

    /// <summary>The licence SmartZoom itself is under.</summary>
    public static string Licence => "MIT licence";

    /// <summary>
    /// The libraries that ship in the build, with the licence each one declares. Read from the packages'
    /// own metadata; <c>Microsoft.Windows.CsWin32</c> is deliberately absent, because it is a source
    /// generator with <c>PrivateAssets=all</c> and nothing of it reaches the output.
    /// </summary>
    public static IReadOnlyList<Dependency> Dependencies { get; } =
    [
        new("Serilog", "Apache-2.0"),
        new("Serilog.Extensions.Hosting", "Apache-2.0"),
        new("Serilog.Sinks.File", "Apache-2.0"),
        new("Microsoft.Extensions.Hosting", "MIT"),
        new("Interop.UIAutomationClient", "MIT"),
    ];

    /// <summary>Which build this is, e.g. "Version 0.2.0".</summary>
    public string Version { get; }

    /// <summary>What it is running on, e.g. "Microsoft Windows 10.0.26200".</summary>
    public string OperatingSystem { get; }

    /// <summary>Where the local record of failures is kept.</summary>
    public string DiagnosticsFile => _paths.DiagnosticsFile;

    /// <summary>Opens the project page in the default browser.</summary>
    public ICommand OpenProject { get; }

    /// <summary>Opens the issue form in the default browser.</summary>
    public ICommand ReportProblem { get; }

    /// <summary>Opens the releases page in the default browser.</summary>
    public ICommand OpenReleases { get; }

    /// <summary>Shows the Advanced page, where the full report and the diagnostics switch live.</summary>
    public ICommand GoToAdvanced { get; }

    /// <summary>
    /// Hands a URL to the shell, which opens it in whatever the user browses with. No request is made from
    /// this process.
    /// </summary>
    private void Open(string url)
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            LogOpenFailed(ex, url);
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Failed to open {Url}.")]
    private partial void LogOpenFailed(Exception exception, string url);
}
