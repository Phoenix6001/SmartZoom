using Microsoft.Extensions.Hosting;

namespace SmartZoom.App.Diagnostics;

/// <summary>Writes the diagnostics record periodically, and once on the way out.</summary>
internal sealed class DiagnosticFlushService(DiagnosticRecorder recorder, TimeProvider time) : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(30);

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(Interval, time);

        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false))
                recorder.Flush();
        }
        catch (OperationCanceledException)
        {
            // Shutting down.
        }
        finally
        {
            recorder.Flush();
        }
    }
}
