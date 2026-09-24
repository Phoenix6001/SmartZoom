using System.ComponentModel;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

using SmartZoom.Core.Zoom;
using SmartZoom.Core.Zoom.Content;

namespace SmartZoom.Interop.Input;

/// <summary>Reads a region of the screen into a <see cref="ScreenSample"/>, the way <see cref="ReaderView"/> reads its strip.</summary>
public sealed class ScreenSampler : IScreenSampler
{
    /// <inheritdoc />
    public ScreenSample? Sample(PixelRect region)
    {
        if (region.IsEmpty)
            return null;

        try
        {
            using var bitmap = new Bitmap(region.Width, region.Height, PixelFormat.Format32bppArgb);
            using (var graphics = Graphics.FromImage(bitmap))
                graphics.CopyFromScreen(region.Left, region.Top, 0, 0, new Size(region.Width, region.Height), CopyPixelOperation.SourceCopy);

            return ScreenSample.FromLuma(region, Luma(bitmap));
        }
        catch (InvalidOperationException)
        {
            return null;    // the window moved or the display changed mid-capture
        }
        catch (ArgumentException)
        {
            return null;
        }
        catch (Win32Exception)
        {
            return null;    // the screen could not be read (a secure desktop, a display mode change)
        }
        catch (ExternalException)
        {
            return null;    // GDI+ itself failed, which it reports as a status rather than a Win32 error
        }
    }

    /// <summary>One value per pixel, row-major: the mean of the three channels, like the rest of the pixel code.</summary>
    private static unsafe byte[] Luma(Bitmap bitmap)
    {
        var luma = new byte[bitmap.Width * bitmap.Height];
        var data = bitmap.LockBits(new Rectangle(0, 0, bitmap.Width, bitmap.Height), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        try
        {
            for (var y = 0; y < bitmap.Height; y++)
            {
                var line = (byte*)data.Scan0 + ((long)y * data.Stride);
                var row = y * bitmap.Width;
                for (var x = 0; x < bitmap.Width; x++)
                {
                    var pixel = line + (x * 4);
                    luma[row + x] = (byte)((pixel[0] + pixel[1] + pixel[2]) / 3);     // blue + green + red
                }
            }
        }
        finally
        {
            bitmap.UnlockBits(data);
        }

        return luma;
    }
}
