namespace OCnM.Core.FileOps;

public static class ConflictNaming
{
    /// <summary>
    /// Returns <paramref name="name"/> if it is free in <paramref name="directory"/>, else the first free
    /// "name (n).ext" (files) or "name (n)" (folders).
    /// </summary>
    public static string GetAvailableName(string directory, string name, bool isDirectory, Func<string, bool>? isTaken = null)
    {
        isTaken ??= path => File.Exists(path) || Directory.Exists(path) || IsDanglingLink(path);
        if (!isTaken(Path.Combine(directory, name)))
            return name;

        var (stem, ext) = Split(name, isDirectory);
        for (var n = 1; n < 100_000; n++)
        {
            var candidate = $"{stem} ({n}){ext}";
            if (!isTaken(Path.Combine(directory, candidate)))
                return candidate;
        }
        return $"{stem} ({Guid.NewGuid():N}){ext}";
    }

    /// <summary>Splits a name into stem and extension. Folders and dot-files (".bashrc") have no extension.</summary>
    public static (string Stem, string Extension) Split(string name, bool isDirectory)
    {
        if (isDirectory)
            return (name, "");
        var ext = Path.GetExtension(name);
        var stem = Path.GetFileNameWithoutExtension(name);
        if (string.IsNullOrEmpty(stem))
            return (name, "");
        return (stem, ext);
    }

    private static bool IsDanglingLink(string path)
    {
        try
        {
            return new FileInfo(path).LinkTarget is not null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }
}
