using SmartZoom.Core.Zoom.Content;

namespace SmartZoom.Core.Tests.Zoom.Content;

public sealed class BlockSelectorTests
{
    // Bounds recorded from a real Wikipedia article in a 1874 px wide viewport.
    private static readonly PixelRect Viewport = PixelRect.FromSize(455, 420, 1874, 1527);
    private static readonly ContentNode TextRun = new(ContentRole.Text, PixelRect.FromSize(910, 1137, 655, 70));
    private static readonly ContentNode Paragraph = new(ContentRole.Group, PixelRect.FromSize(910, 1133, 949, 105));
    private static readonly ContentNode Section = new(ContentRole.Group, PixelRect.FromSize(910, 629, 949, 609));
    private static readonly ContentNode Article = new(ContentRole.Group, PixelRect.FromSize(910, 629, 949, 29286));
    private static readonly ContentNode Main = new(ContentRole.Group, PixelRect.FromSize(910, 510, 1221, 29485));
    private static readonly ContentNode Body = new(ContentRole.Group, PixelRect.FromSize(455, 420, 1859, 1527));
    private static readonly ContentNode Document = new(ContentRole.Document, Viewport);

    private readonly BlockSelector _selector = new();

    [Fact]
    public void Prefers_the_paragraph_over_the_text_run_under_the_cursor()
    {
        var hit = new ContentHit([TextRun, Paragraph, Section, Article, Main, Body, Document], Viewport);

        Assert.Same(Paragraph, _selector.Select(hit));
    }

    [Fact]
    public void Link_inside_a_paragraph_also_resolves_to_the_paragraph()
    {
        var link = new ContentNode(ContentRole.Link, PixelRect.FromSize(1000, 1140, 220, 24));
        var hit = new ContentHit([link, Paragraph, Section, Document], Viewport);

        Assert.Same(Paragraph, _selector.Select(hit));
    }

    [Fact]
    public void Text_run_is_used_when_its_parent_is_page_wide()
    {
        var hit = new ContentHit([TextRun, Body, Document], Viewport);

        Assert.Same(TextRun, _selector.Select(hit));
    }

    [Fact]
    public void Image_is_a_block_of_its_own()
    {
        var image = new ContentNode(ContentRole.Image, PixelRect.FromSize(1150, 380, 250, 220));
        var hit = new ContentHit([image, Section, Document], Viewport);

        Assert.Same(image, _selector.Select(hit));
    }

    [Fact]
    public void Tiny_leaf_is_skipped_in_favour_of_its_container()
    {
        var icon = new ContentNode(ContentRole.Image, PixelRect.FromSize(1000, 1140, 16, 16));
        var hit = new ContentHit([icon, Paragraph, Document], Viewport);

        Assert.Same(Paragraph, _selector.Select(hit));
    }

    [Fact]
    public void Page_wide_containers_are_never_chosen()
    {
        var hit = new ContentHit([Body, Document], Viewport);

        Assert.Null(_selector.Select(hit));
    }

    [Fact]
    public void Controls_are_not_blocks()
    {
        var button = new ContentNode(ContentRole.Other, PixelRect.FromSize(900, 900, 400, 60));
        var hit = new ContentHit([button, Document], Viewport);

        Assert.Null(_selector.Select(hit));
    }

    [Fact]
    public void Empty_chain_yields_nothing() =>
        Assert.Null(_selector.Select(new ContentHit([], Viewport)));

    [Fact]
    public void Nodes_after_the_document_are_ignored()
    {
        var desktop = new ContentNode(ContentRole.Group, PixelRect.FromSize(0, 0, 3840, 2160));
        var hit = new ContentHit([Document, desktop], Viewport);

        Assert.Null(_selector.Select(hit));
    }
}
