using Microsoft.Extensions.Hosting;

using SmartZoom.App.Tray;

namespace SmartZoom.App.Hosting;

/// <summary>
/// Listens for someone starting SmartZoom while it is already running, and treats that as "show me the
/// settings" rather than as an error.
/// </summary>
/// <remarks>
/// Running a tray app that is already running is not a mistake, it is somebody looking for its window —
/// there is no other obvious way to ask for one. The second process signals this event and exits; this one
/// opens the settings window.
/// </remarks>
internal sealed class SecondInstanceListener(TrayApplicationContext tray) : IHostedService, IDisposable
{
    /// <summary>Scoped to the logon session, like the single-instance mutex, so other users are unaffected.</summary>
    internal const string EventName = @"Local\SmartZoom.App-9C7B1E52-3F0A-4C1F-8B7D-2E6A5D4C3B21-settings";

    private readonly EventWaitHandle _requested = new(false, EventResetMode.AutoReset, EventName);
    private readonly CancellationTokenSource _stopping = new();
    private Thread? _listener;

    /// <summary>Signals a running instance to show its settings window.</summary>
    /// <returns>False when no instance is listening, which means this process is the first one.</returns>
    public static bool TryRequestSettings()
    {
        if (!EventWaitHandle.TryOpenExisting(EventName, out var handle))
            return false;

        using (handle)
        {
            return handle.Set();
        }
    }

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
        _requested.Set();       // wake the thread so it notices
        _listener?.Join(TimeSpan.FromSeconds(1));
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _stopping.Dispose();
        _requested.Dispose();
    }

    private void Listen()
    {
        while (!_stopping.IsCancellationRequested)
        {
            _requested.WaitOne();
            if (!_stopping.IsCancellationRequested)
                tray.RequestSettings();
        }
    }
}
