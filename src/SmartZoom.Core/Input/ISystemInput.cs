namespace SmartZoom.Core.Input;

/// <summary>System-wide input settings the user configures in Windows rather than in SmartZoom.</summary>
public interface ISystemInput
{
    /// <summary>
    /// The double-click time in milliseconds (Control Panel, Mouse). It is the double-tap window of every
    /// trigger that does not set its own.
    /// </summary>
    uint DoubleClickTimeMs { get; }
}
