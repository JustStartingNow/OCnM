using System.Text;
using OCnM.Core.Platform;

namespace OCnM.App.Platform.Linux;

/// <summary>~/.config/autostart/ocnm.desktop (XDG autostart spec).</summary>
internal sealed class XdgAutostart : IAutostartService
{
    private readonly string _autostartDir;

    /// <param name="configHome">Defaults to $XDG_CONFIG_HOME or ~/.config (tests pass a temp folder).</param>
    public XdgAutostart(string? configHome = null)
    {
        if (string.IsNullOrEmpty(configHome))
            configHome = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME");
        if (string.IsNullOrEmpty(configHome))
            configHome = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".config");
        _autostartDir = Path.Combine(configHome, "autostart");
    }

    public string EntryPath => Path.Combine(_autostartDir, "ocnm.desktop");

    /// <summary>The entry written when the app was called Slotty.</summary>
    public string LegacyEntryPath => Path.Combine(_autostartDir, "slotty.desktop");

    public bool IsSupported => true;

    public bool IsEnabled() => File.Exists(EntryPath);

    public string? SetEnabled(bool enabled)
    {
        try
        {
            if (!enabled)
            {
                File.Delete(EntryPath);
                return null;
            }

            var (exe, args) = LaunchCommand.Current();
            var exec = string.Join(' ', new[] { exe }.Concat(args).Append("--hide").Select(Quote));
            var entry = new StringBuilder()
                .AppendLine("[Desktop Entry]")
                .AppendLine("Type=Application")
                .AppendLine("Name=OCnM")
                .AppendLine("Comment=Donut folder launcher and drop target")
                .AppendLine($"Exec={exec}")
                .AppendLine("Terminal=false")
                .AppendLine("X-GNOME-Autostart-enabled=true")
                .ToString();
            Directory.CreateDirectory(Path.GetDirectoryName(EntryPath)!);
            File.WriteAllText(EntryPath, entry);
            return null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return $"Could not update {EntryPath}: {ex.Message}";
        }
    }

    public bool RemoveLegacyEntry(out string? error)
    {
        error = null;
        try
        {
            if (!File.Exists(LegacyEntryPath))
                return false;
            File.Delete(LegacyEntryPath);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            error = $"Could not remove {LegacyEntryPath}: {ex.Message}";
            return false;
        }
    }

    /// <summary>
    /// Desktop Entry Exec quoting. Inside quotes, ", `, $ and \ get a backslash; then the whole value is a
    /// desktop-file string where every backslash is doubled again. So `"` becomes `\\"` and `\` becomes `\\\\`.
    /// </summary>
    internal static string Quote(string arg)
    {
        if (arg.Length > 0 && arg.All(c => char.IsLetterOrDigit(c) || "-_./=+:".Contains(c)))
            return arg;
        var escaped = new StringBuilder("\"");
        foreach (var c in arg)
        {
            if (c == '\\')
                escaped.Append(@"\\\\");
            else if (c is '"' or '`' or '$')
                escaped.Append(@"\\").Append(c);
            else
                escaped.Append(c);
        }
        return escaped.Append('"').ToString();
    }
}
