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

    /// <inheritdoc />
    public unsafe TargetInfo? GetTargetAt(ScreenPoint point)
    {
        var hit = PInvoke.WindowFromPoint(new System.Drawing.Point(point.X, point.Y));
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

    private static string GetClassName(HWND window)
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
