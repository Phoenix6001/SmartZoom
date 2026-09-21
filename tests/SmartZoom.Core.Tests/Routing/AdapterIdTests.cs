using System.Text.Json;

using SmartZoom.Core.Routing;
using SmartZoom.Core.Settings;

namespace SmartZoom.Core.Tests.Routing;

public sealed class AdapterIdTests
{
    [Fact]
    public void Ids_compare_ignoring_case_and_surrounding_space()
    {
        Assert.Equal(new AdapterId("Browser"), new AdapterId(" browser "));
        Assert.True(new AdapterId("BROWSER") == new AdapterId("browser"));
        Assert.True(new AdapterId("Browser") != AdapterId.None);
    }

    [Fact]
    public void Ids_work_as_dictionary_keys_regardless_of_case()
    {
        var map = new Dictionary<AdapterId, int> { [new AdapterId("Reader")] = 1 };

        Assert.Equal(1, map[new AdapterId("READER")]);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void An_empty_id_is_rejected(string value) =>
        Assert.Throws<ArgumentException>(() => new AdapterId(value));

    [Fact]
    public void Routing_reads_the_shape_a_user_would_type()
    {
        const string Json = """{ "Apps": { "notepad": "CtrlWheel", "EXCEL": "None" } }""";

        var routing = JsonSerializer.Deserialize<RoutingSettings>(Json)!;

        Assert.Equal(new AdapterId("CtrlWheel"), routing.Apps["notepad"]);
        Assert.Equal(AdapterId.None, routing.Apps["EXCEL"]);
    }

    [Fact]
    public void Routing_writes_ids_as_plain_strings()
    {
        var routing = new RoutingSettings();
        routing.Apps["notepad"] = new AdapterId("CtrlWheel");

        var json = JsonSerializer.Serialize(routing);

        Assert.Contains("\"notepad\":\"CtrlWheel\"", json, StringComparison.Ordinal);
    }

    [Fact]
    public void A_null_id_in_the_file_is_reported_rather_than_read_as_nothing() =>
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<RoutingSettings>("""{ "Apps": { "notepad": null } }"""));
}
