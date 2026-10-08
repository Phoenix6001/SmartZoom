using Microsoft.Extensions.Logging.Abstractions;

using SmartZoom.App.Settings;
using SmartZoom.App.Tests.Diagnostics;
using SmartZoom.Core.Settings;

namespace SmartZoom.App.Tests.Settings;

/// <summary>
/// A settings file another process is holding: an editor saving it, an antivirus scan, a backup or OneDrive
/// sync. Reading it threw an IOException that ended startup ("SmartZoom failed to start"), seen live with the
/// file held open.
/// </summary>
public sealed class LockedSettingsFileTests
{
    private const string UsersFile = """{ "Zoom": { "MaxScale": 2.5 } }""";

    [Fact]
    public async Task A_lock_that_is_let_go_in_a_moment_is_waited_out()
    {
        using var temp = new TempDirectory();
        var paths = DiagnosticFixtures.Paths(temp.Path);
        File.WriteAllText(paths.SettingsFile, UsersFile);
        var store = new SettingsStore(paths, NullLogger<SettingsStore>.Instance, attempts: 40, retryDelay: TimeSpan.FromMilliseconds(25));

        SmartZoomSettings loaded;
        using (var held = new FileStream(paths.SettingsFile, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            var load = Task.Run(store.Load);
            await Task.Delay(150);
            held.Close();
            loaded = await load.WaitAsync(TimeSpan.FromSeconds(10));
        }

        Assert.Equal(2.5, loaded.Zoom.MaxScale);
    }

    [Fact]
    public void A_lock_that_outlasts_the_wait_starts_with_defaults_and_leaves_the_file_alone()
    {
        using var temp = new TempDirectory();
        var paths = DiagnosticFixtures.Paths(temp.Path);
        File.WriteAllText(paths.SettingsFile, UsersFile);
        var store = new SettingsStore(paths, NullLogger<SettingsStore>.Instance, attempts: 3, retryDelay: TimeSpan.FromMilliseconds(10));

        SmartZoomSettings loaded;
        using (new FileStream(paths.SettingsFile, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            loaded = store.Load();

        Assert.Equal(new SmartZoomSettings().Zoom.MaxScale, loaded.Zoom.MaxScale);
        Assert.Equal(UsersFile, File.ReadAllText(paths.SettingsFile));
    }

    [Fact]
    public void A_new_file_is_not_written_over_one_that_appeared_meanwhile()
    {
        using var temp = new TempDirectory();
        var paths = DiagnosticFixtures.Paths(temp.Path);
        File.WriteAllText(paths.SettingsFile, UsersFile);
        var store = new SettingsStore(paths, NullLogger<SettingsStore>.Instance);

        Assert.False(store.TryCreate(new SmartZoomSettings()));
        Assert.Equal(UsersFile, File.ReadAllText(paths.SettingsFile));
        Assert.False(File.Exists(paths.SettingsFile + ".tmp"));
    }

    [Fact]
    public void Falling_back_says_so_until_the_file_has_been_loaded()
    {
        using var temp = new TempDirectory();
        var paths = DiagnosticFixtures.Paths(temp.Path);
        File.WriteAllText(paths.SettingsFile, UsersFile);
        var store = new SettingsStore(paths, NullLogger<SettingsStore>.Instance, attempts: 2, retryDelay: TimeSpan.FromMilliseconds(10));

        using (new FileStream(paths.SettingsFile, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            store.Load();
        Assert.True(store.FileUnreadable);

        // Only reading it does not end the fallback; putting it into force (a load, or a reload) does.
        Assert.True(store.TryLoad(out _, out _));
        Assert.True(store.FileUnreadable);
        store.Load();
        Assert.False(store.FileUnreadable);
    }
}
