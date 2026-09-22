namespace SmartZoom.Core.Diagnostics;

/// <summary>What the machine is, for the questions that depend on hardware rather than behaviour.</summary>
public interface IMachineFacts
{
    /// <summary>The SmartZoom version.</summary>
    string AppVersion { get; }

    /// <summary>A human-readable Windows version.</summary>
    string OperatingSystem { get; }

    /// <summary>Every display attached, primary first.</summary>
    IReadOnlyList<DisplayFacts> Displays { get; }
}
