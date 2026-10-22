using SmartZoom.Core.Input;
using SmartZoom.Core.Routing;

namespace SmartZoom.Core.Tests.Zoom.Reader;

/// <summary>The reader window every test in this folder points at.</summary>
internal static class Reader
{
    public static TargetInfo Acrobat { get; } = new(0x300, 0x301, 13, "Acrobat", "AcrobatSDIWindow", "AVL_AVView");

    /// <summary>The cursor, 500 px below the top of the default content area.</summary>
    public static ScreenPoint Cursor { get; } = new(800, 600);

    /// <summary>The middle of the default content area, where a press magnifies around the cursor itself.</summary>
    public static ScreenPoint Centre { get; } = new(1000, 1100);

    /// <summary>
    /// Where a x2 press at <see cref="Cursor"/> pinches: the point that brings the cursor to the middle of the
    /// default content area, (1000, 1100), since 600 + (800 - 600) * 2 = 1000 and 100 + (600 - 100) * 2 = 1100.
    /// </summary>
    public static ScreenPoint CursorAnchor { get; } = new(600, 100);
}
