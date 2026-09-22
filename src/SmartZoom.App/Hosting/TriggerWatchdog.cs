using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace SmartZoom.App.Hosting;

/// <summary>
/// Says in the log, periodically, that SmartZoom is listening and nothing has arrived.
/// </summary>
/// <remarks>
/// <para>
/// A trigger that never reaches SmartZoom leaves no trace at all: the hook sees nothing, so nothing is
/// logged, and the log of a healthy idle process is indistinguishable from the log of one whose button is
/// being eaten before it gets here. That cost a long diagnosis once — vendor mouse software had an
/// application-specific profile that reverted the button to a hardware function in one browser, so it
/// emitted no input whatsoever while every other application worked.
/// </para>
/// <para>
/// One line every quarter of an hour is enough to tell those two apart, and quiet enough not to bury the
/// lines that matter. It is only written when nothing arrived in that interval.
/// </para>
/// </remarks>
internal sealed partial class TriggerWatchdog(
    ZoomActivity activity,
    TimeProvider time,
    ILogger<TriggerWatchdog> logger) : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(15);

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(Interval, time);

        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false))
            {
                var last = activity.LastTrigger;
                if (last is null)
                {
                    LogNothingEver(Interval.TotalMinutes);
                    continue;
                }

                var idle = time.GetUtcNow() - last.Value;
                if (idle >= Interval)
                    LogNothingLately((int)idle.TotalMinutes);
            }
        }
        catch (OperationCanceledException)
        {
            // Shutting down.
        }
    }

    [LoggerMessage(Level = LogLevel.Information,
        Message = "Listening, but no trigger has ever reached SmartZoom ({Minutes:F0} minutes so far). " +
            "If pressing your trigger does nothing, the press is not getting here: vendor mouse software " +
            "(Logi Options+, Razer Synapse, ...) often remaps side buttons per application.")]
    private partial void LogNothingEver(double minutes);

    [LoggerMessage(Level = LogLevel.Information,
        Message = "Listening; no trigger in the last {Minutes} minutes. If you are pressing one, it is not " +
            "reaching SmartZoom — check whether your mouse software remaps that button for the application " +
            "you are pointing at.")]
    private partial void LogNothingLately(int minutes);
}
