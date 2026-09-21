using SmartZoom.Core.Input;
using SmartZoom.Core.Routing;
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

    // Largest window that may be treated as a cursor decoration rather than a target. Acrobat parks a 5x5
    // layered window under the pointer once it has seen touch input, which would otherwise become the target
    // of the next trigger and hide the document underneath it.
    private const int MaxDecorationSize = 8;

    // How far down the z-order to look for the window a decoration is sitting on, and how deep into it.
    // The budget counts windows that are actually on the screen: the top of a real desktop's z-order is a long
    // run of hidden top-level windows (measured here: 64 of them above the reader the decoration belonged to,
    // only 5 of which were visible), so counting every handle would give up long before the target.
    private const int MaxWindowsBehind = 16;
    private const int MaxChildDepth = 8;

    // An absolute bound on the walk, so a desktop with thousands of hidden windows cannot make a trigger slow.
    private const int MaxZOrderSteps = 512;

    /// <inheritdoc />
    public unsafe TargetInfo? GetTargetAt(ScreenPoint point)
    {
        var hit = WindowUnder(point);
        if (hit.IsNull)
        {
            return null;
        }

        // GA_ROOTOWNER walks both the parent chain and the owner chain, so a dropdown or dialog resolves
        // to the application window that owns it. That window is the stable identity for toggle state.
        var root = PInvoke.GetAncestor(hit, GET_ANCESTOR_FLAGS.GA_ROOTOWNER);
        if (root.IsNull)
        {
            root = hit;
        }

        _ = PInvoke.GetWindowThreadProcessId(root, out var processId);

        return new TargetInfo(
            RootWindow: (nint)root.Value,
            HitWindow: (nint)hit.Value,
            ProcessId: processId,
            ProcessName: TryGetProcessName(processId),
            RootClassName: GetClassName(root),
            HitClassName: GetClassName(hit));
    }

    /// <summary>The window under a point, looking past any cursor decoration parked on top of it.</summary>
    internal static HWND WindowAt(ScreenPoint point) => WindowUnder(point);

    /// <summary>The window under a point, looking past any cursor decoration parked on top of it.</summary>
    /// <remarks>
    /// Walking the z-order rather than hiding the decoration: it belongs to another application, and a window
    /// this one hides and shows again is a window whose owner may be mid-paint, mid-animation or watching for
    /// exactly that. The walk only reads.
    /// </remarks>
    private static HWND WindowUnder(ScreenPoint point)
    {
        var hit = PInvoke.WindowFromPoint(new System.Drawing.Point(point.X, point.Y));
        if (hit.IsNull || !IsDecoration(hit))
        {
            return hit;
        }

        var next = PInvoke.GetAncestor(hit, GET_ANCESTOR_FLAGS.GA_ROOT);
        var considered = 0;
        for (var step = 0; step < MaxZOrderSteps && considered < MaxWindowsBehind; step++)
        {
            next = PInvoke.GetWindow(next, GET_WINDOW_CMD.GW_HWNDNEXT);
            if (next.IsNull)
            {
                return hit;
            }

            if (!PInvoke.IsWindowVisible(next))
            {
                continue;   // hidden windows keep their place in the z-order; they are not what the cursor is on
            }

            considered++;
            if (!IsDecoration(next) && Covers(next, point))
            {
                return Deepest(next, point);
            }
        }

        return hit;
    }

    /// <summary>Whether a window's rectangle contains a point.</summary>
    private static bool Covers(HWND window, ScreenPoint point) =>
        PInvoke.GetWindowRect(window, out var rect)
        && point.X >= rect.left && point.X < rect.right
        && point.Y >= rect.top && point.Y < rect.bottom;

    /// <summary>The innermost child of a window at a point, which is what an adapter needs to talk to.</summary>
    private static HWND Deepest(HWND parent, ScreenPoint point)
    {
        for (var depth = 0; depth < MaxChildDepth; depth++)
        {
            var client = new System.Drawing.Point(point.X, point.Y);
            if (!PInvoke.ScreenToClient(parent, ref client))
            {
                return parent;
            }

            var child = PInvoke.ChildWindowFromPointEx(parent, client, CWP_FLAGS.CWP_SKIPINVISIBLE | CWP_FLAGS.CWP_SKIPTRANSPARENT);
            if (child.IsNull || child == parent)
            {
                return parent;
            }

            parent = child;
        }

        return parent;
    }

    /// <summary>A window too small to hold content, kept above the rest: a pointer decoration, not a target.</summary>
    private static bool IsDecoration(HWND window)
    {
        if (!PInvoke.GetWindowRect(window, out var rect))
        {
            return false;
        }

        if (rect.right - rect.left > MaxDecorationSize || rect.bottom - rect.top > MaxDecorationSize)
        {
            return false;
        }

        const int Layered = 0x0008_0000;
        const int ToolWindow = 0x0000_0080;
        const int Transparent = 0x0000_0020;

        var styles = PInvoke.GetWindowLong(window, WINDOW_LONG_PTR_INDEX.GWL_EXSTYLE);
        return (styles & (Layered | ToolWindow | Transparent)) != 0;
    }

    /// <inheritdoc />
    public bool IsWindowAlive(nint window, uint processId)
    {
        var hwnd = new HWND(window);
        if (!PInvoke.IsWindow(hwnd))
        {
            return false;
        }

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
        {
            return null;
        }

        try
        {
            var buffer = stackalloc char[MaxImagePathLength];
            var size = (uint)MaxImagePathLength;
            if (!PInvoke.QueryFullProcessImageName(process, PROCESS_NAME_FORMAT.PROCESS_NAME_WIN32, new PWSTR(buffer), &size))
            {
                return null;
            }

            return Path.GetFileNameWithoutExtension(new ReadOnlySpan<char>(buffer, (int)size)).ToString();
        }
        finally
        {
            PInvoke.CloseHandle(process);
        }
    }
}
