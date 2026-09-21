using System.Runtime.InteropServices;

using Microsoft.Extensions.Logging;

using SmartZoom.Core.Zoom.Gesture;

using Windows.Win32;
using Windows.Win32.UI.Input.Pointer;
using Windows.Win32.UI.WindowsAndMessaging;

namespace SmartZoom.Interop.Input;

/// <summary>
/// The two virtual touch devices SmartZoom injects through, and the process-wide state they come with.
/// </summary>
/// <remarks>
/// <para>Touch injection needs no admin rights and no touch hardware, but like all input injection it is
/// blocked by UIPI when the target window is elevated.</para>
/// <para>Which device is used depends on who will read the gesture. Chromium and everything that leaves touch
/// to Windows get <c>InjectTouchInput</c>. Gecko (Firefox) treats every two-finger gesture that comes from
/// that API's <c>\\?\VIRTUAL_DIGITIZER</c> device as a touchpad scroll — its workaround for Synaptics
/// touchpads that emulate touch through the same API, Mozilla bug 1355162 — so a pinch from it never zooms.
/// A device from <c>CreateSyntheticPointerDevice</c> registers under a different name and is handled as a
/// real touch screen.</para>
/// <para>Both are process-wide and live for the rest of the process, like the touch-injection registration
/// itself: Windows offers no way to undo either, and a second registration would fail.</para>
/// </remarks>
public sealed partial class TouchDevices(ILogger<TouchDevices> logger)
{
    private const uint MaxContacts = 2;

    private static readonly Lock InitializationGate = new();
    private static bool? s_initialized;
    private static DestroySyntheticPointerDeviceSafeHandle? s_syntheticDevice;
    private static bool s_syntheticDeviceFailed;

    /// <summary>Makes sure the device this engine needs exists.</summary>
    /// <param name="engine">The recognizer that will read the gesture.</param>
    /// <returns>False when the device could not be created; nothing can be injected then.</returns>
    public bool Ensure(GestureEngine engine) =>
        engine == GestureEngine.Gecko ? EnsureSyntheticDevice() : EnsureInitialized();

    /// <summary>Sends one frame of contacts. Internal: its parameters are Win32 types.</summary>
    /// <param name="contacts">The contacts, with their positions already set.</param>
    /// <param name="flags">Down, update or up.</param>
    /// <param name="engine">The recognizer that will read the gesture; it decides the device.</param>
    /// <returns>False when Windows rejected the frame.</returns>
    internal bool Inject(POINTER_TOUCH_INFO[] contacts, POINTER_FLAGS flags, GestureEngine engine)
    {
        ArgumentNullException.ThrowIfNull(contacts);

        for (var i = 0; i < contacts.Length; i++)
            contacts[i].pointerInfo.pointerFlags = flags;

        var injected = engine == GestureEngine.Gecko ? InjectSynthetic(contacts) : (bool)PInvoke.InjectTouchInput(contacts);
        if (injected)
            return true;

        LogInjectFailed(Marshal.GetLastPInvokeError());
        return false;
    }

    private static bool InjectSynthetic(POINTER_TOUCH_INFO[] contacts)
    {
        var pointers = new POINTER_TYPE_INFO[contacts.Length];
        for (var i = 0; i < contacts.Length; i++)
        {
            pointers[i].type = POINTER_INPUT_TYPE.PT_TOUCH;
            pointers[i].Anonymous.touchInfo = contacts[i];
        }

        return s_syntheticDevice is { } device && PInvoke.InjectSyntheticPointerInput(device, pointers);
    }

    private bool EnsureInitialized()
    {
        lock (InitializationGate)
        {
            if (s_initialized is { } known)
                return known;

            var ok = PInvoke.InitializeTouchInjection(MaxContacts, TOUCH_FEEDBACK_MODE.TOUCH_FEEDBACK_NONE);
            if (!ok)
                LogInitFailed(Marshal.GetLastPInvokeError());

            s_initialized = ok;
            return ok;
        }
    }

    private bool EnsureSyntheticDevice()
    {
        lock (InitializationGate)
        {
            if (s_syntheticDevice is not null)
                return true;

            if (s_syntheticDeviceFailed)
                return false;

            var device = PInvoke.CreateSyntheticPointerDevice_SafeHandle(POINTER_INPUT_TYPE.PT_TOUCH, MaxContacts, POINTER_FEEDBACK_MODE.POINTER_FEEDBACK_NONE);
            if (device.IsInvalid)
            {
                s_syntheticDeviceFailed = true;
                LogSyntheticDeviceFailed(Marshal.GetLastPInvokeError());
                device.Dispose();
                return false;
            }

            s_syntheticDevice = device;
            return true;
        }
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "InitializeTouchInjection failed (Win32 error {Error}); browser smart zoom is unavailable.")]
    private partial void LogInitFailed(int error);

    [LoggerMessage(Level = LogLevel.Error, Message = "CreateSyntheticPointerDevice failed (Win32 error {Error}); smart zoom in Firefox is unavailable.")]
    private partial void LogSyntheticDeviceFailed(int error);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Touch injection failed (Win32 error {Error}).")]
    private partial void LogInjectFailed(int error);
}
