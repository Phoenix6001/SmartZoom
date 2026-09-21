using Microsoft.Extensions.Logging;

using SmartZoom.Core.Settings;

namespace SmartZoom.Core.Routing;

/// <summary>
/// Decides which strategy handles a given application. The table is built from what the registered
/// adapters declare, with the user's <see cref="RoutingSettings.Apps"/> merged on top, so a build can
/// never route an application to a strategy it does not contain.
/// </summary>
public sealed partial class ZoomRouter
{
    private readonly Dictionary<string, AdapterId> _map = new(StringComparer.OrdinalIgnoreCase);
    private readonly ILogger<ZoomRouter> _logger;

    /// <summary>Builds the routing table.</summary>
    /// <param name="adapters">Descriptors of the adapters registered in this build.</param>
    /// <param name="settings">The user's per-application routing.</param>
    /// <param name="logger">Logger; a settings entry naming an unknown strategy is reported here, once, at startup.</param>
    /// <exception cref="InvalidOperationException">Two adapters share an id, or claim the same process by default.</exception>
    public ZoomRouter(IEnumerable<AdapterDescriptor> adapters, RoutingSettings settings, ILogger<ZoomRouter> logger)
    {
        ArgumentNullException.ThrowIfNull(adapters);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(logger);

        _logger = logger;

        var known = new Dictionary<AdapterId, AdapterDescriptor>();
        var claimedBy = new Dictionary<string, AdapterId>(StringComparer.OrdinalIgnoreCase);

        foreach (var adapter in adapters)
        {
            if (!known.TryAdd(adapter.Id, adapter))
            {
                throw new InvalidOperationException(
                    $"Two adapters use the id \"{adapter.Id}\". An id names one strategy in the settings file, so it has to be unique.");
            }

            foreach (var process in adapter.DefaultProcesses)
            {
                var name = Normalize(process);
                if (claimedBy.TryGetValue(name, out var owner))
                {
                    throw new InvalidOperationException(
                        $"\"{owner}\" and \"{adapter.Id}\" both claim \"{process}\" by default. Only one adapter may own a process; "
                        + "if both can handle it, leave it out of one of them and let \"Routing.Apps\" choose.");
                }

                claimedBy[name] = adapter.Id;
                _map[name] = adapter.Id;
            }
        }

        var unknown = new List<string>();
        foreach (var (process, id) in settings.Apps)
        {
            _map[Normalize(process)] = id;

            if (id != AdapterId.None && !known.ContainsKey(id) && !unknown.Contains(id.Value, StringComparer.OrdinalIgnoreCase))
            {
                unknown.Add(id.Value);
            }
        }

        Adapters = [.. known.Values];
        UnknownAdapterIds = unknown;

        if (unknown.Count > 0)
        {
            LogUnknownIds(
                string.Join(", ", unknown),
                string.Join(", ", known.Keys.Select(id => id.Value).Order(StringComparer.OrdinalIgnoreCase).Append(AdapterId.None.Value)));
        }
    }

    /// <summary>The adapters this router knows about, for diagnostics and for a settings UI.</summary>
    public IReadOnlyList<AdapterDescriptor> Adapters { get; }

    /// <summary>Ids named in <see cref="RoutingSettings.Apps"/> that no registered adapter provides.</summary>
    public IReadOnlyList<string> UnknownAdapterIds { get; }

    /// <summary>Returns the strategy for a process.</summary>
    /// <param name="processName">Image name, with or without ".exe"; case-insensitive.</param>
    /// <returns>
    /// The configured id, <see cref="AdapterId.None"/> when the application is deliberately excluded, or
    /// null when nothing routes it — which is the normal case for most of the applications on a machine.
    /// </returns>
    public AdapterId? Resolve(string? processName) =>
        processName is not null && _map.TryGetValue(Normalize(processName), out var id) ? id : null;

    private static string Normalize(string processName)
    {
        var name = processName.Trim();
        return name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? name[..^4] : name;
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Settings name adapters this build does not have: {Unknown}. Those applications fall back to Ctrl+wheel. Available: {Available}.")]
    private partial void LogUnknownIds(string unknown, string available);
}
