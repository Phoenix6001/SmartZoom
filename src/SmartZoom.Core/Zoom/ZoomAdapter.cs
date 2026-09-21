using SmartZoom.Core.Input;
using SmartZoom.Core.Routing;

namespace SmartZoom.Core.Zoom;

/// <summary>
/// Base class for adapters, which names the type of the undo data. The coordinator stores that data as
/// <see cref="object"/> because one table holds several adapters' states; this puts the type back on both
/// ends, so handing back the wrong thing is a compile error rather than a surprise on the toggle press.
/// </summary>
/// <typeparam name="TRestore">What this adapter needs in order to undo a zoom.</typeparam>
/// <param name="descriptor">Who this adapter is; conventionally the adapter's own static <c>Descriptor</c>.</param>
public abstract class ZoomAdapter<TRestore>(AdapterDescriptor descriptor) : IZoomAdapter
    where TRestore : notnull
{
    AdapterDescriptor IZoomAdapter.Descriptor => descriptor;

    Task<ZoomInResult> IZoomAdapter.ZoomInAsync(TargetInfo target, ScreenPoint point, CancellationToken cancellationToken) =>
        ZoomInAsync(target, point, cancellationToken);

    Task IZoomAdapter.ZoomOutAsync(TargetInfo target, object restoreState, CancellationToken cancellationToken)
    {
        if (restoreState is not TRestore typed)
        {
            throw new ArgumentException(
                $"{GetType().Name} expected {typeof(TRestore).Name} from a previous zoom-in, not {restoreState?.GetType().Name ?? "null"}.",
                nameof(restoreState));
        }

        return ZoomOutAsync(target, typed, cancellationToken);
    }

    /// <summary>Zooms the target in, centered on the cursor.</summary>
    /// <param name="target">Window under the cursor.</param>
    /// <param name="point">Cursor position in physical pixels.</param>
    /// <param name="cancellationToken">Cancels a zoom in progress; the system must still be left sane (no stuck modifier keys).</param>
    /// <returns><see cref="Applied"/>, <see cref="ZoomInResult.Handled"/> or <see cref="ZoomInResult.Unhandled"/>.</returns>
    protected abstract Task<ZoomInResult> ZoomInAsync(TargetInfo target, ScreenPoint point, CancellationToken cancellationToken);

    /// <summary>Undoes a zoom this adapter applied.</summary>
    /// <param name="target">Window that was zoomed. It is still alive when this is called.</param>
    /// <param name="restoreState">Exactly what was handed to <see cref="Applied"/>.</param>
    /// <param name="cancellationToken">Cancels the restore.</param>
    protected abstract Task ZoomOutAsync(TargetInfo target, TRestore restoreState, CancellationToken cancellationToken);

    /// <summary>The zoom was applied; <paramref name="restoreState"/> comes back on the next press.</summary>
    /// <param name="restoreState">What this adapter needs to undo the zoom.</param>
    protected static ZoomInResult Applied(TRestore restoreState) => ZoomInResult.Applied(restoreState);
}
