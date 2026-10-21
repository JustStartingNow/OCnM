namespace OCnM.Core.FileOps;

/// <summary>Turns file-system exceptions into short messages a user can act on.</summary>
public static class UserMessages
{
    // Windows HRESULTs and Unix errno values that IOException carries in HResult.
    private const int WinDiskFull = unchecked((int)0x80070070);
    private const int WinHandleDiskFull = unchecked((int)0x80070027);
    private const int UnixNoSpace = 28;

    public static string Describe(Exception ex, string path)
    {
        var name = DisplayName(path);
        return ex switch
        {
            UnauthorizedAccessException => $"Permission denied: \"{name}\".",
            PathTooLongException => $"The path is too long: \"{name}\".",
            DirectoryNotFoundException => $"Folder not found: \"{name}\".",
            FileNotFoundException => $"File not found: \"{name}\".",
            IOException io when io.HResult is WinDiskFull or WinHandleDiskFull or UnixNoSpace => "Not enough free disk space.",
            IOException io => $"\"{name}\": {io.Message}",
            _ => $"\"{name}\": {ex.Message}",
        };
    }

    public static string DisplayName(string path)
    {
        var trimmed = Path.TrimEndingDirectorySeparator(path);
        var name = Path.GetFileName(trimmed);
        return string.IsNullOrEmpty(name) ? trimmed : name;
    }
}
