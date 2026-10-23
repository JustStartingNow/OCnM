using OCnM.App.Platform.Linux;

namespace OCnM.Tests;

public class FolderTabsTests
{
    private const string NiriReply = """
        {"Ok":{"Windows":[
          {"id":2,"title":"A — Dolphin","app_id":"org.kde.dolphin","pid":100,"focus_timestamp":{"secs":50,"nanos":0}},
          {"id":7,"title":"B — Dolphin","app_id":"org.kde.dolphin","pid":200,"focus_timestamp":{"secs":90,"nanos":500}},
          {"id":9,"title":"kitty","app_id":"kitty","pid":300,"focus_timestamp":{"secs":99,"nanos":0}},
          {"id":11,"title":"no focus yet","app_id":"org.kde.dolphin","pid":100,"focus_timestamp":null}
        ]}}
        """;

    [Fact]
    public void Parses_niri_windows()
    {
        var windows = NiriIpc.ParseWindows(NiriReply);
        Assert.Equal(4, windows.Count);
        Assert.Equal(new NiriWindow(7, "org.kde.dolphin", 200, 90.0000005), windows[1]);
        Assert.Equal(0, windows[3].FocusTime);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not json")]
    [InlineData("""{"Err":"nope"}""")]
    public void Bad_niri_replies_mean_no_windows(string reply)
    {
        Assert.Empty(NiriIpc.ParseWindows(reply));
    }

    [Fact]
    public void Picks_the_most_recently_used_dolphin_window()
    {
        var target = DolphinTabs.PickTarget(
            ["org.freedesktop.DBus", "org.kde.dolphin-100", "org.kde.dolphin-200", "org.kde.kwalletd6"],
            NiriIpc.ParseWindows(NiriReply));
        Assert.Equal(new DolphinTarget("org.kde.dolphin-200", 200, 7), target);
    }

    [Fact]
    public void A_dolphin_without_any_window_is_not_used()
    {
        // e.g. "dolphin --daemon", started only to serve org.freedesktop.FileManager1
        var target = DolphinTabs.PickTarget(["org.kde.dolphin-555"], NiriIpc.ParseWindows(NiriReply));
        Assert.Null(target);
    }

    [Fact]
    public void Without_niri_the_first_dolphin_is_used()
    {
        var target = DolphinTabs.PickTarget(["org.kde.dolphin-300", "org.kde.dolphin-120"], windows: null);
        Assert.Equal(new DolphinTarget("org.kde.dolphin-120", 120, null), target);
    }

    [Theory]
    [InlineData("org.kde.konsole-5 org.kde.dolphin")]
    [InlineData("org.kde.dolphin-abc org.kde.dolphin-0")]
    [InlineData("")]
    public void No_dolphin_on_the_bus_means_no_target(string services)
    {
        Assert.Null(DolphinTabs.PickTarget(services.Split(' ', StringSplitOptions.RemoveEmptyEntries), windows: null));
    }

    [Fact]
    public void Finds_the_lowest_window_object_path()
    {
        const string xml = """
            <node>
              <interface name="org.freedesktop.DBus.Introspectable"/>
              <node name="Dolphin_3"/>
              <node name="Dolphin_1"/>
              <node name="Something"/>
            </node>
            """;
        Assert.Equal("/dolphin/Dolphin_1", DolphinTabs.WindowPathFromIntrospection(xml));
        Assert.Null(DolphinTabs.WindowPathFromIntrospection("<node><node name=\"actions\"/></node>"));
    }
}
