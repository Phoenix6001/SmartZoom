namespace SmartZoom.Core.Zoom.Content;

/// <summary>Coarse semantic role of a content node, normalized across accessibility APIs.</summary>
public enum ContentRole
{
    /// <summary>Anything not listed below (buttons, inputs, unknown).</summary>
    Other = 0,

    /// <summary>The page or document root; its bounds are the viewport.</summary>
    Document,

    /// <summary>Generic container: paragraph, section, div, article.</summary>
    Group,

    /// <summary>A run of text.</summary>
    Text,

    /// <summary>A hyperlink.</summary>
    Link,

    /// <summary>An image or figure.</summary>
    Image,

    /// <summary>A table.</summary>
    Table,

    /// <summary>A list.</summary>
    List,

    /// <summary>One item of a list.</summary>
    ListItem,

    /// <summary>A heading.</summary>
    Heading,
}
