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
                return Plan(Point(args, 1), Number(args, 3), args.Length > 4 ? args[4] : "Chromium");

            case "pinch":
                return Pinch(Point(args, 1), Number(args, 3), Integer(args, 4, 300));

            case "wheel":
                return Wheel(Point(args, 1), Integer(args, 3, 0));

            case "keys":
                return Keys(Point(args, 1), args[3]);

            case "excel":
                return Excel(Point(args, 1));

            case "shot":
                Screen.Shoot(Region(args, 2) ?? Under(Point(args, 2)), args[1]);
                return true;

            case "diff":
                Screen.Diff(args[1], args[2], Region(args, 3));
                return true;

            case "scale":
                Screen.Scale(args[1], args[2], Integer(args, 3, 0), Integer(args, 4, 4000), Number(args, 5, 0.5), Number(args, 6, 3.0));
                return true;

            default:
                Console.Error.WriteLine($"unknown command \"{args[0]}\"");
                Help();
                return false;
        }
    }

    // ------------------------------------------------------------------ reading

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

    private static bool Plan(ScreenPoint point, double factor, string engineName)
    {
        if (!Enum.TryParse<GestureEngine>(engineName, ignoreCase: true, out var engine))
        {
            Console.Error.WriteLine($"unknown engine \"{engineName}\"; one of {string.Join(", ", Enum.GetNames<GestureEngine>())}");
            return false;
        }

        var bounds = Under(point);
        var slop = RecognizerProfile.SpanSlop(engine, 2.0);
        var plan = PinchGeometry.Plan(point, factor, slop, bounds);

        Console.WriteLine(Invariant($"window    {Describe(bounds)}"));
        Console.WriteLine(Invariant($"engine    {engine}, span slop {slop} px at 200%"));
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

    private static bool Pinch(ScreenPoint point, double factor, int milliseconds)
    {
        if (!Aim(point, Invariant($"pinch x{factor:F2} over {milliseconds} ms")))
            return false;

        var injector = new TouchPinchInjector(new TouchDevices(NullLogger<TouchDevices>.Instance), NullLogger<TouchPinchInjector>.Instance);
        var ok = injector.PinchAsync(point, factor, TimeSpan.FromMilliseconds(milliseconds), Under(point), CancellationToken.None)
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

    private static bool Excel(ScreenPoint point)
    {
        if (new WindowInspector().GetTargetAt(point) is not { } target)
            return false;

        using var window = new ExcelAutomation(NullLogger<ExcelAutomation>.Instance)
            .AttachAsync(target, CancellationToken.None).GetAwaiter().GetResult();

        if (window is null)
        {
            Console.WriteLine($"{target.ProcessName} is not an Excel worksheet window");
            return false;
        }

        var before = window.GetState();
        Console.WriteLine($"before    zoom {before.ZoomPercent}%, row {before.ScrollRow}, column {before.ScrollColumn}");

        // This applies the fit: Excel will only report one by performing it. The view goes back below.
        var fit = window.ApplyFitToBlockAt(point);
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

    private static ScreenPoint Point(string[] args, int index) =>
        new(Integer(args, index, 0), Integer(args, index + 1, 0));

    private static PixelRect? Region(string[] args, int index) =>
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
          plan    <x> <y> <factor> [engine]     where a pinch would put its fingers, and why

        Acting (injects input; names the target and counts down first):
          pinch   <x> <y> <factor> [ms]         one pinch gesture around a point
          wheel   <x> <y> <pixels>              scroll a reader and measure how far it really moved
          keys    <x> <y> "<combination>"       send a shortcut the way the reader adapter does
          excel   <x> <y>                       report the block under the cursor and the fit Excel computes

        Measuring:
          shot <file.png> [x0 y0 x1 y1]         a screenshot; without a region, the window under (x0, y0)
          diff <a.png> <b.png> [x0 y0 x1 y1]    how much differs, and what a whole-image shift explains
          scale <a.png> <b.png> [y0 y1 lo hi]   the scale and offset that map one onto the other

        docs/measurements.md says which command produced which constant.
        """);
}
