using SmartZoom.App.Ui.Applications;

namespace SmartZoom.App.Tests.Ui;

/// <summary>
/// What the Applications page offers to route. Deliberately makes no claim about *which* applications are
/// running: that is the machine's business, and a build agent may have none.
/// </summary>
public sealed class RunningApplicationsTests
{
    [Fact]
    public void Listing_what_is_running_never_throws()
    {
        // A process can exit between being listed and being asked about itself, and one at a higher integrity
        // level refuses to name its executable. Neither may reach the settings window.
        _ = RunningApplications.List();
    }

    [Fact]
    public void Every_entry_can_be_routed_and_shown()
    {
        foreach (var application in RunningApplications.List())
        {
            Assert.False(string.IsNullOrWhiteSpace(application.ImageName), "An entry with no image name cannot be routed.");
            Assert.False(string.IsNullOrWhiteSpace(application.DisplayName), "An entry with no name cannot be shown.");
        }
    }

    [Fact]
    public void An_application_appears_once_however_many_processes_it_runs()
    {
        // A browser is half a dozen processes and one thing to route.
        var running = RunningApplications.List();

        Assert.Equal(
            running.Count,
            running.Select(a => a.ImageName).Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }
}
