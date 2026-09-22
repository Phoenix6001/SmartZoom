namespace SmartZoom.App.Tests.Diagnostics;

/// <summary>A directory under the OS temp path, deleted on dispose, so store I/O in tests never touches
/// the machine's real diagnostics file.</summary>
internal sealed class TempDirectory : IDisposable
{
    public string Path { get; } = Directory.CreateTempSubdirectory("smartzoom-diag-test-").FullName;

    public void Dispose()
    {
        try
        {
            Directory.Delete(Path, recursive: true);
        }
        catch (IOException)
        {
            // Best effort; the OS temp folder gets cleaned up eventually regardless.
        }
    }
}
