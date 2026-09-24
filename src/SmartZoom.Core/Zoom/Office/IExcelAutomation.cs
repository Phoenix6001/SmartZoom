using SmartZoom.Core.Routing;

namespace SmartZoom.Core.Zoom.Office;

/// <summary>Attaches to Excel worksheet windows.</summary>
public interface IExcelAutomation
{
    /// <summary>Attaches to the Excel worksheet window under the cursor, or returns null if the target isn't one.</summary>
    /// <param name="target">The window the trigger landed on.</param>
    /// <param name="cancellationToken">Cancellation.</param>
    Task<IExcelWindow?> AttachAsync(TargetInfo target, CancellationToken cancellationToken);
}
