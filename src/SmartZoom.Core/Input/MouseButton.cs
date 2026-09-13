namespace SmartZoom.Core.Input;

/// <summary>
/// Buttons that can act as a SmartZoom trigger.
/// </summary>
/// <remarks>
/// Left and Right are deliberately absent: a global hook that delays or swallows primary clicks
/// would break ordinary use of the machine.
/// </remarks>
public enum MouseButton : byte
{
    /// <summary>No button; not a valid trigger.</summary>
    None = 0,

    /// <summary>Middle button (wheel click).</summary>
    Middle,

    /// <summary>First extended button, usually "Back".</summary>
    XButton1,

    /// <summary>Second extended button, usually "Forward".</summary>
    XButton2,
}
