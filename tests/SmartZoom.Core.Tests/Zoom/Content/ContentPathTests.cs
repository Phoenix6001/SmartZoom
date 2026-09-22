using SmartZoom.Core.Zoom.Content;

namespace SmartZoom.Core.Tests.Zoom.Content;

public sealed class ContentPathTests
{
    // A real accessibility chain: role and absolute physical-pixel bounds, exactly what IContentHitTester
    // returns in production. Deliberately given a position nowhere near (0, 0), so a coordinate leak cannot
    // hide behind a coincidentally unremarkable number.
    private static readonly IReadOnlyList<ContentNode> Chain =
    [
        new ContentNode(ContentRole.Group, PixelRect.FromSize(1438, 1361, 949, 79)),
        new ContentNode(ContentRole.Document, PixelRect.FromSize(0, 0, 3832, 2074)),
    ];

    public sealed class Shape
    {
        [Fact]
        public void Never_contains_a_coordinate()
        {
            var shape = ContentPath.Shape(Chain);

            Assert.DoesNotContain("1438", shape, StringComparison.Ordinal);
            Assert.DoesNotContain("1361", shape, StringComparison.Ordinal);
            Assert.DoesNotContain("@(", shape, StringComparison.Ordinal);
        }

        [Fact]
        public void Is_roles_and_sizes_only()
        {
            Assert.Equal("Group 949x79 < Document 3832x2074", ContentPath.Shape(Chain));
        }

        [Fact]
        public void Keeps_a_chain_that_is_exactly_as_deep_as_the_cap_whole()
        {
            var chain = Deep(ContentPath.MaxShapeNodes);

            var shape = ContentPath.Shape(chain);

            Assert.DoesNotContain("more]", shape, StringComparison.Ordinal);
            Assert.Equal(ContentPath.MaxShapeNodes, shape.Split(" < ").Length);
        }

        [Fact]
        public void Truncates_a_deeper_chain_after_twelve_nodes_and_says_how_many_it_dropped()
        {
            var chain = Deep(20);

            var shape = ContentPath.Shape(chain);

            // Leaf first, so the twelve that survive are the ones somebody reading the report needs.
            Assert.StartsWith("Group 0x0 < Group 1x1", shape, StringComparison.Ordinal);
            Assert.EndsWith("[8 more]", shape, StringComparison.Ordinal);
            Assert.DoesNotContain("Group 12x12", shape, StringComparison.Ordinal);
        }

        private static ContentNode[] Deep(int nodes) =>
            [.. Enumerable.Range(0, nodes).Select(i => new ContentNode(ContentRole.Group, PixelRect.FromSize(0, 0, i, i)))];

        [Fact]
        public void Names_an_unmapped_role_by_its_raw_number_rather_than_dropping_it()
        {
            var chain = new ContentNode[] { new(ContentRole.Other, PixelRect.FromSize(10, 10, 30, 20), RawRole: 16) };

            Assert.Equal("Other(16) 30x20", ContentPath.Shape(chain));
        }
    }

    public sealed class Describe
    {
        // Contrast with Shape: this form is for the log file only, and IS allowed to carry a position.
        // Recorded here so a change that quietly made Shape and Describe identical (removing the privacy
        // distinction) would fail this test, not just the diagnostics ones.
        [Fact]
        public void Contains_the_absolute_position()
        {
            var described = ContentPath.Describe(Chain);

            Assert.Contains("@(1438,1361)", described, StringComparison.Ordinal);
        }
    }
}
