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
/// Hit-tests web content in Chromium-based browsers (Chrome, Edge, Brave, Opera, Vivaldi, ...) and in
/// Firefox through Microsoft Active Accessibility, the API those browsers expose by default.
/// </summary>
/// <remarks>
/// <para>
/// Gecko (Firefox) answers <c>accHitTest</c> from its top-level <c>MozillaWindowClass</c> window as soon as a
/// client connects, and reports paragraphs and sections with IAccessible2 roles (see <see cref="AccessibleRoles"/>).
/// </para>
/// <para>
/// Chromium builds its accessibility tree lazily, and only hands over a real one to a client that behaves
/// like a screen reader. <see cref="ChromiumAccessibilityWake"/> is that handshake and the root cache.
/// </para>
/// <para>
/// Hit-testing is asynchronous inside Chromium as well: <c>accHitTest</c> may answer from a stale cache
/// while the precise test is in flight, so a page-wide answer is retried briefly.
/// </para>
/// <para>Bounds reported by the tree do not change with visual-viewport (pinch) zoom.</para>
/// </remarks>
public sealed partial class MsaaContentHitTester(ChromiumAccessibilityWake wake, ILogger<MsaaContentHitTester> logger) : IContentHitTester
{
    private const int MaxHitTestAttempts = 12;
    private const int HitTestRetryDelayMs = 50;
    private const int MaxChainDepth = 64;

    /// <inheritdoc />
    public Task<ContentHit?> HitTestAsync(TargetInfo target, ScreenPoint point, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(target);

        // COM proxies from oleacc are apartment-agnostic; a pool thread keeps the dispatcher responsive.
        return Task.Run(() => HitTest(target, point, cancellationToken), cancellationToken);
    }

    private ContentHit? HitTest(TargetInfo target, ScreenPoint point, CancellationToken cancellationToken)
    {
        var gecko = BrowserWindows.IsGecko(target.RootClassName);
        var render = FindRenderWindow(target, gecko);
        if (render.IsNull)
            return null;

        try
        {
            // Gecko's tree is complete from the first call; the screen-reader handshake is Chromium's need.
            var root = wake.GetRoot(render, point, wake: !gecko);
            if (root is null)
                return null;

            // Where to ask the tree about; differs from the cursor only while the tree reports a stale page zoom.
            var query = point;

            for (var attempt = 1; ; attempt++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                try
                {
                    // accHitTest sometimes stops at the document even when the tree is awake (points between
                    // elements, some layouts); walking the children by rectangle finds the enclosing block then.
                    var chain = BuildChain(DeepenByBounds(Descend(root, query), query));
                    var document = chain.FirstOrDefault(n => n.Role == ContentRole.Document);

                    // A Chromium tree left at the scale of an earlier pinch answers in its own, zoomed coordinates
                    // (see StalePageZoom): ask again where it believes the cursor is, then translate its answer back.
                    var stale = document is null || gecko ? null : StalePageZoom.Detect(document.Bounds, WindowRect(render, document.Bounds));
                    var expectedQuery = stale?.ToReported(point) ?? point;
                    if (expectedQuery != query && attempt < MaxHitTestAttempts)
                    {
                        query = expectedQuery;
                        continue;
                    }

                    if (stale is { } zoom)
                    {
                        LogStaleZoom(target.ProcessName, zoom.Scale);
                        chain = [.. chain.Select(n => n with { Bounds = zoom.ToActual(n.Bounds) })];
                        document = chain.First(n => n.Role == ContentRole.Document);
                    }

                    // Anything narrower than the document means we're inside real content. Right after the
                    // wake-up the path may instead end above the document or stop at a page-wide placeholder,
                    // because the renderer serializes the tree asynchronously; give it a few frames.
                    if (document is not null && chain.Any(n => n.Bounds.Width < document.Bounds.Width))
                    {
                        // The hit-test itself is fresh, but reported rectangles lag behind scrolling. A leaf whose
                        // rectangle doesn't contain the point is stale: keep asking until the tree has caught up.
                        if (chain[0].Bounds.Contains(point))
                            return new ContentHit(chain, Viewport(document.Bounds, render));

                        if (attempt == MaxHitTestAttempts)
                        {
                            LogStaleBounds(target.ProcessName, chain[0].Bounds.Left, chain[0].Bounds.Top, point.X, point.Y);
                            return null;
                        }

                        Thread.Sleep(HitTestRetryDelayMs);
                        continue;
                    }

                    if (attempt == MaxHitTestAttempts)
                    {
                        // Nothing usable after ~600 ms: forget this root so the next trigger re-acquires and re-wakes it.
                        wake.Forget(render);
                        if (document is null)
                        {
                            LogNoDocument(target.ProcessName);
                            return null;
                        }

                        return new ContentHit(chain, Viewport(document.Bounds, render));
                    }

                    // A single handshake right after the window appeared can be too early; nudging again is cheap.
                    if (!gecko)
                        wake.Nudge(root, point);

                    Thread.Sleep(HitTestRetryDelayMs);
                }
                catch (COMException ex) when (attempt < MaxHitTestAttempts)
                {
                    // A browser whose accessibility tree is still being built answers with an error rather than
                    // an empty tree, and the user's very first press paid for it. Forget the root, take another
                    // handshake and keep trying; only the last attempt gives up.
                    LogComRetry(ex, target.ProcessName);
                    wake.Forget(render);
                    Thread.Sleep(HitTestRetryDelayMs);

                    var reacquired = wake.GetRoot(render, point, wake: !gecko);
                    if (reacquired is null)
                        return null;

                    root = reacquired;
                    query = point;
                }
            }
        }
        catch (COMException ex)
        {
            // The window went away or the browser is busy; treat as "no content" and let the coordinator fall back.
            wake.Forget(render);
            LogComFailure(ex, target.ProcessName);
            return null;
        }
    }

    // The document's reported bounds are normally the visible page area, but some Chromium documents (sidebar
    // WebUI, background frames) report absurd rectangles. The render window's own rectangle is always right,
    // so the viewport is their intersection, or the window rectangle alone when the two don't overlap.
    private static PixelRect Viewport(PixelRect document, HWND render)
    {
        var window = WindowRect(render, document);
        var intersection = new PixelRect(
            Math.Max(document.Left, window.Left),
            Math.Max(document.Top, window.Top),
            Math.Min(document.Right, window.Right),
            Math.Min(document.Bottom, window.Bottom));

        return intersection.IsEmpty ? window : intersection;
    }

    private static PixelRect WindowRect(HWND window, PixelRect fallback) =>
        PInvoke.GetWindowRect(window, out var rect) ? new PixelRect(rect.left, rect.top, rect.right, rect.bottom) : fallback;

    // The window whose accessible root exposes the page: Chromium's render widget, or Gecko's top-level window
    // (its content is drawn by a disabled child window that has no accessible tree of its own).
    private static HWND FindRenderWindow(TargetInfo target, bool gecko)
    {
        if (gecko)
            return new HWND(target.RootWindow);

        if (target.HitClassName == BrowserWindows.ChromiumRenderWindowClass)
            return new HWND(target.HitWindow);

        var found = HWND.Null;
        PInvoke.EnumChildWindows(new HWND(target.RootWindow), (child, _) =>
        {
            if (WindowInspector.GetClassName(child) == BrowserWindows.ChromiumRenderWindowClass)
            {
                found = child;
                return false;
            }

            return true;
        }, default);

        return found;
    }

    // Descends from a node into whichever child's rectangle contains the point, as far as that goes.
    // Bounded so a huge sibling list can't stall the hook's dispatcher.
    private static (IAccessible Node, int Child) DeepenByBounds((IAccessible Node, int Child) from, ScreenPoint point)
    {
        const int maxVisits = 400;
        if (from.Child != 0)
            return from;

        var current = from.Node;
        var visited = 0;
        for (var depth = 0; depth < MaxChainDepth; depth++)
        {
            int count;
            try
            {
                count = current.accChildCount;
            }
            catch (COMException)
            {
                break;
            }

            IAccessible? next = null;
            var nextChild = 0;
            for (var i = 1; i <= count && visited < maxVisits; i++)
            {
                visited++;
                object? child;
                try
                {
                    child = current.get_accChild(i);
                }
                catch (COMException)
                {
                    continue;
                }

                if (child is IAccessible node)
                {
                    if (TryGetBounds(node, 0, out var bounds) && bounds.Contains(point))
                    {
                        next = node;
                        break;
                    }
                }
                else if (child is int simpleId && TryGetBounds(current, simpleId, out var simpleBounds) && simpleBounds.Contains(point))
                {
                    nextChild = simpleId;
                    break;
                }
            }

            if (next is null)
                return (current, nextChild);

            current = next;
        }

        return (current, 0);
    }

    private static bool TryGetBounds(IAccessible node, int child, out PixelRect bounds)
    {
        try
        {
            bounds = GetBounds(node, child);
            return !bounds.IsEmpty;
        }
        catch (COMException)
        {
            bounds = default;
            return false;
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

    /// <summary>
    /// The path from the element under the cursor out to the page, with every nested document above the
    /// leaf turned into an ordinary container.
    /// </summary>
    /// <remarks>
    /// A page assembled from iframes — a news site with embedded cards and advertisements is the usual case —
    /// has a document at every frame boundary. Stopping at the first one makes an advertisement's 656 px
    /// frame "the page": the viewport becomes the frame, the real containers above it are never seen, and a
    /// paragraph that fills its little frame is rejected for being wider than 90% of "the viewport". The
    /// press then does nothing at all. Only the outermost document is the page; the rest are boxes on it.
    /// </remarks>
    private static List<ContentNode> BuildChain((IAccessible Node, int Child) leaf)
    {
        var chain = new List<ContentNode>();
        var documents = new List<int>();
        var (current, child) = leaf;

        for (var depth = 0; depth < MaxChainDepth && current is not null; depth++)
        {
            var role = GetRole(current, child);
            if (role == AccessibleRoles.Document)
                documents.Add(chain.Count);

            chain.Add(new ContentNode(AccessibleRoles.Map(role), GetBounds(current, child), role));

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

        if (documents.Count == 0)
            return chain;

        // Everything above the outermost document is browser chrome, and every document below it is a frame.
        var page = documents[^1];
        for (var i = 0; i < page; i++)
        {
            if (chain[i].Role == ContentRole.Document)
                chain[i] = chain[i] with { Role = ContentRole.Group };
        }

        chain.RemoveRange(page + 1, chain.Count - page - 1);
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

    [LoggerMessage(Level = LogLevel.Debug, Message = "No document node on the accessibility path in {Process}.")]
    private partial void LogNoDocument(string? process);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Accessibility bounds in {Process} are stale (element at ({Left}, {Top}) does not contain the cursor at ({X}, {Y})); skipping this press rather than zooming the wrong place.")]
    private partial void LogStaleBounds(string? process, int left, int top, int x, int y);

    [LoggerMessage(Level = LogLevel.Debug, Message = "The accessibility tree of {Process} still reports the page pinch-zoomed x{Scale:F3}; translating its coordinates.")]
    private partial void LogStaleZoom(string? process, double scale);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Accessibility call failed in {Process}; re-acquiring the tree and trying again.")]
    private partial void LogComRetry(Exception error, string? process);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Accessibility call failed in {Process}; dropping the cached root.")]
    private partial void LogComFailure(Exception exception, string? process);

}
