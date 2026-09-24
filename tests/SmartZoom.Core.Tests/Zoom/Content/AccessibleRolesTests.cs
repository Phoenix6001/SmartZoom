using SmartZoom.Core.Zoom.Content;

namespace SmartZoom.Core.Tests.Zoom.Content;

public sealed class AccessibleRolesTests
{
    public sealed class Chromium_msaa_roles
    {
        [Theory]
        [InlineData(15, ContentRole.Document)]
        [InlineData(20, ContentRole.Group)]
        [InlineData(41, ContentRole.Text)]
        [InlineData(42, ContentRole.Text)]
        [InlineData(30, ContentRole.Link)]
        [InlineData(40, ContentRole.Image)]
        [InlineData(24, ContentRole.Table)]
        [InlineData(33, ContentRole.List)]
        [InlineData(34, ContentRole.ListItem)]
        public void Map_to_the_block_roles_the_selector_understands(int role, ContentRole expected) =>
            Assert.Equal(expected, AccessibleRoles.Map(role));
    }

    public sealed class A_chromium_pane
    {
        // Chromium reports a generic block container - the div most of the web is built out of - as
        // ROLE_SYSTEM_PANE. Mapped to Other, the selector would reject it outright and a div-built page
        // could not be zoomed anywhere: every node on the path under the cursor would be "unknown".
        [Fact]
        public void Is_a_container_like_any_other_block() =>
            Assert.Equal(ContentRole.Group, AccessibleRoles.Map(16));
    }

    public sealed class Gecko_ia2_roles
    {
        [Theory]
        [InlineData(0x41D, ContentRole.Group)] // paragraph
        [InlineData(0x424, ContentRole.Group)] // section
        [InlineData(0x42A, ContentRole.Group)] // text frame
        [InlineData(0x414, ContentRole.Heading)]
        public void Map_to_block_roles(int role, ContentRole expected) =>
            Assert.Equal(expected, AccessibleRoles.Map(role));
    }

    public sealed class Anything_else
    {
        [Theory]
        [InlineData(0)]
        [InlineData(-1)]
        [InlineData(43)]   // ROLE_SYSTEM_PUSHBUTTON
        [InlineData(0x41B)] // IA2_ROLE_INTERNAL_FRAME
        [InlineData(int.MaxValue)]
        public void Is_other(int role) =>
            Assert.Equal(ContentRole.Other, AccessibleRoles.Map(role));
    }
}
