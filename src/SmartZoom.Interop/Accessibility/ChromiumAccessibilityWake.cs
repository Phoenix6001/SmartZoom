using System.Collections.Concurrent;
using System.Runtime.InteropServices;

using Interop.UIAutomationClient;

using Microsoft.Extensions.Logging;

using SmartZoom.Core.Input;

using Windows.Win32.Foundation;

using IAccessible = Accessibility.IAccessible;

namespace SmartZoom.Interop.Accessibility;

/// <summary>
/// Gets an accessible root for a browser's render window, and performs the handshake that makes Chromium
/// hand over a real one.
/// </summary>
/// <remarks>
/// <para>
/// Chromium builds its accessibility tree lazily. The first <c>WM_GETOBJECT</c> only produces a native root
/// with an empty web-content placeholder; the full DOM tree appears once a client behaves like a screen
/// reader — walks to the root's first child and asks for <c>IAccessible2</c>. Chromium also watches for UI
/// Automation clients, and in practice a fresh browser process only switches its renderer into full
/// accessibility mode after it has seen one, so a UIA hit-test is part of the handshake too. Without all of
/// it, the first press after a browser starts finds a page-wide empty group and nothing to zoom.
/// </para>
/// <para>
/// Gecko needs none of this: it answers from its top-level window as soon as a client connects.
/// </para>
/// <para>
/// Roots are cached per render window, because the tree stays awake for that window's lifetime. A browser
/// that starts failing is <see cref="Forget"/>ten so the next attempt re-acquires and re-wakes it.
/// </para>
/// </remarks>
public sealed partial class ChromiumAccessibilityWake(ILogger<ChromiumAccessibilityWake> logger)
{
    private const uint ObjIdClient = 0xFFFFFFFC;
    private const int NavDirFirstChild = 0x7;

    private static readonly Guid IidIAccessible = new("618736E0-3C3D-11CF-810C-00AA00389B71");
    private static readonly Guid IidIAccessible2 = new("E89F726E-C4F4-4C19-BB19-B647D7FA8478");

    // One accessible root per render window; the tree stays awake for the window's lifetime.
    private readonly ConcurrentDictionary<HWND, IAccessible> _roots = new();

    // Created lazily on first use; UIA objects are free-threaded, so a single instance is fine.
    private IUIAutomation? _uia;

    /// <summary>The accessible root for a render window, waking it first when it needs waking.</summary>
    /// <param name="render">The render window.</param>
    /// <param name="point">Where the cursor is; the UIA part of the handshake needs a point.</param>
    /// <param name="wake">False for Gecko, whose tree is complete from the first call.</param>
    /// <returns>The root, or null when the window exposes nothing.</returns>
    internal IAccessible? GetRoot(HWND render, ScreenPoint point, bool wake)
    {
        if (_roots.TryGetValue(render, out var cached))
            return cached;

        var hr = AccessibleObjectFromWindow(render, ObjIdClient, in IidIAccessible, out var root);
        if (hr != 0 || root is null)
        {
            LogNoAccessibleRoot(hr);
            return null;
        }

        if (wake)
            Nudge(root, point);

        _roots[render] = root;
        return root;
    }

    /// <summary>Drops the cached root, so the next call acquires and wakes a fresh one.</summary>
    /// <param name="render">The render window.</param>
    internal void Forget(HWND render) => _roots.TryRemove(render, out _);

    /// <summary>Repeats the handshake on a root already acquired: one right after a window appears can be too early.</summary>
    /// <param name="root">The accessible root.</param>
    /// <param name="point">Where the cursor is.</param>
    internal void Nudge(IAccessible root, ScreenPoint point)
    {
        ArgumentNullException.ThrowIfNull(root);

        TouchWithUia(point);

        _ = root.accChildCount;
        QueryAccessible2(root);

        try
        {
            if (root.accNavigate(NavDirFirstChild, 0) is IAccessible first)
            {
                _ = first.accChildCount;
                QueryAccessible2(first);
            }
        }
        catch (COMException)
        {
            // Some pages have no children yet; the caller's retry loop copes with that.
        }
    }

    // The result is irrelevant; the call itself is what Chromium reacts to.
    private void TouchWithUia(ScreenPoint point)
    {
        try
        {
            _uia ??= new CUIAutomation();
            _ = _uia.ElementFromPoint(new tagPOINT { x = point.X, y = point.Y });
        }
        catch (COMException)
        {
            // UIA is best-effort here; the MSAA handshake still runs.
        }
    }

    private static void QueryAccessible2(IAccessible node)
    {
        if (node is not IServiceProvider services)
            return;

        var iidService = IidIAccessible;
        var iidIA2 = IidIAccessible2;
        if (services.QueryService(ref iidService, ref iidIA2, out var ia2) == 0 && ia2 != 0)
            Marshal.Release(ia2);
    }

    [DllImport("oleacc.dll", ExactSpelling = true)]
    private static extern int AccessibleObjectFromWindow(HWND hwnd, uint dwId, in Guid riid, [MarshalAs(UnmanagedType.Interface)] out IAccessible? ppvObject);

    [LoggerMessage(Level = LogLevel.Debug, Message = "AccessibleObjectFromWindow failed with 0x{HResult:X8}.")]
    private partial void LogNoAccessibleRoot(int hresult);

    [ComImport]
    [Guid("6d5140c1-7436-11ce-8034-00aa006009fa")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IServiceProvider
    {
        [PreserveSig]
        int QueryService(ref Guid guidService, ref Guid riid, out nint ppvObject);
    }
}
