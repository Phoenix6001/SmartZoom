using System.Runtime.InteropServices;

namespace SmartZoom.Interop.Accessibility;

/// <summary>What it means when a call into a browser's accessibility objects throws.</summary>
internal static class AccessibleFailure
{
    /// <summary>Whether the exception means "this node cannot answer now", which every caller recovers from.</summary>
    /// <remarks>
    /// A node fails in one of two ways: it refuses (<see cref="COMException"/>), or it answers with a VARIANT the
    /// runtime cannot convert (<see cref="InvalidCastException"/>, seen from Brave's <c>accHitTest</c>). Both are
    /// the browser's state, not a bug here, and are handled alike; catching only the first let the second end the
    /// press.
    /// </remarks>
    /// <param name="exception">What the call threw.</param>
    internal static bool Is(Exception exception) => exception is COMException or InvalidCastException;
}
