using SmartZoom.Core.Routing;

namespace SmartZoom.Core.Zoom.Reader;

/// <summary>
/// The document-reader strategy. There are two implementations of it —
/// <see cref="ReaderPinchAdapter"/> and <see cref="ReaderShortcutAdapter"/> — but only one identity: as far
/// as routing and the settings file are concerned they are the same strategy, and <c>Zoom.Reader.Mode</c>
/// decides which one the application registers.
/// </summary>
public static class ReaderStrategy
{
    /// <summary>How the reader strategy is named in settings, and what it handles out of the box.</summary>
    public static AdapterDescriptor Descriptor { get; } = new(
        "Reader",
        ["Acrobat", "AcroRd32", "SumatraPDF"],
        "Document readers",
        "Magnifies a PDF around the cursor, and comes back to the reader's own fit-page zoom. Tuned under \"Zoom.Reader\".");
}
