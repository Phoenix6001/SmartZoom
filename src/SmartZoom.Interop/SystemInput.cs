using SmartZoom.Core.Input;

using Windows.Win32;

namespace SmartZoom.Interop;

/// <summary>System-wide input settings, read from Windows.</summary>
public sealed class SystemInput : ISystemInput
{
    /// <inheritdoc />
    public uint DoubleClickTimeMs => PInvoke.GetDoubleClickTime();
}
