using SmartZoom.Core.Routing;

namespace SmartZoom.Core.Zoom.Office;

/// <summary>Attaches to Word windows.</summary>
public interface IWordAutomation
{
    /// <summary>Attaches to the Word document window under the cursor, or returns null if the target isn't one.</summary>
    Task<IWordWindow?> AttachAsync(TargetInfo target, CancellationToken cancellationToken);
}
