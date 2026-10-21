using Slotty.Core.FileOps;

namespace Slotty.Tests;

public class PathDisplayTests
{
    private static readonly string Home = OperatingSystem.IsWindows() ? @"C:\Users\me" : "/home/me";

    private static string P(params string[] parts) => Path.Combine([Home, .. parts]);

    [Fact]
    public void Home_becomes_tilde()
    {
        var expected = "~" + Path.DirectorySeparatorChar + "Pictures";
        Assert.Equal(expected, PathDisplay.Shorten(P("Pictures"), 80, Home));
    }

    [Fact]
    public void Paths_outside_home_are_kept()
    {
        var other = OperatingSystem.IsWindows() ? @"D:\Media" : "/mnt/media";
        Assert.Equal(other, PathDisplay.Shorten(other, 80, Home));
    }

    [Fact]
    public void A_folder_that_only_starts_like_home_is_not_shortened()
    {
        var sibling = Home + "2" + Path.DirectorySeparatorChar + "x";
        Assert.Equal(sibling, PathDisplay.Shorten(sibling, 80, Home));
    }

    [Fact]
    public void Long_paths_keep_their_end_and_start_at_a_separator()
    {
        var path = P("aaaaaaaaaa", "bbbbbbbbbb", "cccccccccc", "Target");
        var s = PathDisplay.Shorten(path, 22, Home);
        Assert.StartsWith("…", s);
        Assert.EndsWith("Target", s);
        Assert.True(s.Length <= 22);
        Assert.Equal(Path.DirectorySeparatorChar, s[1]);
    }
}
