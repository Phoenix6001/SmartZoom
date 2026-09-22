using Microsoft.Extensions.Logging;

using SmartZoom.Core.Input;
using SmartZoom.Core.Routing;
using SmartZoom.Core.Zoom;

namespace SmartZoom.App.Hosting;

/// <summary>
/// Holds the pipeline in force and lets it be replaced while the app runs. Triggers and replacements take the
/// same gate, so a zoom and a settings change can never interleave.
/// </summary>
/// <remarks>
/// A zoom is not cancelled when settings change: it finishes on the pipeline it started with, which is
/// consistent, and cancelling it would be worse than waiting — a reader zoom that is abandoned mid-way leaves
/// the reader in the foreground, on top of everything the user was looking at.
/// </remarks>
internal sealed partial class ZoomEngine(ZoomPipeline initial, WindowZoomStateStore state, ILogger<ZoomEngine> logger) : IDisposable
{
    /// <summary>
    /// How long a replacement waits for a zoom in flight. A gesture is about 330 ms and a reader shortcut can
    /// wait up to 1.5 s for the user to let go of a hotkey's modifiers, so three seconds is a zoom that is not
    /// coming back.
    /// </summary>
    private static readonly TimeSpan ReplaceTimeout = TimeSpan.FromSeconds(3);

    private readonly SemaphoreSlim _gate = new(1, 1);
    private ZoomPipeline _current = initial;

    /// <summary>The pipeline in force. Its router is also where the UI gets the list of strategies.</summary>
    public ZoomPipeline Current => Volatile.Read(ref _current);

    /// <summary>Handles one trigger on whichever pipeline is current, holding off a replacement until it is done.</summary>
    /// <param name="target">Window under the cursor.</param>
    /// <param name="point">Cursor position in physical pixels.</param>
    /// <param name="cancellationToken">Cancels the zoom.</param>
    public async Task<ZoomOutcome> HandleTriggerAsync(TargetInfo target, ScreenPoint point, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return await Current.Coordinator.HandleTriggerAsync(target, point, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// Puts a new pipeline in force, and forgets the remembered zooms the outgoing one owned that the new one
    /// could not undo.
    /// </summary>
    /// <param name="pipeline">The replacement.</param>
    /// <returns>True if the swap happened under the gate; false if a zoom held it too long and it was forced.</returns>
    public async Task<bool> ReplaceAsync(ZoomPipeline pipeline)
    {
        ArgumentNullException.ThrowIfNull(pipeline);

        // Taking the gate matters for the invalidation below, not the swap: a zoom finishing afterwards would
        // otherwise save its restore state back in after the entries had been cleared.
        var held = await _gate.WaitAsync(ReplaceTimeout).ConfigureAwait(false);
        try
        {
            var previous = Current;
            Volatile.Write(ref _current, pipeline);

            if (!held)
            {
                // A zoom is still running. Swapping is safe — it finishes on its own pipeline — but clearing
                // entries underneath it is not, so the stale ones are left. ZoomCoordinator refuses a restore
                // state it cannot use, so the cost is one extra zoom-in, not an exception.
                LogReplacedWhileBusy();
                return false;
            }

            foreach (var obsolete in pipeline.ObsoletedBy(previous))
            {
                var forgotten = state.RemoveAll(obsolete);
                if (forgotten > 0)
                    LogForgot(forgotten, obsolete);
            }

            return true;
        }
        finally
        {
            if (held)
                _gate.Release();
        }
    }

    /// <inheritdoc />
    public void Dispose() => _gate.Dispose();

    [LoggerMessage(Level = LogLevel.Warning, Message = "Settings were applied while a zoom was still running; windows zoomed by a strategy that has changed will zoom in again rather than come back.")]
    private partial void LogReplacedWhileBusy();

    [LoggerMessage(Level = LogLevel.Information, Message = "{Count} window(s) zoomed by \"{Adapter}\" were forgotten: the new settings changed what that strategy is, so those zooms can no longer be undone from here.")]
    private partial void LogForgot(int count, AdapterId adapter);
}
