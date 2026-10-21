using Slotty.Core.FileOps;

namespace Slotty.Tests;

public class PathRulesTests
{
    [Fact]
    public void Same_and_child_paths_are_inside()
    {
        using var tmp = new TempDir();
        var a = tmp.Dir("a");
        Assert.True(PathRules.IsSameOrInside(a, a));
        Assert.True(PathRules.IsSameOrInside(a + Path.DirectorySeparatorChar, a));
        Assert.True(PathRules.IsSameOrInside(Path.Combine(a, "b", "c"), a));
        Assert.True(PathRules.IsSameOrInside(Path.Combine(a, "b", "..", "c"), a));
    }

    [Fact]
    public void Sibling_with_common_prefix_is_not_inside()
    {
        using var tmp = new TempDir();
        Assert.False(PathRules.IsSameOrInside(tmp.Combine("abc"), tmp.Combine("ab")));
        Assert.False(PathRules.IsSameOrInside(tmp.Combine("a"), tmp.Combine("a", "b")));
    }

    [Fact]
    public void Nesting_through_a_symlink_is_detected()
    {
        if (OperatingSystem.IsWindows())
            return; // creating symlinks needs developer mode on Windows
        using var tmp = new TempDir();
        var src = tmp.Dir("src");
        var inner = tmp.Dir("src", "inner");
        var link = tmp.Combine("link-to-inner");
        Directory.CreateSymbolicLink(link, inner);

        Assert.True(PathRules.WouldNestIntoItself(src, link));
        Assert.False(PathRules.WouldNestIntoItself(src, tmp.Dir("elsewhere")));
    }

    [Fact]
    public void ResolveRealPath_follows_links_in_the_middle()
    {
        if (OperatingSystem.IsWindows())
            return;
        using var tmp = new TempDir();
        var real = tmp.Dir("real", "deep");
        Directory.CreateSymbolicLink(tmp.Combine("l"), tmp.Combine("real"));
        Assert.Equal(PathRules.ResolveRealPath(real), PathRules.ResolveRealPath(tmp.Combine("l", "deep")));
    }

    [Fact]
    public void Same_folder_is_on_the_same_volume()
    {
        using var tmp = new TempDir();
        Assert.True(PathRules.AreOnSameVolume(tmp.Dir("x"), tmp.Dir("y")));
    }
}
