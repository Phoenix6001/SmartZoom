using SmartZoom.Core.Routing;
using SmartZoom.Core.Settings;

namespace SmartZoom.Core.Tests.Routing;

public sealed class ZoomRouterTests
{
    [Theory]
    [InlineData("chrome", AdapterKind.Browser)]
    [InlineData("msedge", AdapterKind.Browser)]
    [InlineData("brave", AdapterKind.Browser)]
    [InlineData("firefox", AdapterKind.Browser)]
    [InlineData("WINWORD", AdapterKind.WordCom)]
    [InlineData("POWERPNT", AdapterKind.PowerPointCom)]
    [InlineData("SumatraPDF", AdapterKind.Keys)]
    [InlineData("Acrobat", AdapterKind.Keys)]
    [InlineData("EXCEL", AdapterKind.ExcelCom)]
    [InlineData("notepad", AdapterKind.None)]
    public void Default_settings_route_known_apps(string process, AdapterKind expected) =>
        Assert.Equal(expected, new ZoomRouter(new RoutingSettings()).Resolve(process));

    [Theory]
    [InlineData("Chrome")]
    [InlineData("CHROME.EXE")]
    [InlineData(" chrome.exe ")]
    public void Matching_ignores_case_extension_and_whitespace(string process) =>
        Assert.Equal(AdapterKind.Browser, new ZoomRouter(new RoutingSettings()).Resolve(process));

    [Fact]
    public void Null_process_name_routes_to_none() =>
        Assert.Equal(AdapterKind.None, new ZoomRouter(new RoutingSettings()).Resolve(null));

    [Fact]
    public void Overrides_win_over_category_lists()
    {
        var settings = new RoutingSettings
        {
            Overrides = new Dictionary<string, AdapterKind>
            {
                ["firefox.exe"] = AdapterKind.CtrlWheel,
                ["EXCEL"] = AdapterKind.None,
                ["notepad"] = AdapterKind.CtrlWheel,
            },
        };

        var router = new ZoomRouter(settings);

        Assert.Equal(AdapterKind.CtrlWheel, router.Resolve("firefox"));
        Assert.Equal(AdapterKind.None, router.Resolve("excel"));
        Assert.Equal(AdapterKind.CtrlWheel, router.Resolve("Notepad.exe"));
    }

    [Fact]
    public void Custom_lists_replace_defaults()
    {
        var router = new ZoomRouter(new RoutingSettings { BrowserProcesses = ["brave"] });

        Assert.Equal(AdapterKind.Browser, router.Resolve("brave"));
        Assert.Equal(AdapterKind.None, router.Resolve("chrome"));
    }
}
