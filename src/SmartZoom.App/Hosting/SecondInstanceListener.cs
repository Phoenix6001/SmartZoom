using Microsoft.Extensions.Hosting;

using SmartZoom.App.Tray;

namespace SmartZoom.App.Hosting;

/// <summary>
/// Listens for a second copy of SmartZoom being started, and lets it ask this one to do something instead of
/// simply refusing to run.
/// </summary>
/// <remarks>
/// <para>
/// Running a tray app that is already running is not a mistake, it is somebody looking for its window — there
/// is no other obvious way to ask for one. So the second process signals and exits, and this one obliges.
/// </para>
/// <para>
/// The same channel carries <c>--quit</c>, which is how the installer asks a running copy to close before it
/// replaces the executable. Killing the process works too, but skips taking the tray icon down, and a ghost
/// icon that lingers until you hover over it is a poor first impression of an upgrade.
/// </para>
/// </remarks>
internal sealed class SecondInstanceListener(TrayApplicationContext tray, IHostApplicationLifetime lifetime) : IHostedService, IDisposable
{
    /// <summary>Scoped to the logon session, like the single-instance mutex, so other users are unaffected.</summary>
    private const string Prefix = @"Local\SmartZoom.App-9C7B1E52-3F0A-4C1F-8B7D-2E6A5D4C3B21-";

    private const string SettingsEventName = Prefix + "settings";
    private const string QuitEventName = Prefix + "quit";

    private readonly EventWaitHandle _settingsRequested = new(false, EventResetMode.AutoReset, SettingsEventName);
    private readonly EventWaitHandle _quitRequested = new(false, EventResetMode.AutoReset, QuitEventName);
    private readonly CancellationTokenSource _stopping = new();
    private Thread? _listener;

    /// <summary>Asks a running instance to show its settings window.</summary>
    /// <returns>False when no instance is listening, which means this process is the first one.</returns>
    public static bool TryRequestSettings() => TrySignal(SettingsEventName);

    /// <summary>Asks a running instance to shut down cleanly.</summary>
    /// <returns>False when nothing was listening; there was nothing to close.</returns>
    public static bool TryRequestQuit() => TrySignal(QuitEventName);

    /// <inheritdoc />
    public Task StartAsync(CancellationToken cancellationToken)
    {
        _listener = new Thread(Listen) { IsBackground = true, Name = "SmartZoom second-instance listener" };
        _listener.Start();
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task StopAsync(CancellationToken cancellationToken)
    {
        _stopping.Cancel();
        _settingsRequested.Set();       // wake the thread so it notices
        _listener?.Join(TimeSpan.FromSeconds(1));
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _stopping.Dispose();
        _settingsRequested.Dispose();
        _quitRequested.Dispose();
    }

    private static bool TrySignal(string name)
    {
        if (!EventWaitHandle.TryOpenExisting(name, out var handle))
            return false;

        using (handle)
        {
            return handle.Set();
        }
    }

    private void Listen()
    {
        WaitHandle[] handles = [_settingsRequested, _quitRequested];

        while (!_stopping.IsCancellationRequested)
        {
            var signalled = WaitHandle.WaitAny(handles);
            if (_stopping.IsCancellationRequested)
                return;

            if (signalled == 1)
            {
                // Shuts the host down, which takes the tray icon with it through ApplicationStopping.
                lifetime.StopApplication();
                return;
            }

            tray.RequestSettings();
        }
    }
}
