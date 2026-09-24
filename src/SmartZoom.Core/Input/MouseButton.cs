namespace SmartZoom.Core.Input;

/// <summary>
/// Buttons that can act as a SmartZoom trigger.
/// </summary>
/// <remarks>
/// <see cref="Left"/> and <see cref="Right"/> are allowed only together with at least one modifier key
/// (see <see cref="MouseButtonTrigger"/>): a global hook that swallows or delays bare primary clicks would
/// make the machine unusable.
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

    /// <summary>Left (primary) button; a trigger only with a modifier key held.</summary>
    Left,

    /// <summary>Right (secondary) button; a trigger only with a modifier key held.</summary>
    Right,
}
