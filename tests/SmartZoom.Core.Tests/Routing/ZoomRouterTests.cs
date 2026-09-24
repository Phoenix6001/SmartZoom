using Microsoft.Extensions.Logging.Abstractions;

using SmartZoom.Core.Routing;
using SmartZoom.Core.Settings;
using SmartZoom.Core.Zoom;
using SmartZoom.Core.Zoom.Office;
using SmartZoom.Core.Zoom.Reader;

namespace SmartZoom.Core.Tests.Routing;

public sealed class ZoomRouterTests
{
    /// <summary>The adapters the shipping app registers. Adding one here is part of adding an adapter.</summary>
    private static readonly AdapterDescriptor[] Registered =
    [
        CtrlWheelAdapter.Descriptor,
        BrowserAdapter.Descriptor,
        ReaderStrategy.Descriptor,
        WordComAdapter.Descriptor,
        ExcelComAdapter.Descriptor,
    ];

    [Theory]
    [InlineData("chrome", "Browser")]
    [InlineData("msedge", "Browser")]
    [InlineData("brave", "Browser")]
    [InlineData("firefox", "Browser")]
    [InlineData("WINWORD", "WordCom")]
    [InlineData("SumatraPDF", "Reader")]
    [InlineData("Acrobat", "Reader")]
    [InlineData("EXCEL", "ExcelCom")]
    [InlineData("i_view64", "CtrlWheel")]
    public void Adapters_claim_their_own_applications(string process, string expected) =>
        Assert.Equal(new AdapterId(expected), Create().Resolve(process));

    [Theory]
    [InlineData("notepad")]
    [InlineData("POWERPNT")]
    public void An_application_no_adapter_claims_is_not_routed(string process) =>
        Assert.Null(Create().Resolve(process));

    [Theory]
    [InlineData("Chrome")]
    [InlineData("CHROME.EXE")]
    [InlineData(" chrome.exe ")]
    public void Matching_ignores_case_extension_and_whitespace(string process) =>
        Assert.Equal(new AdapterId("Browser"), Create().Resolve(process));

    [Fact]
    public void Null_process_name_is_not_routed() =>
        Assert.Null(Create().Resolve(null));

    [Fact]
    public void Settings_win_over_what_the_adapters_claim()
    {
        var router = Create(new Dictionary<string, AdapterId>
        {
            ["firefox.exe"] = new("CtrlWheel"),
            ["notepad"] = new("CtrlWheel"),
        });

        Assert.Equal(new AdapterId("CtrlWheel"), router.Resolve("firefox"));
        Assert.Equal(new AdapterId("CtrlWheel"), router.Resolve("Notepad.exe"));
    }

    [Fact]
    public void None_opts_an_application_out_without_un_routing_it()
    {
        var router = Create(new Dictionary<string, AdapterId> { ["EXCEL"] = AdapterId.None });

        // Distinct from "not routed": the coordinator stays silent either way, but this one was a decision.
        Assert.Equal(AdapterId.None, router.Resolve("excel"));
    }

    [Fact]
    public void An_id_no_adapter_provides_is_kept_and_reported()
    {
        var router = Create(new Dictionary<string, AdapterId> { ["notepad"] = new("PowerPointCom") });

        Assert.Equal(["PowerPointCom"], router.UnknownAdapterIds);

        // Kept in the table so the coordinator can say so and fall back, rather than ignoring the app.
        Assert.Equal(new AdapterId("PowerPointCom"), router.Resolve("notepad"));
    }

    [Fact]
    public void Two_adapters_claiming_the_same_application_is_a_startup_error()
    {
        var rival = new AdapterDescriptor("Rival", ["EXCEL"], "Rival", "Claims Excel too.");

        var error = Assert.Throws<InvalidOperationException>(() => Create(adapters: [.. Registered, rival]));

        Assert.Contains("EXCEL", error.Message, StringComparison.Ordinal);
        Assert.Contains("ExcelCom", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Two_adapters_sharing_an_id_is_a_startup_error()
    {
        var twin = new AdapterDescriptor("ExcelCom", [], "Twin", "Same id as the Excel adapter.");

        var error = Assert.Throws<InvalidOperationException>(() => Create(adapters: [.. Registered, twin]));

        Assert.Contains("ExcelCom", error.Message, StringComparison.Ordinal);
    }

    private static ZoomRouter Create(IDictionary<string, AdapterId>? apps = null, AdapterDescriptor[]? adapters = null) =>
        new(adapters ?? Registered,
            new RoutingSettings { Apps = apps ?? new Dictionary<string, AdapterId>(StringComparer.OrdinalIgnoreCase) },
            NullLogger<ZoomRouter>.Instance);
}
