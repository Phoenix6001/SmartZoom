using SmartZoom.Core.Routing;
using SmartZoom.Core.Zoom;
using SmartZoom.Core.Zoom.Content;

namespace SmartZoom.Core.Tests.Zoom.Reader;

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
