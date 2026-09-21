using Microsoft.Extensions.Hosting;

using SmartZoom.Core.Input;

namespace SmartZoom.App.Hosting;

/// <summary>Ties the trigger source's capture lifetime to the host's lifetime.</summary>
internal sealed class TriggerCaptureService(ITriggerSource triggerSource) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken)
    {
        triggerSource.StartCapture();
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        triggerSource.StopCapture();
        return Task.CompletedTask;
    }
}
