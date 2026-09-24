using System.Text.Json;

using SmartZoom.Core.Routing;
using SmartZoom.Core.Settings;

namespace SmartZoom.Core.Tests.Settings;

public sealed class RoutingSettingsTests
{
    public sealed class A_file_naming_one_application_in_two_spellings
    {
        [Fact]
        public void Produces_one_entry_and_the_later_spelling_wins()
        {
            var routing = JsonSerializer.Deserialize<RoutingSettings>("""{ "Apps": { "chrome": "CtrlWheel", "Chrome": "None" } }""")!;

            Assert.Single(routing.Apps);
            Assert.Equal(AdapterId.None, routing.Apps["CHROME"]);
        }
    }

    public sealed class A_map_that_has_been_through_the_file
    {
        [Fact]
        public void Is_still_looked_up_without_regard_to_case()
        {
            var routing = new RoutingSettings();
            routing.Apps["notepad"] = new AdapterId("CtrlWheel");

            var loaded = JsonSerializer.Deserialize<RoutingSettings>(JsonSerializer.Serialize(routing))!;

            Assert.Equal(new AdapterId("CtrlWheel"), loaded.Apps["NOTEPAD"]);
        }

        [Fact]
        public void Keeps_the_entries_the_file_lists()
        {
            var loaded = JsonSerializer.Deserialize<RoutingSettings>("""{ "Apps": { "notepad": "CtrlWheel", "EXCEL": "None" } }""")!;

            Assert.Equal(2, loaded.Apps.Count);
        }
    }
}
