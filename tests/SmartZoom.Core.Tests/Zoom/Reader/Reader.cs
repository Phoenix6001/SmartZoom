using SmartZoom.Core.Input;
using SmartZoom.Core.Routing;

namespace SmartZoom.Core.Tests.Zoom.Reader;

/// <summary>The reader window every test in this folder points at.</summary>
internal static class Reader
{
    public static TargetInfo Acrobat { get; } = new(0x300, 0x301, 13, "Acrobat", "AcrobatSDIWindow", "AVL_AVView");

    /// <summary>The cursor, 500 px below the top of the default content area.</summary>
    public static ScreenPoint Cursor { get; } = new(800, 600);
}
