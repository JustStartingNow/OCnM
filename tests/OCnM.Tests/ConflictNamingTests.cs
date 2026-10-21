using OCnM.Core.FileOps;

namespace OCnM.Tests;

public class ConflictNamingTests
{
    private static Func<string, bool> Taken(params string[] names) =>
        path => names.Contains(Path.GetFileName(path));

    [Fact]
    public void Free_name_is_kept()
    {
        Assert.Equal("a.txt", ConflictNaming.GetAvailableName("/d", "a.txt", false, Taken()));
    }

    [Fact]
    public void Taken_file_gets_numbered_before_the_extension()
    {
        Assert.Equal("a (1).txt", ConflictNaming.GetAvailableName("/d", "a.txt", false, Taken("a.txt")));
        Assert.Equal("a (3).txt", ConflictNaming.GetAvailableName("/d", "a.txt", false, Taken("a.txt", "a (1).txt", "a (2).txt")));
    }

    [Fact]
    public void Folders_get_numbered_at_the_end_even_with_dots()
    {
        Assert.Equal("v1.2 (1)", ConflictNaming.GetAvailableName("/d", "v1.2", true, Taken("v1.2")));
    }

    [Theory]
    [InlineData(".bashrc", ".bashrc (1)")]
    [InlineData("archive.tar.gz", "archive.tar (1).gz")]
    [InlineData("noext", "noext (1)")]
    [InlineData("文件.txt", "文件 (1).txt")]
    public void Edge_case_names(string name, string expected)
    {
        Assert.Equal(expected, ConflictNaming.GetAvailableName("/d", name, false, Taken(name)));
    }

    [Fact]
    public void Real_file_system_check_is_used_by_default()
    {
        using var tmp = new TempDir();
        tmp.File("x.bin");
        tmp.Dir("x (1).bin");
        Assert.Equal("x (2).bin", ConflictNaming.GetAvailableName(tmp.Path, "x.bin", false));
    }
}
