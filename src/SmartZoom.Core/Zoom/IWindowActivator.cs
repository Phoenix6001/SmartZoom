namespace SmartZoom.Core.Zoom;

/// <summary>Brings a window to the foreground so keyboard input reaches it.</summary>
public interface IWindowActivator
{
    /// <summary>The top-level window currently in the foreground, or 0 when there is none.</summary>
    nint ForegroundWindow { get; }

    /// <summary>Gives the window keyboard focus.</summary>
    /// <param name="rootWindow">The top-level window.</param>
    /// <returns>True when the window is in the foreground afterwards.</returns>
    bool TryActivate(nint rootWindow);
}
