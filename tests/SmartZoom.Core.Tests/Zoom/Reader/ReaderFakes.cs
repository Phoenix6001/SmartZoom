using SmartZoom.Core.Input;
using SmartZoom.Core.Routing;
using SmartZoom.Core.Zoom;
using SmartZoom.Core.Zoom.Content;

namespace SmartZoom.Core.Tests.Zoom.Reader;

/// <summary>The reader window every test in this folder points at.</summary>
internal static class Reader
{
    public static TargetInfo Acrobat { get; } = new(0x300, 0x301, 13, "Acrobat", "AcrobatSDIWindow", "AVL_AVView");

    /// <summary>The cursor, 500 px below the top of the default content area.</summary>
    public static ScreenPoint Cursor { get; } = new(800, 600);
}

internal sealed class FakeReaderView : IReaderView
{
    private static readonly int[] Profile = [1, 2, 3];

    /// <summary>The content area; the cursor at (800, 600) sits 500 px below its top.</summary>
    public PixelRect? ContentBounds { get; set; } = PixelRect.FromSize(0, 100, 2000, 2000);

    /// <summary>How far the view really moves, when that differs from the request.</summary>
    public int? Moves { get; set; }

    public List<int> Requests { get; } = [];

    public List<string> Order { get; } = [];

    public int Snapshots { get; private set; }

    public int Alignments { get; private set; }

    /// <summary>Whether the view can be read well enough to put it back.</summary>
    public bool Aligns { get; set; } = true;

    public PixelRect? Bounds(TargetInfo target) => ContentBounds;

    public int ScrollBy(TargetInfo target, int pixels, CancellationToken cancellationToken)
    {
        Requests.Add(pixels);
        Order.Add("scroll");
        return Moves is { } moved ? Math.Sign(pixels) * Math.Abs(moved) : pixels;
    }

    public ReaderViewMark? Snapshot(TargetInfo target)
    {
        Snapshots++;
        return new ReaderViewMark(ContentBounds ?? default, Profile);
    }

    public bool ScrollBackTo(TargetInfo target, ReaderViewMark snapshot, CancellationToken cancellationToken)
    {
        Alignments++;
        return Aligns;
    }
}

internal sealed class FakePinchInjector : IPinchInjector
{
    public bool Succeeds { get; set; } = true;

    public List<(ScreenPoint Anchor, double Factor)> Gestures { get; } = [];

    public Task<bool> PinchAsync(ScreenPoint anchor, double factor, TimeSpan duration, PixelRect bounds, CancellationToken cancellationToken)
    {
        if (Succeeds)
            Gestures.Add((anchor, factor));

        return Task.FromResult(Succeeds);
    }
}

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
