namespace SmartZoom.Core.Diagnostics;

/// <summary>The class of thing that went wrong, or deliberately did nothing.</summary>
public enum DiagnosticKind
{
    /// <summary>A press resolved to a window and produced no zoom.</summary>
    ZoomedNothing,

    /// <summary>An adapter raised an exception the dispatcher caught.</summary>
    AdapterThrew,

    /// <summary>A trigger resolved to no window at all.</summary>
    NoWindow,

    /// <summary>An unhandled exception reached the top of the process.</summary>
    Crashed,
}
