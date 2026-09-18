using SmartZoom.Core.Settings;

namespace SmartZoom.Core.Routing;

/// <summary>The zoom strategy responsible for a target application.</summary>
public enum AdapterKind
{
    /// <summary>Not handled; the trigger is ignored.</summary>
    None = 0,

    /// <summary>Browser extension via native messaging (true element-aware smart zoom).</summary>
    Browser,

    /// <summary>Synthesized Ctrl+wheel burst centered on the cursor.</summary>
    CtrlWheel,

    /// <summary>Microsoft Word object model.</summary>
    WordCom,

    /// <summary>Microsoft PowerPoint object model.</summary>
    PowerPointCom,

    /// <summary>The application's own keyboard shortcuts, e.g. fit width and fit page in a PDF reader.</summary>
    Keys,
}

/// <summary>Maps a process image name to the zoom strategy that should handle it.</summary>
public sealed class ZoomRouter
{
    private readonly Dictionary<string, AdapterKind> _map = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Builds the routing table. Later categories win on duplicates; <see cref="RoutingSettings.Overrides"/> always win.</summary>
    /// <param name="settings">Routing configuration.</param>
    public ZoomRouter(RoutingSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        Add(settings.BrowserProcesses, AdapterKind.Browser);
        Add(settings.CtrlWheelProcesses, AdapterKind.CtrlWheel);
        Add(settings.KeyProcesses, AdapterKind.Keys);
        Add(settings.WordProcesses, AdapterKind.WordCom);
        Add(settings.PowerPointProcesses, AdapterKind.PowerPointCom);

        foreach (var (process, kind) in settings.Overrides)
        {
            _map[Normalize(process)] = kind;
        }
    }

    /// <summary>Returns the adapter for a process.</summary>
    /// <param name="processName">Image name, with or without ".exe"; case-insensitive.</param>
    /// <returns>The configured adapter, or <see cref="AdapterKind.None"/> for unknown or null names.</returns>
    public AdapterKind Resolve(string? processName) =>
        processName is not null && _map.TryGetValue(Normalize(processName), out var kind) ? kind : AdapterKind.None;

    private void Add(IEnumerable<string> processes, AdapterKind kind)
    {
        foreach (var process in processes)
        {
            _map[Normalize(process)] = kind;
        }
    }

    private static string Normalize(string processName)
    {
        var name = processName.Trim();
        return name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? name[..^4] : name;
    }
}
