namespace OCnM.Core.FileOps;

public static class PathDisplay
{
    /// <summary>
    /// Shortens a path for display: the home folder becomes "~" and, when still too long, the start is
    /// replaced by "…" so the most specific part (the end) stays visible.
    /// </summary>
    public static string Shorten(string path, int maxChars, string? home = null)
    {
        home ??= Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var display = path;
        if (!string.IsNullOrEmpty(home) && home.Length > 1 && PathRules.IsSameOrInside(path, home))
            display = "~" + path[Path.TrimEndingDirectorySeparator(home).Length..];

        if (display.Length <= maxChars || maxChars < 4)
            return display;

        var tail = display[^(maxChars - 1)..];
        // Prefer to start at a separator so no folder name is cut in half.
        var cut = tail.IndexOfAny(['/', '\\']);
        if (cut > 0 && cut < tail.Length - 1)
            tail = tail[cut..];
        return "…" + tail;
    }
}
