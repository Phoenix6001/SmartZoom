using SmartZoom.Core.Input;
using SmartZoom.Core.Routing;

namespace SmartZoom.Core.Zoom.Content;

/// <summary>Finds the content element under a screen point, with its ancestors, in a supported application.</summary>
public interface IContentHitTester
{
    /// <summary>Hit-tests the target's content.</summary>
    /// <param name="target">Window under the cursor.</param>
    /// <param name="point">Cursor position in physical pixels.</param>
    /// <param name="cancellationToken">Cancels a slow hit-test (e.g. while a page's accessibility tree is still being built).</param>
    /// <returns>The hit, or null if the target is not supported or exposes no content at that point.</returns>
    Task<ContentHit?> HitTestAsync(TargetInfo target, ScreenPoint point, CancellationToken cancellationToken);
}
