using System.Globalization;

using Microsoft.Extensions.Logging.Abstractions;

using SmartZoom.Core.Input;
using SmartZoom.Core.Routing;
using SmartZoom.Core.Settings;
using SmartZoom.Core.Zoom.Content;
using SmartZoom.Core.Zoom.Gesture;
using SmartZoom.Core.Zoom.Office;
using SmartZoom.Core.Zoom.Reader;
using SmartZoom.Interop.Accessibility;
using SmartZoom.Interop.Input;
using SmartZoom.Interop.Office;
using SmartZoom.Interop.Windows;

namespace SmartZoom.Probe;

/// <summary>
/// The tool every measured constant in docs/measurements.md came out of. It drives the same code the tray
/// app does, so what it reports is what SmartZoom would do.
/// </summary>
internal static class Program
{
    private static int Main(string[] args)
    {
        if (args.Length == 0 || args[0] is "-h" or "--help" or "help")
        {
            Help();
            return 0;
        }

        try
        {
            return Run(args) ? 0 : 1;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"{ex.GetType().Name}: {ex.Message}");
            return 1;
        }
    }

    private static bool Run(string[] args)
    {
        switch (args[0])
        {
            case "windows":
                return Windows(Point(args, 1));

            case "hittest":
                return HitTest(Point(args, 1));

            case "plan":
                return Plan(Point(args, 1), Number(args, 3), args.Length > 4 ? args[4] : "Chromium", Bounds(args, 5));

            case "pinch":
                return Pinch(Point(args, 1), Number(args, 3), Integer(args, 4, 300), Bounds(args, 5));

            case "wheel":
                return Wheel(Point(args, 1), Integer(args, 3, 0));

            case "keys":
                return Arg(args, 3, "combination") is { } combination && Keys(Point(args, 1), combination);

            case "excel":
                return Excel(Point(args, 1));

            case "exceltrace":
                return ExcelTrace(Point(args, 1), Integer(args, 3, 4000));

            case "word":
                return Word(Point(args, 1), Number(args, 3), Integer(args, 4, 0));

            case "shot":
                if (Arg(args, 1, "file.png") is not { } shot)
                    return false;

                Screen.Shoot(Region(args, 2) ?? Under(Point(args, 2)), shot);
                return true;

            case "diff":
                if (Arg(args, 1, "a.png") is not { } diffFirst || Arg(args, 2, "b.png") is not { } diffSecond)
                    return false;

                Screen.Diff(diffFirst, diffSecond, Region(args, 3));
                return true;

            case "scale":
                if (Arg(args, 1, "a.png") is not { } scaleFirst || Arg(args, 2, "b.png") is not { } scaleSecond)
                    return false;

                Screen.Scale(scaleFirst, scaleSecond, Integer(args, 3, 0), Integer(args, 4, 4000), Number(args, 5, 0.5), Number(args, 6, 3.0));
                return true;

            case "track":
                return Arg(args, 1, "prefix") is { } prefix && Track(prefix);

            default:
                Console.Error.WriteLine($"unknown command \"{args[0]}\"");
                Help();
                return false;
        }
    }

    // ------------------------------------------------------------------ reading

    /// <summary>Every <c>&lt;prefix&gt;*.png</c> in name order, which is capture order.</summary>
    private static bool Track(string prefix)
    {
        var directory = Path.GetDirectoryName(prefix);
        if (string.IsNullOrEmpty(directory))
            directory = ".";

        var frames = Directory.GetFiles(directory, Path.GetFileName(prefix) + "*.png");
        Array.Sort(frames, StringComparer.Ordinal);

        if (frames.Length == 0)
        {
            Console.Error.WriteLine($"no frames matching {prefix}*.png");
            return false;
        }

        Screen.Track(frames);
        return true;
    }

    private static bool Windows(ScreenPoint point)
    {
        if (new WindowInspector().GetTargetAt(point) is not { } target)
        {
            Console.WriteLine($"nothing at ({point.X}, {point.Y})");
            return false;
        }

        Console.WriteLine($"process   {target.ProcessName} (pid {target.ProcessId})");
        Console.WriteLine($"root      0x{target.RootWindow:X} class {target.RootClassName}");
        Console.WriteLine($"hit       0x{target.HitWindow:X} class {target.HitClassName}");
        return true;
    }

    private static bool HitTest(ScreenPoint point)
    {
        if (new WindowInspector().GetTargetAt(point) is not { } target)
            return false;

        var tester = new MsaaContentHitTester(new ChromiumAccessibilityWake(NullLogger<ChromiumAccessibilityWake>.Instance), NullLogger<MsaaContentHitTester>.Instance);
        var hit = tester.HitTestAsync(target, point, CancellationToken.None).GetAwaiter().GetResult();
        if (hit is null)
        {
            Console.WriteLine($"{target.ProcessName} exposes no content at ({point.X}, {point.Y})");
            return false;
        }

        Console.WriteLine($"viewport  {Describe(hit.Viewport)}");
        foreach (var node in hit.Chain)
            Console.WriteLine($"  {node.Role,-10} {Describe(node.Bounds)}");

        return true;
    }

    private static bool Plan(ScreenPoint point, double factor, string engineName, PixelRect? given)
    {
        if (!Enum.TryParse<GestureEngine>(engineName, ignoreCase: true, out var engine))
        {
            Console.Error.WriteLine($"unknown engine \"{engineName}\"; one of {string.Join(", ", Enum.GetNames<GestureEngine>())}");
            return false;
        }

        // The same inputs the injector uses: the display's own scale and the recognizer's minimum scaling span.
        var bounds = given ?? Under(point);
        var scale = new DisplayScale().ScaleAt(point);
        var slop = RecognizerProfile.SpanSlop(engine, scale);
        var plan = PinchGeometry.Plan(point, factor, slop, bounds, RecognizerProfile.MinimumScalingSpan(engine));

        Console.WriteLine(Invariant($"bounds    {Describe(bounds)}"));
        Console.WriteLine(Invariant($"engine    {engine}, span slop {slop:F1} px at {scale:P0}"));
        Console.WriteLine(Invariant($"focus     ({plan.Focus.X}, {plan.Focus.Y}) {(plan.Vertical ? "vertical" : "horizontal")}"));
        Console.WriteLine(Invariant($"spans     down {plan.DownHalf:F1} -> pre-roll {plan.PreRolledHalf:F1} -> end {plan.EndHalf:F1} (half, px)"));
        Console.WriteLine(Invariant($"pan       ({plan.Pan.X}, {plan.Pan.Y})"));

        if (plan.NarrowedHalfGap is { } narrowed)
            Console.WriteLine(Invariant($"narrowed  half gap {narrowed:F1} px"));

        if (plan.Shortfall is { } shortfall)
            Console.WriteLine(Invariant($"shrunk    wanted {shortfall.NeededHalfSpread:F1} px, had {shortfall.Room:F1} px"));

        return true;
    }

    // ------------------------------------------------------------------ acting

    private static bool Pinch(ScreenPoint point, double factor, int milliseconds, PixelRect? bounds)
    {
        if (!Aim(point, Invariant($"pinch x{factor:F2} over {milliseconds} ms")))
            return false;

        var injector = new TouchPinchInjector(new TouchDevices(NullLogger<TouchDevices>.Instance), NullLogger<TouchPinchInjector>.Instance);
        // Bounds given on the command line replay what an adapter passed (the browser's contact area, say);
        // otherwise the content area of the window under the point.
        var ok = injector.PinchAsync(point, factor, TimeSpan.FromMilliseconds(milliseconds), bounds ?? Under(point), CancellationToken.None)
            .GetAwaiter().GetResult();

        Console.WriteLine(ok ? "gesture delivered" : "the gesture was rejected");
        return ok;
    }

    private static bool Wheel(ScreenPoint point, int pixels)
    {
        if (new WindowInspector().GetTargetAt(point) is not { } target || !Aim(point, $"scroll {pixels} px"))
            return false;

        var view = new ReaderView(NullLogger<ReaderView>.Instance);
        var moved = view.ScrollBy(target, pixels, CancellationToken.None);

        Console.WriteLine($"asked for {pixels} px, measured {moved} px");
        return true;
    }

    private static bool Keys(ScreenPoint point, string combination)
    {
        if (new WindowInspector().GetTargetAt(point) is not { } target || !Aim(point, $"send {combination}"))
            return false;

        var sender = new ShortcutSender(
            new SendInputInjector(),
            new WindowActivator(),
            TimeProvider.System,
            NullLogger<ShortcutSender>.Instance);

        var sent = sender.SendAsync(target, KeyCombo.Parse(combination), null, null, CancellationToken.None).GetAwaiter().GetResult();
        Console.WriteLine(sent ? "sent" : "not sent (see the reason above)");
        return sent;
    }

    /// <summary>
    /// Whether Word can carry a zoom the way a PDF reader does: it renders a pinch itself, which is smooth,
    /// but only if it then lets the object model decide where the zoom lands. Word was found in 2026-09-16 to
    /// commit the gesture asynchronously and overwrite that value; Excel was found not to. This measures it.
    /// </summary>
    /// <param name="point">Where to pinch, over the document.</param>
    /// <param name="factor">Gesture factor; 1 or less only reads and sets.</param>
    /// <param name="percent">Zoom to set through the object model afterwards; 0 to leave it alone.</param>
    private static bool Word(ScreenPoint point, double factor, int percent)
    {
        if (new WindowInspector().GetTargetAt(point) is not { } target)
            return false;

        using var word = new WordAutomation(NullLogger<WordAutomation>.Instance);
        using var window = word.AttachAsync(target, CancellationToken.None).GetAwaiter().GetResult();

        if (window is null)
        {
            Console.WriteLine($"{target.ProcessName} is not a Word document window");
            return false;
        }

        var before = window.GetState();
        Console.WriteLine($"before      zoom {before.ZoomPercent}%");

        if (factor > 1)
        {
            var injector = new TouchPinchInjector(new TouchDevices(NullLogger<TouchDevices>.Instance), NullLogger<TouchPinchInjector>.Instance);
            injector.PinchAsync(point, factor, TimeSpan.FromMilliseconds(300), Under(point), CancellationToken.None).GetAwaiter().GetResult();

            foreach (var settle in new[] { 0, 150, 400 })
            {
                Thread.Sleep(settle);
                Console.WriteLine($"after pinch zoom {window.GetState().ZoomPercent}% (settled {settle} ms)");
            }
        }

        if (percent > 0)
        {
            window.SetZoom(percent);
            foreach (var wait in new[] { 100, 300, 600, 1500 })
            {
                Thread.Sleep(wait);
                Console.WriteLine($"+{wait,4} ms   zoom {window.GetState().ZoomPercent}% (asked for {percent}%)");
            }
        }

        return true;
    }

    /// <summary>
    /// Samples the worksheet's zoom and scroll as fast as the object model will answer, so a press can be
    /// watched happening. Every distinct line is a separate thing the user sees move.
    /// </summary>
    /// <param name="point">A point over the worksheet, to attach through.</param>
    /// <param name="milliseconds">How long to watch for.</param>
    private static bool ExcelTrace(ScreenPoint point, int milliseconds)
    {
        if (new WindowInspector().GetTargetAt(point) is not { } target)
            return false;

        using var excel = new ExcelAutomation(NullLogger<ExcelAutomation>.Instance);
        using var window = excel.AttachAsync(target, CancellationToken.None).GetAwaiter().GetResult();
        if (window is null)
        {
            Console.WriteLine($"{target.ProcessName} is not an Excel worksheet window");
            return false;
        }

        var clock = System.Diagnostics.Stopwatch.StartNew();
        var last = string.Empty;
        while (clock.ElapsedMilliseconds < milliseconds)
        {
            string now;
            try
            {
                var state = window.GetState();
                now = $"zoom {state.ZoomPercent}%, row {state.ScrollRow}, column {state.ScrollColumn}";
            }
            catch (Exception ex) when (ex is System.Runtime.InteropServices.COMException or TimeoutException)
            {
                now = "busy";
            }

            if (now != last)
            {
                Console.WriteLine($"{clock.ElapsedMilliseconds,5} ms  {now}");
                last = now;
            }

            Thread.Sleep(15);
        }

        return true;
    }

    private static bool Excel(ScreenPoint point)
    {
        if (new WindowInspector().GetTargetAt(point) is not { } target)
            return false;

        using var excel = new ExcelAutomation(NullLogger<ExcelAutomation>.Instance);
        using var window = excel.AttachAsync(target, CancellationToken.None).GetAwaiter().GetResult();

        if (window is null)
        {
            Console.WriteLine($"{target.ProcessName} is not an Excel worksheet window");
            return false;
        }

        var before = window.GetState();
        Console.WriteLine($"before    zoom {before.ZoomPercent}%, row {before.ScrollRow}, column {before.ScrollColumn}");

        // This applies the fit: Excel will only report one by performing it. The view goes back below.
        var fit = window.MeasureFitAt(point);
        if (fit is { } block)
        {
            Console.WriteLine($"block     {block.Rows} rows x {block.Columns} columns at row {block.Row}, column {block.Column}");
            Console.WriteLine($"cursor    row {block.CursorRow}");
            Console.WriteLine($"fit       {block.FitZoomPercent}% in a {block.PaneWidthPx} px pane");
        }
        else
        {
            Console.WriteLine("no data, chart or picture under the cursor");
        }

        window.Restore(before);
        Console.WriteLine("view restored");
        return true;
    }

    // ------------------------------------------------------------------ plumbing

    /// <summary>
    /// Names what is about to be acted on and counts down. Everything below this line injects input into
    /// somebody else's window, so it should never be a surprise which one.
    /// </summary>
    private static bool Aim(ScreenPoint point, string what)
    {
        if (new WindowInspector().GetTargetAt(point) is not { } target)
        {
            Console.Error.WriteLine($"nothing at ({point.X}, {point.Y}); refusing to inject input into the void");
            return false;
        }

        Console.WriteLine($"about to {what} at ({point.X}, {point.Y})");
        Console.WriteLine($"target:  {target.ProcessName} (class {target.RootClassName}, hit {target.HitClassName})");

        for (var i = 3; i > 0; i--)
        {
            Console.Write($"\r{i}... (Ctrl+C to stop) ");
            Thread.Sleep(1000);
        }

        Console.WriteLine("\rgo             ");
        return true;
    }

    /// <summary>The window rectangle under a point, which is the bounds a gesture must stay inside.</summary>
    private static PixelRect Under(ScreenPoint point)
    {
        if (new WindowInspector().GetTargetAt(point) is not { } target)
            throw new InvalidOperationException($"no window at ({point.X}, {point.Y})");

        return new ReaderView(NullLogger<ReaderView>.Instance).Bounds(target)
            ?? throw new InvalidOperationException("that window has no usable content area");
    }

    /// <summary>A required argument, or null after saying which one is missing and printing the usage.</summary>
    private static string? Arg(string[] args, int index, string name)
    {
        if (args.Length > index)
            return args[index];

        Console.Error.WriteLine($"missing <{name}>");
        Help();
        return null;
    }

    private static ScreenPoint Point(string[] args, int index) =>
        new(Integer(args, index, 0), Integer(args, index + 1, 0));

    private static PixelRect? Region(string[] args, int index) =>
        args.Length > index + 3
            ? new PixelRect(Integer(args, index, 0), Integer(args, index + 1, 0), Integer(args, index + 2, 0), Integer(args, index + 3, 0))
            : null;

    /// <summary>Four integers from <paramref name="index"/> on as left, top, right, bottom; null when absent.</summary>
    private static PixelRect? Bounds(string[] args, int index) =>
        args.Length > index + 3
            ? new PixelRect(Integer(args, index, 0), Integer(args, index + 1, 0), Integer(args, index + 2, 0), Integer(args, index + 3, 0))
            : null;

    private static int Integer(string[] args, int index, int fallback) =>
        args.Length > index && int.TryParse(args[index], NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) ? value : fallback;

    private static double Number(string[] args, int index, double fallback = 2.0) =>
        args.Length > index && double.TryParse(args[index], NumberStyles.Float, CultureInfo.InvariantCulture, out var value) ? value : fallback;

    private static string Describe(PixelRect rect) =>
        $"{rect.Width}x{rect.Height} at ({rect.Left}, {rect.Top})";

    private static string Invariant(FormattableString text) => text.ToString(CultureInfo.InvariantCulture);

    private static void Help() => Console.WriteLine("""
        smartzoom-probe <command> [arguments]

        Reading (harmless):
          windows <x> <y>                       what SmartZoom sees under a point
          hittest <x> <y>                       the accessibility chain a browser exposes there
          plan    <x> <y> <factor> [engine] [l t r b]  where a pinch would put its fingers, and why

        Acting (injects input; names the target and counts down first):
          pinch   <x> <y> <factor> [ms] [l t r b]  one pinch gesture around a point, optionally inside these bounds
          wheel   <x> <y> <pixels>              scroll a reader and measure how far it really moved
          keys    <x> <y> "<combination>"       send a shortcut the way the reader adapter does
          excel   <x> <y>                       report the block under the cursor and the fit Excel computes

        Measuring:
          shot <file.png> [x0 y0 x1 y1]         a screenshot; without a region, the window under (x0, y0)
          diff <a.png> <b.png> [x0 y0 x1 y1]    how much differs, and what a whole-image shift explains
          scale <a.png> <b.png> [y0 y1 lo hi]   the scale and offset that map one onto the other
          track <prefix>                        the scale each frame of a captured zoom reached, in order

        docs/measurements.md says which command produced which constant.
        """);
}
