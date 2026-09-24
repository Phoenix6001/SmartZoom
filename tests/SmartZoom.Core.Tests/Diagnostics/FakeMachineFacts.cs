using SmartZoom.Core.Diagnostics;

namespace SmartZoom.Core.Tests.Diagnostics;

internal sealed class FakeMachineFacts : IMachineFacts
{
    public string AppVersion { get; set; } = "0.1.0";

    public string OperatingSystem { get; set; } = "Windows 11 (10.0.26200)";

    /// <summary>When set, <see cref="GetDisplays"/> throws instead of returning a value, so a test can
    /// exercise the renderer's per-section failure isolation.</summary>
    public bool ThrowOnDisplays { get; set; }

    public IReadOnlyList<DisplayFacts> Displays { get; set; } = [new DisplayFacts(3840, 2160, 59, 2.0, Primary: true)];

    public IReadOnlyList<DisplayFacts> GetDisplays() =>
        ThrowOnDisplays ? throw new InvalidOperationException("Displays unavailable (test fake).") : Displays;
}
