using System.Security;
using System.Text;
using OCnM.Core.Platform;

namespace OCnM.App.Platform.Mac;

/// <summary>~/Library/LaunchAgents/app.slotty.plist (best effort).</summary>
public sealed class LaunchAgentAutostart : IAutostartService
{
    private static string PlistPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Library", "LaunchAgents", "app.slotty.plist");

    public bool IsSupported => true;

    public bool IsEnabled() => File.Exists(PlistPath);

    public string? SetEnabled(bool enabled)
    {
        try
        {
            if (!enabled)
            {
                File.Delete(PlistPath);
                return null;
            }

            var (exe, args) = LaunchCommand.Current();
            var program = new StringBuilder();
            foreach (var a in new[] { exe }.Concat(args).Append("--hide"))
                program.AppendLine($"    <string>{SecurityElement.Escape(a)}</string>");
            var plist = $"""
                <?xml version="1.0" encoding="UTF-8"?>
                <!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
                <plist version="1.0">
                <dict>
                  <key>Label</key>
                  <string>app.slotty</string>
                  <key>ProgramArguments</key>
                  <array>
                {program}  </array>
                  <key>RunAtLoad</key>
                  <true/>
                </dict>
                </plist>
                """;
            Directory.CreateDirectory(Path.GetDirectoryName(PlistPath)!);
            File.WriteAllText(PlistPath, plist);
            return null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return $"Could not update {PlistPath}: {ex.Message}";
        }
    }
}
