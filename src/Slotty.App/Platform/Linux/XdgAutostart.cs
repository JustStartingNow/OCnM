using System.Text;
using Slotty.Core.Platform;

namespace Slotty.App.Platform.Linux;

/// <summary>~/.config/autostart/slotty.desktop (XDG autostart spec).</summary>
internal sealed class XdgAutostart : IAutostartService
{
    private static string EntryPath
    {
        get
        {
            var configHome = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME");
            if (string.IsNullOrEmpty(configHome))
                configHome = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".config");
            return Path.Combine(configHome, "autostart", "slotty.desktop");
        }
    }

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
                .AppendLine("Name=Slotty")
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
