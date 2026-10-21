using System.Text;
using Slotty.Core.Config;
using Slotty.Core.Models;

namespace Slotty.Tests;

public class ConfigStoreTests : IDisposable
{
    private readonly TempDir _tmp = new();

    private ConfigStore Store() => new(_tmp.Combine("config.json"));

    public void Dispose() => _tmp.Dispose();

    [Fact]
    public void Missing_file_creates_nine_empty_slices_and_writes_the_file()
    {
        var store = Store();
        var result = store.Load();

        Assert.Equal(ConfigLoadStatus.CreatedNew, result.Status);
        Assert.Equal(9, result.Config.Slices.Length);
        Assert.All(result.Config.Slices, s => Assert.False(s.IsAssigned));
        Assert.Equal(Enumerable.Range(0, 9), result.Config.Slices.Select(s => s.Index));
        Assert.True(File.Exists(store.FilePath));
        Assert.Equal(DropAction.Copy, result.Config.Settings.DefaultAction);
        Assert.Equal(ConflictPolicy.AutoRename, result.Config.Settings.ConflictPolicy);
    }

    [Fact]
    public void Save_then_load_round_trips()
    {
        var store = Store();
        var config = SlottyConfig.CreateDefault()
            .WithSlice(new SliceConfig { Index = 0, Path = "/home/u/Ünïcødé 文件", Label = "Docs", Color = "#FF8800", Icon = SliceIcon.Documents, Action = SliceAction.Move })
            .WithSlice(new SliceConfig { Index = 8, Path = @"C:\Users\u\Downloads" });
        config = config with
        {
            Settings = config.Settings with
            {
                DefaultAction = DropAction.Move,
                ConflictPolicy = ConflictPolicy.Ask,
                Hotkey = "Ctrl+Shift+K",
                Theme = ThemePreference.Dark,
                HideAfterDropMs = 1234,
                Pinned = true,
                ReduceMotion = true,
                LinuxBackend = LinuxBackend.Wayland,
            },
        };

        store.Save(config);
        var loaded = store.Load();

        Assert.Equal(ConfigLoadStatus.Loaded, loaded.Status);
        Assert.Equal(config.Settings, loaded.Config.Settings);
        for (var i = 0; i < 9; i++)
            Assert.Equal(config.Slices[i], loaded.Config.Slices[i]);
    }

    [Fact]
    public void Written_json_uses_the_documented_shape()
    {
        var json = Encoding.UTF8.GetString(ConfigStore.Serialize(SlottyConfig.CreateDefault()));
        Assert.Contains("\"version\": 1", json);
        Assert.Contains("\"defaultAction\": \"copy\"", json);
        Assert.Contains("\"conflictPolicy\": \"autoRename\"", json);
        Assert.Contains("\"hotkey\": \"Ctrl+Alt+Space\"", json);
        Assert.Contains("\"theme\": \"system\"", json);
        Assert.Contains("\"action\": \"inherit\"", json);
        Assert.Contains("\"path\": null", json);
    }

    [Fact]
    public void Corrupt_file_is_backed_up_and_recreated()
    {
        var store = Store();
        File.WriteAllText(store.FilePath, "{ this is not json");

        var result = store.Load();

        Assert.Equal(ConfigLoadStatus.RecoveredFromCorruption, result.Status);
        Assert.Equal(store.BackupPath, result.BackupPath);
        Assert.Equal("{ this is not json", File.ReadAllText(store.BackupPath));
        Assert.Equal(9, result.Config.Slices.Length);
        Assert.True(ConfigStore.TryParse(File.ReadAllText(store.FilePath), out _, out _));
    }

    [Theory]
    [InlineData("")]
    [InlineData("[]")]
    [InlineData("42")]
    [InlineData("null")]
    public void Non_object_roots_count_as_corrupt(string content)
    {
        var store = Store();
        File.WriteAllText(store.FilePath, content);
        Assert.Equal(ConfigLoadStatus.RecoveredFromCorruption, store.Load().Status);
    }

    [Fact]
    public void Unknown_and_invalid_fields_fall_back_to_defaults()
    {
        const string json = """
            {
              "version": 1,
              "futureThing": { "x": 1 },
              "settings": { "defaultAction": "teleport", "conflictPolicy": 7, "hotkey": 5, "theme": "dark", "hideAfterDropMs": -50, "extra": true },
              "slices": [
                { "index": 0, "path": "/a", "action": "move", "icon": "nonsense", "color": "red" },
                { "index": 1, "path": "   ", "label": "" },
                "garbage",
                { "index": 3, "path": "/d", "color": "#abc", "icon": "music", "unknown": [1,2] }
              ]
            }
            """;
        Assert.True(ConfigStore.TryParse(json, out var config, out _));

        Assert.Equal(DropAction.Copy, config.Settings.DefaultAction);
        Assert.Equal(ConflictPolicy.AutoRename, config.Settings.ConflictPolicy);
        Assert.Equal(AppSettings.DefaultHotkey, config.Settings.Hotkey);
        Assert.Equal(ThemePreference.Dark, config.Settings.Theme);
        Assert.Equal(0, config.Settings.HideAfterDropMs);

        Assert.Equal(9, config.Slices.Length);
        Assert.Equal("/a", config.Slices[0].Path);
        Assert.Equal(SliceAction.Move, config.Slices[0].Action);
        Assert.Null(config.Slices[0].Icon);
        Assert.Null(config.Slices[0].Color);
        Assert.False(config.Slices[1].IsAssigned);
        Assert.Null(config.Slices[1].Label);
        Assert.Equal("#AABBCC", config.Slices[3].Color);
        Assert.Equal(SliceIcon.Music, config.Slices[3].Icon);
    }

    [Fact]
    public void Always_exactly_nine_slices_even_with_too_many_or_duplicate_indexes()
    {
        var items = Enumerable.Range(0, 14).Select(i => $$"""{ "index": {{i % 3}}, "path": "/p{{i}}" }""");
        var json = $$"""{ "slices": [ {{string.Join(",", items)}} ] }""";

        Assert.True(ConfigStore.TryParse(json, out var config, out _));
        Assert.Equal(9, config.Slices.Length);
        Assert.Equal(Enumerable.Range(0, 9), config.Slices.Select(s => s.Index));
        Assert.Equal("/p0", config.Slices[0].Path);
        Assert.Equal("/p1", config.Slices[1].Path);
        Assert.Equal("/p2", config.Slices[2].Path);
        // Duplicates fill the free positions in order.
        Assert.Equal("/p3", config.Slices[3].Path);
    }

    [Fact]
    public void Too_few_slices_are_padded_with_empty_ones()
    {
        Assert.True(ConfigStore.TryParse("""{ "slices": [ { "path": "/only" } ] }""", out var config, out _));
        Assert.Equal(9, config.Slices.Length);
        Assert.Equal("/only", config.Slices[0].Path);
        Assert.All(config.Slices.Skip(1), s => Assert.False(s.IsAssigned));
    }

    [Fact]
    public void Missing_sections_use_defaults()
    {
        Assert.True(ConfigStore.TryParse("{}", out var config, out _));
        Assert.Equal(new AppSettings(), config.Settings);
        Assert.Equal(9, config.Slices.Length);
    }

    [Fact]
    public void Comments_and_trailing_commas_are_tolerated()
    {
        const string json = """
            { // hand edited
              "settings": { "theme": "light", }, }
            """;
        Assert.True(ConfigStore.TryParse(json, out var config, out _));
        Assert.Equal(ThemePreference.Light, config.Settings.Theme);
    }

    [Fact]
    public void Save_leaves_no_temp_file_behind()
    {
        var store = Store();
        store.Save(SlottyConfig.CreateDefault());
        Assert.False(File.Exists(store.FilePath + ".tmp"));
    }

    [Fact]
    public void Save_creates_missing_directories()
    {
        var store = new ConfigStore(_tmp.Combine("a", "b", "config.json"));
        store.Save(SlottyConfig.CreateDefault());
        Assert.True(File.Exists(store.FilePath));
    }

    [Theory]
    [InlineData("#fff", "#FFFFFF")]
    [InlineData("12ab34", "#12AB34")]
    [InlineData(" #12AB34 ", "#12AB34")]
    [InlineData("#12AB3", null)]
    [InlineData("#GGGGGG", null)]
    [InlineData("", null)]
    public void NormalizeColor(string input, string? expected)
    {
        Assert.Equal(expected, ConfigStore.NormalizeColor(input));
    }

    [Fact]
    public void SwapSlices_keeps_indexes_in_position_order()
    {
        var config = SlottyConfig.CreateDefault().WithSlice(new SliceConfig { Index = 0, Path = "/a" });
        var swapped = config.SwapSlices(0, 4);
        Assert.Equal("/a", swapped.Slices[4].Path);
        Assert.Equal(4, swapped.Slices[4].Index);
        Assert.Equal(0, swapped.Slices[0].Index);
        Assert.False(swapped.Slices[0].IsAssigned);
    }

    [Theory]
    [InlineData(null, "/home/u/Pictures", "Pictures")]
    [InlineData("Pics", "/home/u/Pictures", "Pics")]
    [InlineData(null, "/home/u/Pictures/", "Pictures")]
    [InlineData(null, null, "Empty")]
    public void DisplayLabel(string? label, string? path, string expected)
    {
        Assert.Equal(expected, new SliceConfig { Label = label, Path = path }.DisplayLabel);
    }
}
