namespace SmartZoom.App.Settings;

/// <summary>The few colours the settings window picks for itself rather than taking from the system.</summary>
internal static class Palette
{
    /// <summary>A dark red for text that reports a failure; readable on the system's light window colour.</summary>
    public static readonly Color ErrorText = Color.FromArgb(0xB0, 0x30, 0x20);
}
