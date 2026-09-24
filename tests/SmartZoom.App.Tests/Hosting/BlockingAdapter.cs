using SmartZoom.Core.Input;
using SmartZoom.Core.Routing;
using SmartZoom.Core.Zoom;

namespace SmartZoom.App.Tests.Hosting;

/// <summary>An adapter whose zoom-in waits until the test lets it finish, so a test can hold a zoom in flight.</summary>
internal sealed class BlockingAdapter() : ZoomAdapter<string>(Describe)
{
    /// <summary>The process this adapter claims; a target named this way reaches it.</summary>
    public const string Process = "slow";

    private static readonly AdapterDescriptor Describe = new("Slow", [Process], "Slow", "Waits until released.");

    private readonly TaskCompletionSource _entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource _release = new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>Completes once a zoom-in is inside the adapter and waiting.</summary>
    public Task Entered => _entered.Task;

    /// <summary>Lets the waiting zoom-in finish.</summary>
    public void Release() => _release.TrySetResult();

    protected override async Task<ZoomInResult> ZoomInAsync(TargetInfo target, ScreenPoint point, CancellationToken cancellationToken)
    {
        _entered.TrySetResult();
        await _release.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
        return Applied("zoomed");
    }

    protected override Task ZoomOutAsync(TargetInfo target, string restoreState, CancellationToken cancellationToken) =>
        Task.CompletedTask;
}
