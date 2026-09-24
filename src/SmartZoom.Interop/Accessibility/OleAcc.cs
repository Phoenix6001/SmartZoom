using System.Runtime.InteropServices;

using Windows.Win32.Foundation;
using Windows.Win32.UI.WindowsAndMessaging;

namespace SmartZoom.Interop.Accessibility;

/// <summary>The one <c>oleacc.dll</c> entry point SmartZoom needs, with the object identifiers it asks for.</summary>
/// <remarks>
/// Hand-written rather than generated: CsWin32 hands the object back as a raw <c>void*</c>, and building the
/// runtime-callable wrapper from that by hand is more code and more risk than letting the marshaller do it.
/// </remarks>
internal static class OleAcc
{
    /// <summary><c>OBJID_CLIENT</c>: the accessible object of a window's client area, an MSAA root.</summary>
    public const uint ObjIdClient = unchecked((uint)OBJECT_IDENTIFIER.OBJID_CLIENT);

    /// <summary><c>OBJID_NATIVEOM</c>: a window's native object model; Office answers with its automation object.</summary>
    public const uint ObjIdNativeOm = unchecked((uint)OBJECT_IDENTIFIER.OBJID_NATIVEOM);

    /// <summary><c>IID_IAccessible</c>, the interface an MSAA root is asked for.</summary>
    public static readonly Guid IidIAccessible = new("618736E0-3C3D-11CF-810C-00AA00389B71");

    /// <summary><c>IID_IDispatch</c>, the interface an object model is asked for.</summary>
    public static readonly Guid IidIDispatch = new("00020400-0000-0000-C000-000000000046");

    /// <summary>Asks a window for one of its accessible objects.</summary>
    /// <param name="window">The window.</param>
    /// <param name="objectId"><see cref="ObjIdClient"/> or <see cref="ObjIdNativeOm"/>.</param>
    /// <param name="iid">The interface wanted on the object.</param>
    /// <param name="result">The object as a runtime-callable wrapper, or null when the call fails.</param>
    /// <returns>The HRESULT; 0 on success.</returns>
    [DllImport("oleacc.dll", ExactSpelling = true)]
    public static extern int AccessibleObjectFromWindow(HWND window, uint objectId, in Guid iid, [MarshalAs(UnmanagedType.Interface)] out object? result);
}
