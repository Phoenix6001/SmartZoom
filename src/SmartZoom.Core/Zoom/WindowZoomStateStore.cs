using SmartZoom.Core.Routing;

namespace SmartZoom.Core.Zoom;

/// <summary>Remembers which top-level windows are currently zoomed and how to undo it.</summary>
/// <remarks>
/// Windows recycles HWND values, so an entry is only valid while the same process still owns the
/// handle. <see cref="Prune"/> drops entries for windows that have gone away; the coordinator calls
/// it on every trigger, which is cheap because only a handful of windows are ever zoomed at once.
/// </remarks>
public sealed class WindowZoomStateStore
{
    private readonly Dictionary<nint, Entry> _entries = [];
    private readonly Lock _gate = new();

    /// <summary>Number of windows currently recorded as zoomed.</summary>
    public int Count
    {
        get
        {
            lock (_gate)
            {
                return _entries.Count;
            }
        }
    }

    /// <summary>Records that <paramref name="target"/>'s root window was zoomed by <paramref name="adapter"/>.</summary>
    public void Save(TargetInfo target, AdapterId adapter, object restoreState)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(restoreState);

        lock (_gate)
        {
            _entries[target.RootWindow] = new Entry(target.ProcessId, adapter, restoreState);
        }
    }

    /// <summary>Removes and returns the saved state for <paramref name="target"/>'s root window, if the same process still owns it.</summary>
    public bool TryTake(TargetInfo target, out AdapterId adapter, out object restoreState)
    {
        ArgumentNullException.ThrowIfNull(target);

        lock (_gate)
        {
            if (_entries.Remove(target.RootWindow, out var entry) && entry.ProcessId == target.ProcessId)
            {
                adapter = entry.Adapter;
                restoreState = entry.RestoreState;
                return true;
            }
        }

        adapter = AdapterId.None;
        restoreState = null!;
        return false;
    }

    /// <summary>Drops entries whose window no longer exists or now belongs to a different process.</summary>
    /// <param name="isAlive">Returns true if the window handle still refers to a window owned by the given process.</param>
    /// <returns>Number of entries removed.</returns>
    public int Prune(Func<nint, uint, bool> isAlive)
    {
        ArgumentNullException.ThrowIfNull(isAlive);

        lock (_gate)
        {
            var removed = 0;
            foreach (var (window, entry) in _entries)
            {
                if (!isAlive(window, entry.ProcessId) && _entries.Remove(window))
                {
                    removed++;
                }
            }

            return removed;
        }
    }

    private sealed record Entry(uint ProcessId, AdapterId Adapter, object RestoreState);
}
