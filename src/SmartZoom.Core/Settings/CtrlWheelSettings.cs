namespace SmartZoom.Core.Settings;

/// <summary>Tuning for the generic Ctrl+wheel adapter.</summary>
public sealed class CtrlWheelSettings
{
    /// <summary>Wheel detents sent per zoom. Most apps step 10–25% per detent.</summary>
    public int Ticks { get; set; } = 6;

    /// <summary>Pause between detents so the app doesn't coalesce them; 0 sends them back to back.</summary>
    public int IntervalMs { get; set; } = 20;
}
