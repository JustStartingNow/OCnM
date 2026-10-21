using Slotty.Core.Platform;

namespace Slotty.Tests;

public class HotkeyGestureTests
{
    [Theory]
    [InlineData("Ctrl+Alt+Space", HotkeyModifiers.Ctrl | HotkeyModifiers.Alt, "Space")]
    [InlineData("ctrl + shift + k", HotkeyModifiers.Ctrl | HotkeyModifiers.Shift, "K")]
    [InlineData("Super+F12", HotkeyModifiers.Super, "F12")]
    [InlineData("Win+Esc", HotkeyModifiers.Super, "Escape")]
    [InlineData("Control+Option+1", HotkeyModifiers.Ctrl | HotkeyModifiers.Alt, "1")]
    [InlineData("Alt+PgDn", HotkeyModifiers.Alt, "PageDown")]
    public void Parses(string text, HotkeyModifiers mods, string key)
    {
        Assert.True(HotkeyGesture.TryParse(text, out var g, out var error), error);
        Assert.Equal(mods, g!.Modifiers);
        Assert.Equal(key, g.Key);
    }

    [Theory]
    [InlineData("")]
    [InlineData("Space")]
    [InlineData("Ctrl+Alt")]
    [InlineData("Ctrl+A+B")]
    [InlineData("Ctrl++")]
    [InlineData("Ctrl+F25")]
    [InlineData("Ctrl+F0")]
    [InlineData("Ctrl+Banana")]
    public void Rejects(string text)
    {
        Assert.False(HotkeyGesture.TryParse(text, out _, out var error));
        Assert.False(string.IsNullOrEmpty(error));
    }

    [Fact]
    public void ToString_is_canonical()
    {
        Assert.True(HotkeyGesture.TryParse("space+alt+CTRL", out var g, out _));
        Assert.Equal("Ctrl+Alt+Space", g!.ToString());
    }
}

public class SessionDetectorTests
{
    private static Func<string, string?> Env(params (string Key, string Value)[] vars) =>
        name => vars.FirstOrDefault(v => v.Key == name).Value;

    [Fact]
    public void Wayland_display_means_wayland_even_with_xwayland_display()
    {
        Assert.Equal(SessionKind.LinuxWayland, SessionDetector.DetectUnix(Env(("WAYLAND_DISPLAY", "wayland-1"), ("DISPLAY", ":0"))));
    }

    [Fact]
    public void Session_type_wayland_counts()
    {
        Assert.Equal(SessionKind.LinuxWayland, SessionDetector.DetectUnix(Env(("XDG_SESSION_TYPE", "wayland"))));
    }

    [Fact]
    public void Plain_display_is_x11()
    {
        Assert.Equal(SessionKind.LinuxX11, SessionDetector.DetectUnix(Env(("DISPLAY", ":0"))));
    }

    [Fact]
    public void Nothing_is_other()
    {
        Assert.Equal(SessionKind.Other, SessionDetector.DetectUnix(Env()));
    }
}
