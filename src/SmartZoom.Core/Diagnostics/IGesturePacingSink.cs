namespace SmartZoom.Core.Diagnostics;

/// <summary>Told how each injected gesture was actually delivered.</summary>
/// <remarks>
/// Declared in Core so the injector, which lives in Interop, can report without knowing what records it.
/// </remarks>
public interface IGesturePacingSink
{
    /// <summary>Reports one delivered gesture.</summary>
    /// <param name="frames">Frames the gesture asked for.</param>
    /// <param name="intervalMs">The interval between them.</param>
    /// <param name="lateFrames">How many missed their slot.</param>
    /// <param name="worstLateMs">The worst lateness in this gesture.</param>
    void Paced(int frames, int intervalMs, int lateFrames, double worstLateMs);
}
