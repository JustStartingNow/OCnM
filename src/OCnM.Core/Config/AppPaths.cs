namespace OCnM.Core.Config;

/// <summary>Where Slotty keeps its files. SLOTTY_CONFIG_DIR overrides the default (used by tests and portable setups).</summary>
public static class AppPaths
{
    public const string ConfigDirEnvVar = "SLOTTY_CONFIG_DIR";

    public static string ConfigDirectory
    {
        get
        {
            var overridden = Environment.GetEnvironmentVariable(ConfigDirEnvVar);
            if (!string.IsNullOrWhiteSpace(overridden))
                return overridden;

            // ApplicationData maps to %APPDATA% on Windows, $XDG_CONFIG_HOME (or ~/.config) on Linux
            // and ~/Library/Application Support on macOS.
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            if (string.IsNullOrEmpty(appData))
                appData = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".config");
            return Path.Combine(appData, OperatingSystem.IsWindows() || OperatingSystem.IsMacOS() ? "Slotty" : "slotty");
        }
    }

    public static string ConfigFile => Path.Combine(ConfigDirectory, "config.json");

    public static string LogFile => Path.Combine(ConfigDirectory, "slotty.log");
}
