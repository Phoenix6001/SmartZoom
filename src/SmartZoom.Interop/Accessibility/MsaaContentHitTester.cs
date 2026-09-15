using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;
using SmartZoom.Core.Input;
using SmartZoom.Core.Routing;
using SmartZoom.Core.Zoom.Content;
using SmartZoom.Interop.Windows;
using Windows.Win32;
using Windows.Win32.Foundation;
using IAccessible = Accessibility.IAccessible;

namespace SmartZoom.Interop.Accessibility;

/// <summary>
/// Hit-tests web content in Chromium-based browsers (Chrome, Edge, Brave, Opera, Vivaldi, ...) through
/// Microsoft Active Accessibility, the API those browsers expose by default.
/// </summary>
/// <remarks>
/// <para>
/// Chromium builds its accessibility tree lazily. The first <c>WM_GETOBJECT</c> only produces a native root
/// with an empty web-content placeholder; the full DOM tree appears once a client behaves like a screen
/// reader: walks to the root's first child and asks for <c>IAccessible2</c>. <see cref="Wake"/> performs
/// that handshake once per render window.
/// </para>
/// <para>
/// Hit-testing is asynchronous inside Chromium as well: <c>accHitTest</c> may answer from a stale cache
/// while the precise test is in flight, so a page-wide answer is retried briefly.
/// </para>
/// <para>Bounds reported by the tree do not change with visual-viewport (pinch) zoom.</para>
/// </remarks>
public sealed partial class MsaaContentHitTester(ILogger<MsaaContentHitTester> logger) : IContentHitTester
{
    private const string ChromiumRenderWindowClass = "Chrome_RenderWidgetHostHWND";
    private const uint ObjIdClient = 0xFFFFFFFC;
    private const int RoleDocument = 15;
    private const int NavDirFirstChild = 0x7;
    private const int MaxHitTestAttempts = 6;
    private const int HitTestRetryDelayMs = 40;
    private const int MaxChainDepth = 64;

    private static readonly Guid IidIAccessible = new("618736E0-3C3D-11CF-810C-00AA00389B71");
    private static readonly Guid IidIAccessible2 = new("E89F726E-C4F4-4C19-BB19-B647D7FA8478");

    // One accessible root per render window; the tree stays awake for the window's lifetime.
    private readonly ConcurrentDictionary<HWND, IAccessible> _roots = new();

    /// <inheritdoc />
    public Task<ContentHit?> HitTestAsync(TargetInfo target, ScreenPoint point, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(target);

        // COM proxies from oleacc are apartment-agnostic; a pool thread keeps the dispatcher responsive.
        return Task.Run(() => HitTest(target, point, cancellationToken), cancellationToken);
    }

    private ContentHit? HitTest(TargetInfo target, ScreenPoint point, CancellationToken cancellationToken)
    {
        var render = FindRenderWindow(target);
        if (render.IsNull)
            return null;

        try
        {
            var root = GetRoot(render);
            if (root is null)
                return null;

            for (var attempt = 1; ; attempt++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var chain = BuildChain(Descend(root, point));
                var document = chain.FirstOrDefault(n => n.Role == ContentRole.Document);
                if (document is null)
                {
                    LogNoDocument(target.ProcessName);
                    return null;
                }

                // Anything narrower than the document means we're inside real content.
                if (chain.Any(n => n.Bounds.Width < document.Bounds.Width) || attempt == MaxHitTestAttempts)
                    return new ContentHit(chain, document.Bounds);

                Thread.Sleep(HitTestRetryDelayMs);
            }
        }
        catch (COMException ex)
        {
            // The window went away or the browser is busy; treat as "no content" and let the coordinator fall back.
            _roots.TryRemove(render, out _);
            LogComFailure(ex, target.ProcessName);
            return null;
        }
    }

    private static HWND FindRenderWindow(TargetInfo target)
    {
        if (target.HitClassName == ChromiumRenderWindowClass)
            return new HWND(target.HitWindow);

        var found = HWND.Null;
        PInvoke.EnumChildWindows(new HWND(target.RootWindow), (child, _) =>
        {
            if (WindowInspector.GetClassName(child) == ChromiumRenderWindowClass)
            {
                found = child;
                return false;
            }

            return true;
        }, default);

        return found;
    }

    private IAccessible? GetRoot(HWND render)
    {
        if (_roots.TryGetValue(render, out var cached))
            return cached;

        var hr = AccessibleObjectFromWindow(render, ObjIdClient, in IidIAccessible, out var root);
        if (hr != 0 || root is null)
        {
            LogNoAccessibleRoot(hr);
            return null;
        }

        Wake(root);
        _roots[render] = root;
        return root;
    }

    // The screen-reader handshake that makes Chromium serialize the full web-content tree.
    private static void Wake(IAccessible root)
    {
        _ = root.accChildCount;

        if (root is IServiceProvider services)
        {
            var iidService = IidIAccessible;
            var iidIA2 = IidIAccessible2;
            if (services.QueryService(ref iidService, ref iidIA2, out var ia2) == 0 && ia2 != 0)
                Marshal.Release(ia2);
        }

        try
        {
            _ = root.accNavigate(NavDirFirstChild, 0);
        }
        catch (COMException)
        {
            // Some pages have no children yet; the hit-test retry loop copes with that.
        }
    }

    private static (IAccessible Node, int Child) Descend(IAccessible from, ScreenPoint point)
    {
        var current = from;
        for (var guard = 0; guard < MaxChainDepth; guard++)
        {
            object? hit;
            try
            {
                hit = current.accHitTest(point.X, point.Y);
            }
            catch (COMException)
            {
                break;
            }

            if (hit is IAccessible deeper && !ReferenceEquals(deeper, current))
            {
                current = deeper;
                continue;
            }

            return (current, hit is int childId ? childId : 0);
        }

        return (current, 0);
    }

    private static List<ContentNode> BuildChain((IAccessible Node, int Child) leaf)
    {
        var chain = new List<ContentNode>();
        var (current, child) = leaf;

        for (var depth = 0; depth < MaxChainDepth && current is not null; depth++)
        {
            var role = GetRole(current, child);
            chain.Add(new ContentNode(MapRole(role), GetBounds(current, child)));
            if (role == RoleDocument)
                break;

            child = 0;
            try
            {
                current = current.accParent as IAccessible;
            }
            catch (COMException)
            {
                break;
            }
        }

        return chain;
    }

    private static int GetRole(IAccessible node, int child)
    {
        try
        {
            return node.get_accRole(child) is int role ? role : -1;
        }
        catch (COMException)
        {
            return -1;
        }
    }

    private static PixelRect GetBounds(IAccessible node, int child)
    {
        node.accLocation(out var left, out var top, out var width, out var height, child);
        return PixelRect.FromSize(left, top, width, height);
    }

    // MSAA ROLE_SYSTEM_* values. Chromium reports paragraphs and sections as GROUPING.
    private static ContentRole MapRole(int role) => role switch
    {
        RoleDocument => ContentRole.Document,
        20 => ContentRole.Group,      // ROLE_SYSTEM_GROUPING
        41 or 42 => ContentRole.Text, // ROLE_SYSTEM_STATICTEXT, ROLE_SYSTEM_TEXT
        30 => ContentRole.Link,       // ROLE_SYSTEM_LINK
        40 => ContentRole.Image,      // ROLE_SYSTEM_GRAPHIC
        24 => ContentRole.Table,      // ROLE_SYSTEM_TABLE
        33 => ContentRole.List,       // ROLE_SYSTEM_LIST
        34 => ContentRole.ListItem,   // ROLE_SYSTEM_LISTITEM
        _ => ContentRole.Other,
    };

    [DllImport("oleacc.dll", ExactSpelling = true)]
    private static extern int AccessibleObjectFromWindow(HWND hwnd, uint dwId, in Guid riid, [MarshalAs(UnmanagedType.Interface)] out IAccessible? ppvObject);

    [LoggerMessage(Level = LogLevel.Debug, Message = "AccessibleObjectFromWindow failed with 0x{HResult:X8}.")]
    private partial void LogNoAccessibleRoot(int hresult);

    [LoggerMessage(Level = LogLevel.Debug, Message = "No document node on the accessibility path in {Process}.")]
    private partial void LogNoDocument(string? process);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Accessibility call failed in {Process}; dropping the cached root.")]
    private partial void LogComFailure(Exception exception, string? process);

    [ComImport]
    [Guid("6d5140c1-7436-11ce-8034-00aa006009fa")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IServiceProvider
    {
        [PreserveSig]
        int QueryService(ref Guid guidService, ref Guid riid, out nint ppvObject);
    }
}
