using Microsoft.Extensions.Logging;

using SmartZoom.Core.Input;
using SmartZoom.Core.Routing;
using SmartZoom.Core.Zoom.Content;
using SmartZoom.Interop.Windows;

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
/// This class only asks. What each answer means - when to retry, when an answer is stale, which document is
/// the page, when the window stands in for it - is <see cref="HitTestPolicy"/>'s, in Core, where every answer
/// a browser has been seen to give is a unit test.
/// </para>
/// <para>Bounds reported by the tree do not change with visual-viewport (pinch) zoom.</para>
/// </remarks>
public sealed partial class MsaaContentHitTester(ChromiumAccessibilityWake wake, ILogger<MsaaContentHitTester> logger) : IContentHitTester
{
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

            var policy = new HitTestPolicy(point, gecko, target.ProcessName, logger);
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var step = policy.Next(Ask(root, render, policy.Query));
                if (step.ForgetRoot)
                    wake.Forget(render);

                switch (step.Action)
                {
                    case HitTestAction.Done:
                        return step.Hit;

                    case HitTestAction.AskAgain:
                        continue;

                    case HitTestAction.Reacquire:
                        Thread.Sleep(HitTestPolicy.RetryDelay);
                        root = wake.GetRoot(render, point, wake: !gecko);
                        if (root is null)
                            return null;

                        continue;

                    default:
                        if (step.Nudge)
                            wake.Nudge(root, point);

                        Thread.Sleep(HitTestPolicy.RetryDelay);
                        continue;
                }
            }
        }
        catch (Exception ex) when (AccessibleFailure.Is(ex))
        {
            // Taking the root itself failed: the window went away or the browser is busy. Treat it as "no
            // content" and let the coordinator fall back.
            wake.Forget(render);
            LogComFailure(ex, target.ProcessName);
            return null;
        }
    }

    /// <summary>Asks the tree once about a point, and reports what it said without judging it.</summary>
    /// <param name="root">The accessible root.</param>
    /// <param name="render">The render window.</param>
    /// <param name="query">Where to ask.</param>
    /// <returns>The path as reported, with the render window's rectangle; or the failure, when the tree threw.</returns>
    private static HitTestAnswer Ask(IAccessible root, HWND render, ScreenPoint query)
    {
        try
        {
            // accHitTest sometimes stops at the document even when the tree is awake (points between
            // elements, some layouts); walking the children by rectangle finds the enclosing block then.
            var path = ReadPath(DeepenByBounds(Descend(root, query), query));
            return new HitTestAnswer(path, WindowInspector.Bounds(render));
        }
        catch (Exception ex) when (AccessibleFailure.Is(ex))
        {
            return HitTestAnswer.Refused(ex);
        }
    }

    // The window whose accessible root exposes the page: Chromium's render widget, or Gecko's top-level window
    // (its content is drawn by a disabled child window that has no accessible tree of its own).
    private static HWND FindRenderWindow(TargetInfo target, bool gecko)
    {
        if (gecko)
            return new HWND(target.RootWindow);

        if (target.HitClassName == BrowserWindows.ChromiumRenderWindowClass)
            return new HWND(target.HitWindow);

        return WindowInspector.FindChildByClass(new HWND(target.RootWindow), BrowserWindows.ChromiumRenderWindowClass);
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
            catch (Exception ex) when (AccessibleFailure.Is(ex))
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
                catch (Exception ex) when (AccessibleFailure.Is(ex))
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
        catch (Exception ex) when (AccessibleFailure.Is(ex))
        {
            bounds = default;
            return false;
        }
    }

    internal static (IAccessible Node, int Child) Descend(IAccessible from, ScreenPoint point)
    {
        var current = from;
        for (var guard = 0; guard < MaxChainDepth; guard++)
        {
            object? hit;
            try
            {
                hit = current.accHitTest(point.X, point.Y);
            }
            catch (Exception ex) when (AccessibleFailure.Is(ex))
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

    /// <summary>The nodes from the leaf out to the tree's root, as reported; <see cref="ContentPath.ToPage"/> finds the page.</summary>
    private static List<ContentNode> ReadPath((IAccessible Node, int Child) leaf)
    {
        var path = new List<ContentNode>();
        var (current, child) = leaf;

        for (var depth = 0; depth < MaxChainDepth && current is not null; depth++)
        {
            var role = GetRole(current, child);
            path.Add(new ContentNode(AccessibleRoles.Map(role), GetBounds(current, child), role));

            child = 0;
            try
            {
                current = current.accParent as IAccessible;
            }
            catch (Exception ex) when (AccessibleFailure.Is(ex))
            {
                break;
            }
        }

        return path;
    }

    private static int GetRole(IAccessible node, int child)
    {
        try
        {
            return node.get_accRole(child) is int role ? role : -1;
        }
        catch (Exception ex) when (AccessibleFailure.Is(ex))
        {
            return -1;
        }
    }

    private static PixelRect GetBounds(IAccessible node, int child)
    {
        node.accLocation(out var left, out var top, out var width, out var height, child);
        return PixelRect.FromSize(left, top, width, height);
    }

    [LoggerMessage(Level = LogLevel.Debug, Message = "Accessibility call failed in {Process}; dropping the cached root.")]
    private partial void LogComFailure(Exception exception, string? process);
}
