using SmartZoom.Core.Routing;
using SmartZoom.Core.Zoom;

namespace SmartZoom.App.Hosting;

/// <summary>One generation of the zooming half of the app, built from one set of settings.</summary>
/// <param name="Coordinator">Turns a trigger into a zoom.</param>
/// <param name="Router">Which strategy handles which application; also the list of strategies, for the UI.</param>
/// <param name="RestoreTypes">
/// What each adapter hands out as restore state. Comparing this between two generations is how the applier
/// works out whose remembered zooms have stopped meaning anything — the reader's two modes share an id but
/// not a restore type, and a stored one would reach an adapter that cannot use it.
/// </param>
internal sealed record ZoomPipeline(
    ZoomCoordinator Coordinator,
    ZoomRouter Router,
    IReadOnlyDictionary<AdapterId, Type> RestoreTypes)
{
    /// <summary>The ids whose restore state this generation can no longer make sense of.</summary>
    /// <param name="previous">The generation being replaced.</param>
    public IEnumerable<AdapterId> ObsoletedBy(ZoomPipeline previous)
    {
        ArgumentNullException.ThrowIfNull(previous);

        foreach (var (id, type) in previous.RestoreTypes)
        {
            if (!RestoreTypes.TryGetValue(id, out var now) || now != type)
                yield return id;
        }
    }
}
