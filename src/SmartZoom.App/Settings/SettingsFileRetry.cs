using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace SmartZoom.App.Settings;

/// <summary>
/// Puts the settings file into force once it can be read, after a startup that had to fall back to defaults
/// because another process was holding it. Does nothing, and ends at once, after a normal startup.
/// </summary>
internal sealed partial class SettingsFileRetry(
    SettingsStore store,
    SettingsApplier applier,
    ILogger<SettingsFileRetry> logger,
    TimeSpan? interval = null) : BackgroundService
{
    // A lock that outlasted the startup wait is a slow sync or a long scan; checking every few seconds puts the
    // user's settings back soon after it ends and costs nothing meanwhile. Only a test passes the interval.
    private readonly TimeSpan _interval = interval ?? TimeSpan.FromSeconds(5);

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!store.FileUnreadable)
            return;

        using var timer = new PeriodicTimer(_interval);
        try
        {
            // The applier logs what each outcome means; this only decides whether to keep trying.
            while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false))
            {
                if (await applier.RetryUnreadableFileAsync().ConfigureAwait(false))
                    return;
            }
        }
        catch (OperationCanceledException)
        {
            // Shutting down.
        }
        catch (Exception ex)
        {
            // A background service that throws stops the host. Running on the defaults, with the file left as
            // it is, is a better place to stop than quitting the app.
            LogRetryFailed(ex);
        }
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Reading the settings file again failed; the defaults stay in force for this session and the file is left as it is.")]
    private partial void LogRetryFailed(Exception exception);
}
