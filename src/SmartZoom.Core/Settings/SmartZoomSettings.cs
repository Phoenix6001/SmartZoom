using SmartZoom.Core.Input;
using SmartZoom.Core.Routing;

namespace SmartZoom.Core.Settings;

/// <summary>Root of the user settings file (<c>%APPDATA%\SmartZoom\settings.json</c>).</summary>
public sealed class SmartZoomSettings
{
    /// <summary>Master switch; when false the hook passes all input through.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Trigger gesture configuration.</summary>
    public TriggerSettings Trigger { get; set; } = new();

    /// <summary>Which applications are handled, and how.</summary>
    public RoutingSettings Routing { get; set; } = new();

    /// <summary>Zoom behavior shared by all adapters.</summary>
    public ZoomSettings Zoom { get; set; } = new();
}

/// <summary>Trigger gesture configuration.</summary>
public sealed class TriggerSettings
{
    /// <summary>Button whose double-press fires the trigger.</summary>
    public MouseButton Button { get; set; } = MouseButton.XButton2;

    /// <summary>Maximum time between presses in milliseconds; null uses the system double-click time.</summary>
    public uint? DoubleTapWindowMs { get; set; }

    /// <summary>
    /// Hide trigger presses from the target application. Single presses are then delayed by the
    /// double-tap window before being replayed, which is noticeable on XButton back/forward.
    /// </summary>
    public bool SwallowClicks { get; set; }
}

/// <summary>Process-to-adapter routing. Process names are image names without ".exe", case-insensitive.</summary>
public sealed class RoutingSettings
{
    /// <summary>Processes handled by the browser extension.</summary>
    public IList<string> BrowserProcesses { get; set; } = ["chrome", "msedge", "firefox"];

    /// <summary>Processes handled by synthesized Ctrl+wheel zoom.</summary>
    public IList<string> CtrlWheelProcesses { get; set; } = ["Acrobat", "AcroRd32", "SumatraPDF", "i_view64", "i_view32", "EXCEL"];

    /// <summary>Processes handled through the Word object model.</summary>
    public IList<string> WordProcesses { get; set; } = ["WINWORD"];

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
}
