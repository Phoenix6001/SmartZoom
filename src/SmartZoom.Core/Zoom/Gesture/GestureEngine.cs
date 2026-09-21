namespace SmartZoom.Core.Zoom.Gesture;

/// <summary>Which recognizer turns injected touch contacts into a zoom. They differ in how much movement they want.</summary>
public enum GestureEngine
{
    /// <summary>Chromium's own touch handling.</summary>
    Chromium,

    /// <summary>Gecko's, which needs a different injection device as well.</summary>
    Gecko,

    /// <summary>Windows' gesture recognizer, used by everything else, PDF readers included.</summary>
    Windows,
}
