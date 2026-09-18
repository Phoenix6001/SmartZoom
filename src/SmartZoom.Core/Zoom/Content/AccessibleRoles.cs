namespace SmartZoom.Core.Zoom.Content;

/// <summary>
/// Maps the role numbers browsers report through Microsoft Active Accessibility to <see cref="ContentRole"/>.
/// </summary>
/// <remarks>
/// Chromium reports paragraphs, sections and generic containers as <c>ROLE_SYSTEM_GROUPING</c>. Gecko
/// (Firefox) instead reports the IAccessible2 roles it defines for them, which arrive through the same
/// <c>accRole</c> property as numbers above the MSAA range.
/// </remarks>
public static class AccessibleRoles
{
    /// <summary><c>ROLE_SYSTEM_DOCUMENT</c>.</summary>
    public const int Document = 15;

    /// <summary><c>ROLE_SYSTEM_GROUPING</c>.</summary>
    public const int Grouping = 20;

    /// <summary><c>ROLE_SYSTEM_TABLE</c>.</summary>
    public const int Table = 24;

    /// <summary><c>ROLE_SYSTEM_LINK</c>.</summary>
    public const int Link = 30;

    /// <summary><c>ROLE_SYSTEM_LIST</c>.</summary>
    public const int List = 33;

    /// <summary><c>ROLE_SYSTEM_LISTITEM</c>.</summary>
    public const int ListItem = 34;

    /// <summary><c>ROLE_SYSTEM_GRAPHIC</c>.</summary>
    public const int Graphic = 40;

    /// <summary><c>ROLE_SYSTEM_STATICTEXT</c>.</summary>
    public const int StaticText = 41;

    /// <summary><c>ROLE_SYSTEM_TEXT</c>.</summary>
    public const int Text = 42;

    /// <summary><c>IA2_ROLE_HEADING</c> (Gecko).</summary>
    public const int Ia2Heading = 0x414;

    /// <summary><c>IA2_ROLE_PARAGRAPH</c> (Gecko).</summary>
    public const int Ia2Paragraph = 0x41D;

    /// <summary><c>IA2_ROLE_SECTION</c> (Gecko: div, article, aside, ...).</summary>
    public const int Ia2Section = 0x424;

    /// <summary><c>IA2_ROLE_TEXT_FRAME</c> (Gecko: generic block or inline container).</summary>
    public const int Ia2TextFrame = 0x42A;

    /// <summary>Normalizes a role number.</summary>
    /// <param name="role">The value of <c>IAccessible.accRole</c>.</param>
    /// <returns>The coarse role, or <see cref="ContentRole.Other"/> for anything unknown.</returns>
    public static ContentRole Map(int role) => role switch
    {
        Document => ContentRole.Document,
        Grouping or Ia2Paragraph or Ia2Section or Ia2TextFrame => ContentRole.Group,
        StaticText or Text => ContentRole.Text,
        Link => ContentRole.Link,
        Graphic => ContentRole.Image,
        Table => ContentRole.Table,
        List => ContentRole.List,
        ListItem => ContentRole.ListItem,
        Ia2Heading => ContentRole.Heading,
        _ => ContentRole.Other,
    };
}
