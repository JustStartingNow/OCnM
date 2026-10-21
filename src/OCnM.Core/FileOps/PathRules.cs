namespace OCnM.Core.FileOps;

public static class PathRules
{
    /// <summary>Windows and macOS file systems are case-insensitive by default.</summary>
    public static StringComparison PathComparison =>
        OperatingSystem.IsWindows() || OperatingSystem.IsMacOS() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

    /// <summary>Full path without a trailing separator (roots keep theirs).</summary>
    public static string Normalize(string path)
    {
        var full = Path.GetFullPath(path);
        return Path.TrimEndingDirectorySeparator(full);
    }

    public static bool PathsEqual(string a, string b) => string.Equals(Normalize(a), Normalize(b), PathComparison);

    /// <summary>True when <paramref name="candidate"/> is <paramref name="folder"/> or somewhere below it.</summary>
    public static bool IsSameOrInside(string candidate, string folder)
    {
        var c = Normalize(candidate);
        var f = Normalize(folder);
        if (string.Equals(c, f, PathComparison))
            return true;
        var prefix = Path.EndsInDirectorySeparator(f) ? f : f + Path.DirectorySeparatorChar;
        return c.StartsWith(prefix, PathComparison);
    }

    /// <summary>
    /// True when copying/moving folder <paramref name="sourceFolder"/> into <paramref name="destination"/>
    /// would put it inside itself. Symlinks are resolved so a link pointing into the source is caught too.
    /// </summary>
    public static bool WouldNestIntoItself(string sourceFolder, string destination)
    {
        if (IsSameOrInside(destination, sourceFolder))
            return true;
        var realSource = ResolveRealPath(sourceFolder);
        var realDest = ResolveRealPath(destination);
        return IsSameOrInside(realDest, realSource);
    }

    /// <summary>Resolves symlinks in every component of an existing path (like realpath). Best effort.</summary>
    public static string ResolveRealPath(string path)
    {
        var current = Normalize(path);
        try
        {
            for (var hops = 0; hops < 40; hops++)
            {
                var changed = false;
                var root = Path.GetPathRoot(current) ?? "";
                var parts = current[root.Length..].Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries);
                var built = root;
                for (var i = 0; i < parts.Length; i++)
                {
                    built = Path.Combine(built, parts[i]);
                    FileSystemInfo info = Directory.Exists(built) ? new DirectoryInfo(built) : new FileInfo(built);
                    if (info.LinkTarget is { } target)
                    {
                        var parent = Path.GetDirectoryName(built) ?? root;
                        var resolved = Path.IsPathRooted(target) ? target : Path.Combine(parent, target);
                        var rest = string.Join(Path.DirectorySeparatorChar, parts.Skip(i + 1));
                        current = Normalize(rest.Length == 0 ? resolved : Path.Combine(resolved, rest));
                        changed = true;
                        break;
                    }
                }
                if (!changed)
                    return current;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            // Fall through with what we have.
        }
        return current;
    }

    /// <summary>
    /// Whether a rename between the two paths can stay on one file system. Errs towards "no",
    /// which only costs a copy + verify + delete instead of a rename.
    /// </summary>
    public static bool AreOnSameVolume(string a, string b)
    {
        if (OperatingSystem.IsWindows())
        {
            var ra = Path.GetPathRoot(Normalize(a));
            var rb = Path.GetPathRoot(Normalize(b));
            return !string.IsNullOrEmpty(ra) && string.Equals(ra, rb, StringComparison.OrdinalIgnoreCase);
        }

        var ma = MountPointOf(ResolveRealPath(a));
        var mb = MountPointOf(ResolveRealPath(b));
        return ma is not null && string.Equals(ma, mb, StringComparison.Ordinal);
    }

    private static string? MountPointOf(string path)
    {
        string? best = null;
        DriveInfo[] drives;
        try
        {
            drives = DriveInfo.GetDrives();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }

        foreach (var drive in drives)
        {
            var mount = drive.RootDirectory.FullName;
            if (IsSameOrInside(path, mount) && (best is null || mount.Length > best.Length))
                best = mount;
        }
        return best;
    }
}
