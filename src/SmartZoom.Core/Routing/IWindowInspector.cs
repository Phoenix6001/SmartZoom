using SmartZoom.Core.Input;

namespace SmartZoom.Core.Routing;

/// <summary>Resolves screen positions to windows and their owning processes.</summary>
public interface IWindowInspector
{
    /// <summary>Resolves the window under a physical screen point.</summary>
    /// <param name="point">Position in physical pixels.</param>
    /// <returns>The target, or null if no window is at that point.</returns>
    TargetInfo? GetTargetAt(ScreenPoint point);

    /// <summary>Whether <paramref name="window"/> still exists and is owned by <paramref name="processId"/>.</summary>
    /// <remarks>Both checks are needed: handles are recycled, so a live HWND may belong to a new window in another process.</remarks>
    bool IsWindowAlive(nint window, uint processId);
}
