using SmartZoom.Core.Input;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.WindowsAndMessaging;

namespace SmartZoom.Interop.Windows;

/// <summary>Window classes that identify the browser engine behind a window.</summary>
internal static class BrowserWindows
{
    /// <summary>The window Chromium renders web content into (Chrome, Edge, Brave, Opera, Vivaldi, ...).</summary>
    public const string ChromiumRenderWindowClass = "Chrome_RenderWidgetHostHWND";

    /// <summary>Gecko's top-level window (Firefox); content is drawn by a disabled child, so hit-tests land on the root.</summary>
    public const string GeckoWindowClass = "MozillaWindowClass";

    /// <summary>Whether a window class belongs to a Gecko (Firefox) top-level window.</summary>
    public static bool IsGecko(string className) => string.Equals(className, GeckoWindowClass, StringComparison.Ordinal);

    /// <summary>Whether the top-level window under a screen point belongs to Gecko (Firefox).</summary>
    public static bool IsGeckoWindowAt(ScreenPoint point)
    {
        var window = PInvoke.WindowFromPoint(new System.Drawing.Point(point.X, point.Y));
        if (window.IsNull)
            return false;

        var root = PInvoke.GetAncestor(window, GET_ANCESTOR_FLAGS.GA_ROOT);
        return IsGecko(WindowInspector.GetClassName(root.IsNull ? window : root));
    }
}
