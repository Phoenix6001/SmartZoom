using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace SmartZoom.App.Tray;

/// <summary>The application's own icon, shared by the notification area and the settings window.</summary>
internal static class TrayIcon
{
    private const string Resource = "SmartZoom.App.Resources.SmartZoom.ico";

    // Luma weights. The blue tile becomes a mid grey and the white glyph stays white, so the shape is still
    // recognisable at 16 px while the colour that means "listening" is gone. Derived from the one icon rather
    // than shipped as a second file, so the two can never drift apart.
    private static readonly ColorMatrix Greyscale = new(
    [
        [0.299f, 0.299f, 0.299f, 0, 0],
        [0.587f, 0.587f, 0.587f, 0, 0],
        [0.114f, 0.114f, 0.114f, 0, 0],
        [0, 0, 0, 1, 0],
        [0, 0, 0, 0, 1],
    ]);

    // One instance of each for the life of the process: neither NotifyIcon nor Form disposes an icon handed to
    // it, the fallback is a system icon that must never be disposed at all, and the grey one owns a handle from
    // GetHicon that is deliberately never destroyed because it stays in use until the process exits.
    private static readonly Lazy<Icon> Colour = new(Create);
    private static readonly Lazy<Icon> Grey = new(CreateGrey);

    /// <summary>The icon at the size the notification area uses at this scaling, or the system default if the resource is missing.</summary>
    public static Icon Load() => Colour.Value;

    /// <summary>The icon for a state: grey while zooming is switched off.</summary>
    /// <param name="enabled">Whether triggers are being acted on.</param>
    /// <remarks>
    /// Whether SmartZoom is listening is the one thing its place in the notification area exists to say, and
    /// until now the icon said it only in a tooltip nobody hovers.
    /// </remarks>
    public static Icon Load(bool enabled) => enabled ? Colour.Value : Grey.Value;

    private static Icon Create()
    {
        using var stream = typeof(TrayIcon).Assembly.GetManifestResourceStream(Resource);
        return stream is null ? SystemIcons.Application : new Icon(stream, SystemInformation.SmallIconSize);
    }

    private static Icon CreateGrey()
    {
        var colour = Colour.Value;

        // Nothing of ours to recolour, and a system icon must not be taken apart.
        if (ReferenceEquals(colour, SystemIcons.Application))
            return colour;

        try
        {
            var size = SystemInformation.SmallIconSize;
            using var bitmap = new Bitmap(size.Width, size.Height, PixelFormat.Format32bppArgb);

            using (var graphics = Graphics.FromImage(bitmap))
            using (var attributes = new ImageAttributes())
            using (var source = colour.ToBitmap())
            {
                attributes.SetColorMatrix(Greyscale);
                graphics.DrawImage(
                    source,
                    new Rectangle(Point.Empty, size),
                    0,
                    0,
                    source.Width,
                    source.Height,
                    GraphicsUnit.Pixel,
                    attributes);
            }

            return Icon.FromHandle(bitmap.GetHicon());
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or ExternalException)
        {
            // Greying is a nicety; having an icon at all is not. GDI+ reports its failures as a status rather
            // than a Win32 error, which is why ExternalException is in the list.
            return colour;
        }
    }
}
