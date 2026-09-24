using SmartZoom.Core.Zoom;

namespace SmartZoom.Core.Tests.Zoom.Reader;

internal sealed class FakeActivator : IWindowActivator
{
    public bool Succeeds { get; set; } = true;

    /// <summary>Whether bringing back a window other than the target succeeds.</summary>
    public bool RestoreSucceeds { get; set; } = true;

    public List<long> Activated { get; } = [];

    public nint ForegroundWindow { get; set; }

    /// <summary>Another window takes the foreground the moment the target has been activated.</summary>
    public nint StealForegroundAfterActivating { get; set; }

    public bool TryActivate(nint rootWindow)
    {
        Activated.Add(rootWindow);
        var ok = rootWindow == Reader.Acrobat.RootWindow ? Succeeds : RestoreSucceeds;
        if (ok)
            ForegroundWindow = StealForegroundAfterActivating == 0 ? rootWindow : StealForegroundAfterActivating;

        return ok;
    }
}
