using OCnM.App.Platform.Linux;
using OCnM.App.Platform.Mac;
using OCnM.Core.Config;

namespace OCnM.Tests;

/// <summary>Moving users from the old name (Slotty) to OCnM without losing settings or leaving stale autostart entries.</summary>
public class ConfigMigrationTests : IDisposable
{
    private readonly TempDir _tmp = new();
    private readonly string _legacy;
    private readonly string _new;

    public ConfigMigrationTests()
    {
        _legacy = _tmp.Combine("slotty");
        _new = _tmp.Combine("ocnm");
    }

    public void Dispose() => _tmp.Dispose();

    [Fact]
    public void Copies_the_old_folder_once_and_leaves_it_untouched()
    {
        _tmp.File("slotty/config.json", """{ "version": 1 }""");
        _tmp.File("slotty/slotty.log", "old log");
        _tmp.File("slotty/sub/extra.txt", "x");

        var result = ConfigMigration.Migrate(_legacy, _new);

        Assert.Equal(MigrationOutcome.Migrated, result.Outcome);
        Assert.Equal("""{ "version": 1 }""", File.ReadAllText(Path.Combine(_new, "config.json")));
        Assert.Equal("old log", File.ReadAllText(Path.Combine(_new, "slotty.log")));
        Assert.Equal("x", File.ReadAllText(Path.Combine(_new, "sub", "extra.txt")));
        Assert.True(File.Exists(Path.Combine(_legacy, "config.json")));
        Assert.Equal(["ocnm", "slotty"], Directory.GetDirectories(_tmp.Path).Select(Path.GetFileName).Order());

        Assert.Equal(MigrationOutcome.NotNeeded, ConfigMigration.Migrate(_legacy, _new).Outcome);
    }

    [Fact]
    public void Never_overwrites_an_existing_new_folder()
    {
        _tmp.File("slotty/config.json", "old");
        _tmp.File("ocnm/config.json", "new");

        var result = ConfigMigration.Migrate(_legacy, _new);

        Assert.Equal(MigrationOutcome.NotNeeded, result.Outcome);
        Assert.Equal("new", File.ReadAllText(Path.Combine(_new, "config.json")));
    }

    [Fact]
    public void Even_an_empty_new_folder_counts_as_existing()
    {
        _tmp.File("slotty/config.json", "old");
        _tmp.Dir("ocnm");

        Assert.Equal(MigrationOutcome.NotNeeded, ConfigMigration.Migrate(_legacy, _new).Outcome);
        Assert.Empty(Directory.GetFileSystemEntries(_new));
    }

    [Fact]
    public void Nothing_happens_without_an_old_folder()
    {
        Assert.Equal(MigrationOutcome.NotNeeded, ConfigMigration.Migrate(_legacy, _new).Outcome);
        Assert.False(Directory.Exists(_new));
    }
}

public class EnvVarsTests
{
    private static Func<string, string?> Env(params (string Key, string Value)[] vars) =>
        name => vars.FirstOrDefault(v => v.Key == name).Value;

    [Fact]
    public void New_name_wins()
    {
        Assert.Equal("wayland", EnvVars.Read(EnvVars.Backend, Env(("OCNM_BACKEND", "wayland"), ("SLOTTY_BACKEND", "x11"))));
    }

    [Fact]
    public void Falls_back_to_the_old_name_when_the_new_one_is_empty()
    {
        Assert.Equal("x11", EnvVars.Read(EnvVars.Backend, Env(("SLOTTY_BACKEND", "x11"))));
        Assert.Equal("/cfg", EnvVars.Read(EnvVars.ConfigDir, Env(("OCNM_CONFIG_DIR", " "), ("SLOTTY_CONFIG_DIR", "/cfg"))));
        Assert.Equal("1", EnvVars.Read(EnvVars.Solid, Env(("SLOTTY_SOLID", "1"))));
    }

    [Fact]
    public void Missing_everywhere_is_null()
    {
        Assert.Null(EnvVars.Read(EnvVars.Backend, Env()));
    }
}

public class LegacyAutostartTests : IDisposable
{
    private readonly TempDir _tmp = new();

    public void Dispose() => _tmp.Dispose();

    [Fact]
    public void Xdg_removes_the_old_slotty_entry_and_writes_the_new_one()
    {
        var autostart = new XdgAutostart(_tmp.Path);
        _tmp.File("autostart/slotty.desktop", "[Desktop Entry]\nExec=/old/slotty --hide\n");
        _tmp.File("autostart/other-app.desktop", "keep me");

        Assert.True(autostart.RemoveLegacyEntry(out var error));
        Assert.Null(error);
        Assert.False(File.Exists(autostart.LegacyEntryPath));
        Assert.True(File.Exists(_tmp.Combine("autostart", "other-app.desktop")));

        Assert.Null(autostart.SetEnabled(true));
        Assert.EndsWith("ocnm.desktop", autostart.EntryPath);
        var entry = File.ReadAllText(autostart.EntryPath);
        Assert.Contains("Name=OCnM", entry);
        Assert.Contains("--hide", entry);
        Assert.DoesNotContain("Name=Slotty", entry);

        Assert.False(autostart.RemoveLegacyEntry(out _));
    }

    [Fact]
    public void Xdg_cleanup_without_an_old_entry_is_a_no_op()
    {
        var autostart = new XdgAutostart(_tmp.Path);
        Assert.False(autostart.RemoveLegacyEntry(out var error));
        Assert.Null(error);
    }

    [Fact]
    public void LaunchAgent_removes_the_old_plist_and_writes_the_new_label()
    {
        var autostart = new LaunchAgentAutostart(_tmp.Path);
        _tmp.File("Library/LaunchAgents/app.slotty.plist", "<plist/>");

        Assert.True(autostart.RemoveLegacyEntry(out _));
        Assert.False(File.Exists(autostart.LegacyPlistPath));

        Assert.Null(autostart.SetEnabled(true));
        Assert.Contains("<string>app.ocnm</string>", File.ReadAllText(autostart.PlistPath));
    }
}
