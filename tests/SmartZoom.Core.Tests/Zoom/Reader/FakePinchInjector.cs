using SmartZoom.Core.Input;
using SmartZoom.Core.Zoom;
using SmartZoom.Core.Zoom.Content;

namespace SmartZoom.Core.Tests.Zoom.Reader;

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
