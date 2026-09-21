using SmartZoom.Core.Routing;

namespace SmartZoom.Core.Settings;

/// <summary>Which application is zoomed by which strategy.</summary>
public sealed class RoutingSettings
{
    /// <summary>
    /// Process image name to the id of the strategy that handles it, e.g. <c>"notepad": "CtrlWheel"</c>.
    /// Names may include ".exe" and are matched case-insensitively.
    /// </summary>
    /// <remarks>
    /// Every adapter already claims the applications it was written for, so this map is only for changing
    /// its mind: naming an application it does not know about, moving one to a different strategy, or
    /// setting <c>"None"</c> to switch SmartZoom off there. Entries here win over the defaults, and because
    /// the defaults live in the code, upgrading SmartZoom brings new applications with it instead of
    /// leaving them out of an old settings file.
    /// </remarks>
    public IDictionary<string, AdapterId> Apps { get; set; } = new Dictionary<string, AdapterId>(StringComparer.OrdinalIgnoreCase);
}
