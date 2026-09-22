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
