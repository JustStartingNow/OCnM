namespace Slotty.Tests;

/// <summary>A unique temp directory deleted on dispose.</summary>
public sealed class TempDir : IDisposable
{
    public TempDir()
    {
        Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "slotty-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path);
    }

    public string Path { get; }

    public string Combine(params string[] parts) => System.IO.Path.Combine([Path, .. parts]);

    public string Dir(params string[] parts)
    {
        var p = Combine(parts);
        Directory.CreateDirectory(p);
        return p;
    }

    public string File(string relative, string content = "data")
    {
        var p = Combine(relative);
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(p)!);
        System.IO.File.WriteAllText(p, content);
        return p;
    }

    public void Dispose()
    {
        try
        {
            foreach (var f in new DirectoryInfo(Path).EnumerateFileSystemInfos("*", new EnumerationOptions { RecurseSubdirectories = true, AttributesToSkip = 0 }))
            {
                if (!OperatingSystem.IsWindows() && f.LinkTarget is null)
                    f.UnixFileMode |= UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute;
                else if (OperatingSystem.IsWindows())
                    f.Attributes &= ~FileAttributes.ReadOnly;
            }
            Directory.Delete(Path, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Best effort.
        }
    }
}
