using System.Drawing;

using SmartZoom.App.Tray;

namespace SmartZoom.App.Tests.Tray;

/// <summary>
/// The notification area's one job is to say whether SmartZoom is listening, so the icon has to differ between
/// the two states and still be the same shape.
/// </summary>
public sealed class TrayIconTests
{
    [Fact]
    public void The_icon_resource_is_there()
    {
        // Everything below is meaningless if the embedded icon went missing and the system one stood in, and a
        // shipped build with no icon of its own is worth failing over on its own account.
        Assert.NotSame(SystemIcons.Application, TrayIcon.Load());
    }

    [Fact]
    public void Switched_off_is_a_different_icon_from_switched_on()
    {
        Assert.NotSame(TrayIcon.Load(enabled: true), TrayIcon.Load(enabled: false));
        Assert.Same(TrayIcon.Load(), TrayIcon.Load(enabled: true));
    }

    [Fact]
    public void Switched_off_has_no_colour_left_in_it()
    {
        using var off = TrayIcon.Load(enabled: false).ToBitmap();

        for (var y = 0; y < off.Height; y++)
        {
            for (var x = 0; x < off.Width; x++)
            {
                var pixel = off.GetPixel(x, y);
                if (pixel.A == 0)
                    continue;

                Assert.True(
                    pixel.R == pixel.G && pixel.G == pixel.B,
                    $"({x}, {y}) is {pixel.R},{pixel.G},{pixel.B}, which is not a grey.");
            }
        }
    }

    [Fact]
    public void Switched_on_still_has_its_colour()
    {
        using var on = TrayIcon.Load(enabled: true).ToBitmap();

        var coloured = 0;
        for (var y = 0; y < on.Height; y++)
        {
            for (var x = 0; x < on.Width; x++)
            {
                var pixel = on.GetPixel(x, y);
                if (pixel.A != 0 && (pixel.R != pixel.G || pixel.G != pixel.B))
                    coloured++;
            }
        }

        // The tile is blue, so most of the icon is not grey. Guards against greying the wrong one.
        Assert.True(coloured > 0, "The enabled icon has no coloured pixel in it.");
    }
}
