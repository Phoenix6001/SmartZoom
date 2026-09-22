using SmartZoom.Core.Input;
using SmartZoom.Core.Routing;

namespace SmartZoom.Core.Zoom;

/// <summary>A strategy for zooming one family of applications.</summary>
/// <remarks>
/// Adapters are stateless with respect to windows: whatever they need to undo a zoom is returned in
/// <see cref="ZoomInResult.RestoreState"/> and handed back by <see cref="ZoomCoordinator"/> on the
/// next trigger for the same window. Adapters that keep their own toggle state return
/// <see cref="ZoomInResult.Handled"/> and are simply invoked again to undo.
///
/// Implement <see cref="ZoomAdapter{TRestore}"/> rather than this interface directly: it names the type of
/// the undo data, which this interface cannot.
/// </remarks>
public interface IZoomAdapter
{
    /// <summary>
    /// Who this adapter is: the id the settings file routes to, the applications it claims by default,
    /// and how to describe it to a user. Registering the adapter is what makes the id exist.
    /// </summary>
    AdapterDescriptor Descriptor { get; }

    /// <summary>
    /// The type this adapter hands out as its restore state. The coordinator stores restore state as
    /// <see cref="object"/>, so this is how it can tell — without catching an exception — that an entry
    /// left behind by an earlier build of this adapter is no longer one this build can undo.
    /// </summary>
    Type RestoreType { get; }

    /// <summary>Zooms the target in, centered on the cursor.</summary>
    /// <param name="target">Window under the cursor.</param>
    /// <param name="point">Cursor position in physical pixels.</param>
    /// <param name="cancellationToken">Cancels a zoom in progress; adapters must still leave the system in a sane state (no stuck modifier keys).</param>
    Task<ZoomInResult> ZoomInAsync(TargetInfo target, ScreenPoint point, CancellationToken cancellationToken);

    /// <summary>Undoes a zoom previously applied by this adapter.</summary>
    /// <param name="target">Window that was zoomed. It is still alive when this is called.</param>
    /// <param name="restoreState">The state this adapter returned from <see cref="ZoomInAsync"/>.</param>
    /// <param name="cancellationToken">Cancels the restore.</param>
    Task ZoomOutAsync(TargetInfo target, object restoreState, CancellationToken cancellationToken);
}
