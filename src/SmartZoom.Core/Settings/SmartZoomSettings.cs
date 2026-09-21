using System.Text.Json.Serialization;
using SmartZoom.Core.Input;
using SmartZoom.Core.Routing;

namespace SmartZoom.Core.Settings;

/// <summary>Root of the user settings file (<c>%APPDATA%\SmartZoom\settings.json</c>).</summary>
public sealed class SmartZoomSettings
{
    /// <summary>Master switch; when false the hook passes all input through.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Gestures that fire SmartZoom. Any of them works; mouse buttons and hotkeys can be mixed.</summary>
    public IList<TriggerSettings> Triggers { get; set; } = [TriggerSettings.CreateDefault()];

    /// <summary>Pre-M2.5 single trigger. Read for migration only; never written.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public TriggerSettings? Trigger { get; set; }

    /// <summary>Which applications are handled, and how.</summary>
    public RoutingSettings Routing { get; set; } = new();

    /// <summary>Zoom behavior shared by all adapters.</summary>
    public ZoomSettings Zoom { get; set; } = new();

    /// <summary>
    /// Brings older files up to date: moves a legacy <see cref="Trigger"/> into <see cref="Triggers"/>.
    /// </summary>
    /// <returns>True if anything changed and the file should be rewritten.</returns>
    public bool Migrate()
    {
        var changed = false;

        if (Zoom.Keys is { } reader)
        {
            // "Keys" described shortcuts; the section now describes how readers are zoomed, gesture included.
            // A file that already carries a "Reader" section has been through this once and the newer section
            // is the one the user has been editing, so the leftover is only dropped.
            if (Zoom.Reader.IsDefault())
                Zoom.Reader = reader;

            Zoom.Keys = null;
            changed = true;
        }

        if (Trigger is null)
            return changed;

        if (Triggers.Count == 0 || (Triggers.Count == 1 && Triggers[0].IsDefault()))
            Triggers = [Trigger];

        Trigger = null;
        return true;
    }
}

/// <summary>One trigger gesture. Set exactly one of <see cref="Mouse"/> or <see cref="Keys"/>.</summary>
public sealed class TriggerSettings
{
    /// <summary>The out-of-the-box trigger: double-tap of the Forward button.</summary>
    public static TriggerSettings CreateDefault() => new() { Mouse = MouseButton.XButton2 };

    /// <summary>Mouse button that fires the trigger. Leave unset for a hotkey trigger.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public MouseButton? Mouse { get; set; }

    /// <summary>Pre-M2.5 name of <see cref="Mouse"/>. Read for migration only; never written.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public MouseButton? Button
    {
        get => null;
        set
        {
            if (value is not null)
                Mouse = value;
        }
    }

    /// <summary>Key combination that fires the trigger, e.g. "Ctrl+Alt+Z", "F9" or a bare "Ctrl" for double-tap.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Keys { get; set; }

    /// <summary>
    /// Presses per trigger: 2 (double-tap, the default, safe for an input that also has another job)
    /// or 1 (every press, ideal for an input dedicated to SmartZoom).
    /// </summary>
    public int TapCount { get; set; } = 2;

    /// <summary>For double-tap, the maximum time between presses in milliseconds; null uses the system double-click time.</summary>
    public uint? DoubleTapWindowMs { get; set; }

    /// <summary>
    /// Hide the trigger's presses from the target application. With double-tap, single presses are
    /// then delayed by the double-tap window before being replayed (noticeable on Back/Forward buttons).
    /// With single tap there is no delay.
    /// </summary>
    public bool SwallowClicks { get; set; }

    /// <summary>Builds the runtime definition.</summary>
    /// <param name="systemDoubleClickTimeMs">Used when <see cref="DoubleTapWindowMs"/> is null.</param>
    /// <exception cref="InvalidOperationException">Neither or both of <see cref="Mouse"/> and <see cref="Keys"/> are set.</exception>
    /// <exception cref="FormatException"><see cref="Keys"/> is not a valid combination.</exception>
    public TriggerDefinition ToDefinition(uint systemDoubleClickTimeMs)
    {
        var tap = new TapOptions(TapCount, DoubleTapWindowMs ?? systemDoubleClickTimeMs, SwallowClicks);

        return (Mouse, Keys) switch
        {
            (not null and not MouseButton.None, null) => new MouseButtonTrigger(Mouse.Value, tap),
            (null or MouseButton.None, { Length: > 0 }) => new HotkeyTrigger(KeyCombo.Parse(Keys), tap),
            (null or MouseButton.None, _) => throw new InvalidOperationException("A trigger needs either \"Mouse\" or \"Keys\"."),
            _ => throw new InvalidOperationException("A trigger can't have both \"Mouse\" and \"Keys\"; use two triggers."),
        };
    }

    internal bool IsDefault() =>
        Mouse == MouseButton.XButton2 && Keys is null && TapCount == 2 && DoubleTapWindowMs is null && !SwallowClicks;
}

/// <summary>Process-to-adapter routing. Process names are image names without ".exe", case-insensitive.</summary>
public sealed class RoutingSettings
{
    /// <summary>Browsers handled by the native smart zoom (accessibility hit-test + touch pinch): Chromium-based browsers and Firefox.</summary>
    public IList<string> BrowserProcesses { get; set; } = ["chrome", "msedge", "brave", "opera", "vivaldi", "firefox"];

    /// <summary>Processes handled by synthesized Ctrl+wheel zoom.</summary>
    public IList<string> CtrlWheelProcesses { get; set; } = ["i_view64", "i_view32"];

    /// <summary>Processes zoomed with their own keyboard shortcuts (<see cref="ZoomSettings.Keys"/>): readers with fit-width / fit-page commands.</summary>
    public IList<string> KeyProcesses { get; set; } = ["Acrobat", "AcroRd32", "SumatraPDF"];

    /// <summary>Processes handled through the Word object model.</summary>
    public IList<string> WordProcesses { get; set; } = ["WINWORD"];

    /// <summary>Processes zoomed through Microsoft Excel's object model.</summary>
    public IList<string> ExcelProcesses { get; set; } = ["EXCEL"];

    /// <summary>Processes handled through the PowerPoint object model.</summary>
    public IList<string> PowerPointProcesses { get; set; } = ["POWERPNT"];

    /// <summary>Per-process override that wins over the lists above; use <see cref="AdapterKind.None"/> to opt an app out.</summary>
    public IDictionary<string, AdapterKind> Overrides { get; set; } = new Dictionary<string, AdapterKind>(StringComparer.OrdinalIgnoreCase);
}

/// <summary>Zoom behavior shared by all adapters.</summary>
public sealed class ZoomSettings
{
    /// <summary>Smallest zoom factor a smart zoom will apply.</summary>
    public double MinScale { get; set; } = 1.1;

    /// <summary>Largest zoom factor a smart zoom will apply.</summary>
    public double MaxScale { get; set; } = 3.0;

    /// <summary>Animate zoom transitions where the adapter supports it.</summary>
    public bool Animate { get; set; } = true;

    /// <summary>When a richer adapter can't act (e.g. no browser extension connected), use Ctrl+wheel instead.</summary>
    public bool FallbackToCtrlWheel { get; set; } = true;

    /// <summary>Tuning for the generic Ctrl+wheel adapter.</summary>
    public CtrlWheelSettings CtrlWheel { get; set; } = new();

    /// <summary>Tuning for the browser smart-zoom adapter.</summary>
    public BrowserZoomSettings Browser { get; set; } = new();

    /// <summary>How PDF readers and other document viewers are zoomed.</summary>
    public ReaderZoomSettings Reader { get; set; } = new();

    /// <summary>Former name of <see cref="Reader"/>; read from older settings files and then dropped.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ReaderZoomSettings? Keys { get; set; }
}

/// <summary>
/// How a document reader is zoomed: by default an animated pinch around the cursor, with the reader's own
/// commands to land on. Defaults are Acrobat's and Sumatra's fit width and fit page.
/// </summary>
public sealed class ReaderZoomSettings
{
    /// <summary>Combination sent on the first press; by default "fit width", which fills the window with the page.</summary>
    public string ZoomInKeys { get; set; } = "Ctrl+2";

    /// <summary>Combination sent on the second press; by default "fit page", which shows the whole page again.</summary>
    public string ZoomOutKeys { get; set; } = "Ctrl+0";

    /// <summary>
    /// Zoom with an animated pinch around the cursor, the way browsers are zoomed, instead of jumping straight
    /// to the shortcut's zoom level. What you pointed at stays where it is and simply grows. Readers that
    /// ignore touch should have this off, so the shortcuts are used instead.
    /// </summary>
    public bool Gesture { get; set; } = true;

    /// <summary>How much the pinch magnifies; ignored when <see cref="Gesture"/> is off.</summary>
    public double Scale { get; set; } = 2.0;

    /// <summary>
    /// With <see cref="Gesture"/> off: scroll the block under the cursor to the top of the reader before
    /// zooming, so the first press magnifies what you pointed at rather than wherever the reader happened to
    /// be. The second press scrolls back.
    /// </summary>
    public bool FollowCursor { get; set; } = true;

    /// <summary>Gap left above the cursor's content when <see cref="FollowCursor"/> scrolls it to the top.</summary>
    public int MarginPx { get; set; } = 16;

    /// <summary>How long the gesture takes, in milliseconds.</summary>
    public int AnimationMs { get; set; } = 300;

    /// <summary>Whether nothing in this section has been changed from its default.</summary>
    internal bool IsDefault()
    {
        var fresh = new ReaderZoomSettings();
        return ZoomInKeys == fresh.ZoomInKeys
            && ZoomOutKeys == fresh.ZoomOutKeys
            && Gesture == fresh.Gesture
            && Scale == fresh.Scale
            && FollowCursor == fresh.FollowCursor
            && MarginPx == fresh.MarginPx
            && AnimationMs == fresh.AnimationMs;
    }
}

/// <summary>Tuning for the browser smart-zoom adapter.</summary>
public sealed class BrowserZoomSettings
{
    /// <summary>Space left between the zoomed block and the viewport edges, in pixels.</summary>
    public int MarginPx { get; set; } = 16;

    /// <summary>Length of the zoom gesture when <see cref="ZoomSettings.Animate"/> is on.</summary>
    public int AnimationMs { get; set; } = 280;

    /// <summary>
    /// Minimum distance between the gesture's anchor and the window edges, in pixels. Normally not needed:
    /// the gesture orients its contacts to stay inside the window on its own.
    /// </summary>
    public int AnchorInsetPx { get; set; }
}

/// <summary>Tuning for the generic Ctrl+wheel adapter.</summary>
public sealed class CtrlWheelSettings
{
    /// <summary>Wheel detents sent per zoom. Most apps step 10–25% per detent.</summary>
    public int Ticks { get; set; } = 6;

    /// <summary>Pause between detents so the app doesn't coalesce them; 0 sends them back to back.</summary>
    public int IntervalMs { get; set; } = 20;
}
