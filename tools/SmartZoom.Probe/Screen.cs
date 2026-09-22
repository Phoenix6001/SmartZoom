using System.Drawing;
using System.Drawing.Imaging;
using System.Globalization;

using SmartZoom.Core.Zoom.Content;

namespace SmartZoom.Probe;

/// <summary>Screenshots and the three comparisons that turned them into measurements.</summary>
internal static class Screen
{
    /// <summary>Captures a rectangle of the screen to a PNG.</summary>
    public static void Shoot(PixelRect area, string path)
    {
        using var bitmap = Capture(area);
        bitmap.Save(path, ImageFormat.Png);
        Console.WriteLine($"saved {area.Width}x{area.Height} to {path}");
    }

    /// <summary>
    /// How much of two shots differs, and how much of that a whole-image shift accounts for. A restore that
    /// worked reads 0.0%; one that left the view a line of text out reads a few percent at a shift of a few
    /// tens of pixels.
    /// </summary>
    public static void Diff(string first, string second, PixelRect? region)
    {
        var a = Load(first, region);
        var b = Load(second, region);
        if (a.GetLength(0) != b.GetLength(0) || a.GetLength(1) != b.GetLength(1))
        {
            Console.WriteLine("the two images are different sizes");
            return;
        }

        Console.WriteLine($"differing pixels: {Differing(a, b, 0, 0):F1}%");

        var (bestDy, atDy) = BestShift(a, b, vertical: true, other: 0);
        Console.WriteLine($"best vertical shift {bestDy} px -> {atDy:F1}% differing");

        var (bestDx, atDx) = BestShift(a, b, vertical: false, other: bestDy);
        Console.WriteLine($"best horizontal shift {bestDx} px (with dy {bestDy}) -> {atDx:F1}% differing");
    }

    /// <summary>
    /// The scale and offset that best map one shot onto the other, down the given rows. This is what measured
    /// the zoom a gesture really delivered, and the creep a gesture and its inverse left behind.
    /// </summary>
    public static void Scale(string first, string second, int top, int bottom, double low, double high)
    {
        var a = RowProfile(Load(first, null), top, bottom);
        var b = RowProfile(Load(second, null), top, bottom);

        var best = (Score: double.MaxValue, Scale: 1.0, Offset: 0);
        for (var scale = low; scale <= high + 1e-9; scale += 0.002)
        {
            for (var offset = -400; offset <= 400; offset += 2)
            {
                var score = Compare(a, b, scale, offset);
                if (score < best.Score)
                    best = (score, scale, offset);
            }
        }

        Console.WriteLine(string.Create(
            CultureInfo.InvariantCulture,
            $"best scale {best.Scale:F4} at offset {best.Offset} px (score {best.Score:F2})"));
    }

    /// <summary>
    /// The trajectory of a zoom, frame by frame: where the ink sits and how far it is spread.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Written because the two obvious metrics both lie about motion. "Percentage of differing pixels"
    /// saturates on text — once a frame moves further than about a character width, nearly every pixel
    /// differs and the number stops responding to speed — and template matching loses its lock entirely once
    /// content triples in size. Both made a perfectly smooth zoom look ragged and a ragged one look smooth.
    /// </para>
    /// <para>
    /// Ink spread does neither. Magnifying content by a factor about an anchor multiplies the spread of its
    /// dark pixels about their centroid by that same factor, so the spread of each frame, divided by the
    /// spread of the first, IS the scale reached in that frame — continuous, unsaturating, and needing no
    /// feature to track. A zoom that only ever grows prints a rising column; a wobble prints as a fall
    /// between two rises, which the "step" column shows directly.
    /// </para>
    /// </remarks>
    /// <param name="paths">The frames, in the order they were captured.</param>
    public static void Track(IReadOnlyList<string> paths)
    {
        double? first = null;
        double? previous = null;

        Console.WriteLine("frame  centroid(x,y)      spread   scale   step");

        for (var i = 0; i < paths.Count; i++)
        {
            var pixels = Load(paths[i], null);
            var (cx, cy, spread) = Ink(pixels);
            first ??= spread;

            // A frame with no ink at all (a blank strip) would divide by zero and mean nothing anyway.
            if (first is not > 0)
            {
                Console.WriteLine($"{i,5}  no ink to track in {Path.GetFileName(paths[i])}");
                return;
            }

            var scale = spread / first.Value;
            var step = previous is { } p ? scale - p : 0;
            previous = scale;

            var arrow = step > 0.004 ? "up" : step < -0.004 ? "DOWN" : "-";
            Console.WriteLine($"{i,5}  ({cx,6:F1},{cy,6:F1})  {spread,7:F2}  {scale,6:F3}  {step,7:F3} {arrow}");
        }
    }

    /// <summary>The centroid of the dark pixels, and their mean distance from it.</summary>
    private static (double X, double Y, double Spread) Ink(int[,] pixels)
    {
        var height = pixels.GetLength(0);
        var width = pixels.GetLength(1);

        // Weight by how dark a pixel is, so text counts and the page behind it does not. The floor keeps
        // anti-aliasing and panel noise out of the centroid without discarding grey text.
        const int Floor = 40;

        double weight = 0, sumX = 0, sumY = 0;
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var ink = 255 - pixels[y, x];
                if (ink <= Floor)
                    continue;

                weight += ink;
                sumX += ink * x;
                sumY += ink * y;
            }
        }

        if (weight <= 0)
            return (0, 0, 0);

        var cx = sumX / weight;
        var cy = sumY / weight;

        double spread = 0;
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var ink = 255 - pixels[y, x];
                if (ink > Floor)
                    spread += ink * (Math.Abs(x - cx) + Math.Abs(y - cy));
            }
        }

        return (cx, cy, spread / weight);
    }

    private static Bitmap Capture(PixelRect area)
    {
        var bitmap = new Bitmap(area.Width, area.Height, PixelFormat.Format32bppArgb);
        using var graphics = Graphics.FromImage(bitmap);
        graphics.CopyFromScreen(area.Left, area.Top, 0, 0, new Size(area.Width, area.Height), CopyPixelOperation.SourceCopy);
        return bitmap;
    }

    private static int[,] Load(string path, PixelRect? region)
    {
        using var bitmap = new Bitmap(path);
        var left = region?.Left ?? 0;
        var top = region?.Top ?? 0;
        var width = Math.Min(region?.Width ?? bitmap.Width, bitmap.Width - left);
        var height = Math.Min(region?.Height ?? bitmap.Height, bitmap.Height - top);

        var pixels = new int[height, width];
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var colour = bitmap.GetPixel(left + x, top + y);
                pixels[y, x] = (colour.R + colour.G + colour.B) / 3;
            }
        }

        return pixels;
    }

    private static double Differing(int[,] a, int[,] b, int dx, int dy)
    {
        const int Tolerance = 12;      // JPEG-free screenshots still vary by a shade or two
        var height = a.GetLength(0);
        var width = a.GetLength(1);
        var compared = 0;
        var differing = 0;

        for (var y = 0; y < height; y++)
        {
            var sy = y + dy;
            if (sy < 0 || sy >= height)
                continue;

            for (var x = 0; x < width; x++)
            {
                var sx = x + dx;
                if (sx < 0 || sx >= width)
                    continue;

                compared++;
                if (Math.Abs(a[y, x] - b[sy, sx]) > Tolerance)
                    differing++;
            }
        }

        return compared == 0 ? 100 : differing * 100.0 / compared;
    }

    private static (int Shift, double Differing) BestShift(int[,] a, int[,] b, bool vertical, int other)
    {
        var best = (Shift: 0, Differing: double.MaxValue);
        for (var shift = -400; shift <= 400; shift += 2)
        {
            var value = vertical ? Differing(a, b, other, shift) : Differing(a, b, shift, other);
            if (value < best.Differing)
                best = (shift, value);
        }

        return best;
    }

    private static double[] RowProfile(int[,] pixels, int top, int bottom)
    {
        var height = pixels.GetLength(0);
        var width = pixels.GetLength(1);
        var from = Math.Clamp(top, 0, height);
        var to = Math.Clamp(bottom, from, height);

        var rows = new double[to - from];
        for (var y = from; y < to; y++)
        {
            var total = 0L;
            for (var x = 0; x < width; x++)
                total += pixels[y, x];

            rows[y - from] = total / (double)width;
        }

        return rows;
    }

    private static double Compare(double[] a, double[] b, double scale, int offset)
    {
        var total = 0.0;
        var counted = 0;
        for (var i = 0; i < a.Length; i++)
        {
            var j = (int)Math.Round((i * scale) + offset);
            if (j < 0 || j >= b.Length)
                continue;

            var d = a[i] - b[j];
            total += d * d;
            counted++;
        }

        return counted < a.Length / 4 ? double.MaxValue : Math.Sqrt(total / counted);
    }
}
