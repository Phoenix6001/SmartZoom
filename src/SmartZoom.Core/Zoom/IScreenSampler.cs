using SmartZoom.Core.Zoom.Content;

namespace SmartZoom.Core.Zoom;

/// <summary>Reads what a region of the screen looks like, for telling whether a gesture changed anything.</summary>
public interface IScreenSampler
{
    /// <summary>Takes a sample of the region as it is now.</summary>
    /// <param name="region">The screen rectangle to read, in physical pixels.</param>
    /// <returns>The sample, or null when the screen cannot be read (a secure desktop, a display change, an empty region).</returns>
    ScreenSample? Sample(PixelRect region);
}
