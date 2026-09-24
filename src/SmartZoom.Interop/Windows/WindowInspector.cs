using SmartZoom.Core.Input;
using SmartZoom.Core.Routing;
using SmartZoom.Core.Windows;
using SmartZoom.Core.Zoom.Content;

using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.System.Threading;
using Windows.Win32.UI.WindowsAndMessaging;

namespace SmartZoom.Interop.Windows;

/// <summary>Resolves the window and process under a screen point using user32/kernel32.</summary>
/// <remarks>
/// Requires the process to be Per-Monitor V2 DPI aware. Otherwise WindowFromPoint interprets the
/// physical coordinates from the hook as DPI-virtualized ones and hits the wrong window on scaled monitors.
/// </remarks>
public sealed class WindowInspector : IWindowInspector
{
    // Window class names are limited to 256 characters.
    private const int MaxClassNameLength = 256;

    // Extended-length paths can exceed MAX_PATH; 1024 comfortably covers real-world install locations.
    private const int MaxImagePathLength = 1024;

    /// <inheritdoc />
    public TargetInfo? GetTargetAt(ScreenPoint point)
    {
        var hit = WindowUnder(point);
        if (hit.IsNull)
            return null;

        // GA_ROOTOWNER walks both the parent chain and the owner chain, so a dropdown or dialog resolves
        // to the application window that owns it. That window is the stable identity for toggle state.
        var root = PInvoke.GetAncestor(hit, GET_ANCESTOR_FLAGS.GA_ROOTOWNER);
        if (root.IsNull)
            root = hit;

        _ = PInvoke.GetWindowThreadProcessId(root, out var processId);

        return new TargetInfo(
            RootWindow: (nint)root,
            HitWindow: (nint)hit,
            ProcessId: processId,
            ProcessName: TryGetProcessName(processId),
            RootClassName: GetClassName(root),
            HitClassName: GetClassName(hit));
    }

    /// <summary>The window under a point, looking past any cursor decoration parked on top of it.</summary>
    /// <remarks>The rules are <see cref="DecorationPolicy"/>; this is the walk that applies them.</remarks>
    internal static HWND WindowUnder(ScreenPoint point)
    {
        var hit = PInvoke.WindowFromPoint(new System.Drawing.Point(point.X, point.Y));
        if (hit.IsNull || !IsDecoration(hit))
            return hit;

        var next = PInvoke.GetAncestor(hit, GET_ANCESTOR_FLAGS.GA_ROOT);
        var considered = 0;
        for (var step = 0; step < DecorationPolicy.MaxZOrderSteps && considered < DecorationPolicy.MaxWindowsBehind; step++)
        {
            next = PInvoke.GetWindow(next, GET_WINDOW_CMD.GW_HWNDNEXT);
            if (next.IsNull)
                return hit;

            if (!PInvoke.IsWindowVisible(next))
                continue;   // hidden windows keep their place in the z-order; they are not what the cursor is on

            considered++;
            if (!IsDecoration(next) && Covers(next, point))
                return Deepest(next, point);
        }

        return hit;
    }

    /// <summary>The first descendant of a window with a given class, or <see cref="HWND.Null"/>.</summary>
    /// <remarks><c>EnumChildWindows</c> walks the whole tree, so a grandchild is found as readily as a child.</remarks>
    /// <param name="root">The window whose descendants are searched.</param>
    /// <param name="className">The window class wanted, compared ordinally.</param>
    internal static HWND FindChildByClass(HWND root, string className)
    {
        var found = HWND.Null;
        PInvoke.EnumChildWindows(root, (child, _) =>
        {
            if (GetClassName(child) != className)
                return true;

            found = child;
            return false;
        }, default);

        return found;
    }

    /// <summary>A window's rectangle in screen pixels, or null when Windows reports none (the window is gone).</summary>
    /// <param name="window">The window.</param>
    internal static PixelRect? Bounds(HWND window) =>
        PInvoke.GetWindowRect(window, out var rect) ? new PixelRect(rect.left, rect.top, rect.right, rect.bottom) : null;

    /// <summary>Whether a window's rectangle contains a point.</summary>
    private static bool Covers(HWND window, ScreenPoint point) =>
        Bounds(window) is { } bounds && DecorationPolicy.Covers(bounds, point);

    /// <summary>The innermost child of a window at a point, which is what an adapter needs to talk to.</summary>
    private static HWND Deepest(HWND parent, ScreenPoint point)
    {
        for (var depth = 0; depth < DecorationPolicy.MaxChildDepth; depth++)
        {
            var client = new System.Drawing.Point(point.X, point.Y);
            if (!PInvoke.ScreenToClient(parent, ref client))
                return parent;

            var child = PInvoke.ChildWindowFromPointEx(parent, client, CWP_FLAGS.CWP_SKIPINVISIBLE | CWP_FLAGS.CWP_SKIPTRANSPARENT);
            if (child.IsNull || child == parent)
                return parent;

            parent = child;
        }

        return parent;
    }

    /// <summary>A window too small to hold content, kept above the rest: a pointer decoration, not a target.</summary>
    private static bool IsDecoration(HWND window)
    {
        if (Bounds(window) is not { } bounds)
            return false;

        const int DecorationMask = (int)(DecorationPolicy.DecorationStyles.Layered
            | DecorationPolicy.DecorationStyles.ToolWindow
            | DecorationPolicy.DecorationStyles.Transparent);

        var styles = PInvoke.GetWindowLong(window, WINDOW_LONG_PTR_INDEX.GWL_EXSTYLE) & DecorationMask;
        return DecorationPolicy.IsDecoration(bounds, (DecorationPolicy.DecorationStyles)styles);
    }

    /// <inheritdoc />
    public bool IsWindowAlive(nint window, uint processId)
    {
        var hwnd = new HWND(window);
        if (!PInvoke.IsWindow(hwnd))
            return false;

        _ = PInvoke.GetWindowThreadProcessId(hwnd, out var owner);
        return owner == processId;
    }

    internal static string GetClassName(HWND window)
    {
        Span<char> buffer = stackalloc char[MaxClassNameLength + 1];
        var length = PInvoke.GetClassName(window, buffer);
        return length > 0 ? new string(buffer[..length]) : string.Empty;
    }

    private static unsafe string? TryGetProcessName(uint processId)
    {
        // PROCESS_QUERY_LIMITED_INFORMATION is granted even for elevated processes, so we can still
        // identify (and log) targets we won't be able to send input to.
        var process = PInvoke.OpenProcess(PROCESS_ACCESS_RIGHTS.PROCESS_QUERY_LIMITED_INFORMATION, false, processId);
        if (process.IsNull)
            return null;

        try
        {
            var buffer = stackalloc char[MaxImagePathLength];
            var size = (uint)MaxImagePathLength;
            if (!PInvoke.QueryFullProcessImageName(process, PROCESS_NAME_FORMAT.PROCESS_NAME_WIN32, new PWSTR(buffer), &size))
                return null;

            return Path.GetFileNameWithoutExtension(new ReadOnlySpan<char>(buffer, (int)size)).ToString();
        }
        finally
        {
            PInvoke.CloseHandle(process);
        }
    }
}
