using SmartZoom.Core.Input;
using SmartZoom.Core.Routing;
using SmartZoom.Core.Settings;

namespace SmartZoom.Core.Zoom;

/// <summary>
/// Generic zoom via a burst of synthesized Ctrl+wheel ticks. Works with any application that binds
/// Ctrl+wheel to zoom (PDF viewers, Excel, image viewers), centered wherever the app centers it —
/// usually the cursor.
/// </summary>
/// <remarks>
/// Toggle-back sends the same number of ticks in the opposite direction. That is only approximate:
/// if the app clamps at its maximum zoom, some of the "in" ticks did nothing and the "out" burst
/// overshoots. Applications with an exact zoom API get a dedicated adapter instead.
/// </remarks>
public sealed class CtrlWheelAdapter(IInputInjector injector, CtrlWheelSettings settings, TimeProvider timeProvider) : IZoomAdapter
{
    /// <inheritdoc />
    public AdapterKind Kind => AdapterKind.CtrlWheel;

    /// <inheritdoc />
    public async Task<ZoomInResult> ZoomInAsync(TargetInfo target, ScreenPoint point, CancellationToken cancellationToken)
    {
        var ticks = await SendBurstAsync(settings.Ticks, cancellationToken).ConfigureAwait(false);
        return ticks == 0 ? ZoomInResult.Unhandled : ZoomInResult.Applied(new RestoreState(ticks));
    }

    /// <inheritdoc />
    public async Task ZoomOutAsync(TargetInfo target, object restoreState, CancellationToken cancellationToken)
    {
        if (restoreState is not RestoreState state)
        {
            throw new ArgumentException($"Expected {nameof(RestoreState)} from a previous zoom-in.", nameof(restoreState));
        }

        await SendBurstAsync(-state.Ticks, cancellationToken).ConfigureAwait(false);
    }

    /// <returns>The number of ticks that were actually delivered, signed like <paramref name="ticks"/>.</returns>
    private async Task<int> SendBurstAsync(int ticks, CancellationToken cancellationToken)
    {
        // If the user is physically holding Ctrl we must not release it afterwards, or their next wheel
        // notch would scroll instead of zoom. In that case the modifier is simply already in place.
        var holdControl = !injector.IsModifierDown(ModifierKey.Control);
        if (holdControl && !injector.TrySendModifier(ModifierKey.Control, isDown: true))
        {
            return 0;
        }

        var direction = Math.Sign(ticks);
        var delivered = 0;
        try
        {
            for (var i = 0; i < Math.Abs(ticks); i++)
            {
                if (i > 0 && settings.IntervalMs > 0)
                {
                    // Spread the ticks out: many apps coalesce wheel messages that arrive in the same frame.
                    await Task.Delay(TimeSpan.FromMilliseconds(settings.IntervalMs), timeProvider, cancellationToken).ConfigureAwait(false);
                }

                if (!injector.TrySendWheel(direction))
                {
                    break;
                }

                delivered++;
            }
        }
        finally
        {
            // Unconditional: a stuck Ctrl key is the worst failure mode this adapter has.
            if (holdControl)
            {
                injector.TrySendModifier(ModifierKey.Control, isDown: false);
            }
        }

        return delivered * direction;
    }

    /// <summary>How far a window was zoomed in, so the same distance can be undone.</summary>
    /// <param name="Ticks">Wheel ticks delivered; positive.</param>
    internal sealed record RestoreState(int Ticks);
}
