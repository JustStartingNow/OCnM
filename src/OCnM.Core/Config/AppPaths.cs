namespace OCnM.Core.Config;

/// <summary>Where OCnM keeps its files. OCNM_CONFIG_DIR (or the legacy SLOTTY_CONFIG_DIR) overrides the default.</summary>
public static class AppPaths
{
    /// <summary>True when the config directory comes from an environment variable instead of the OS default.</summary>
    public static bool IsOverridden => EnvVars.Read(EnvVars.ConfigDir) is not null;

    public static string ConfigDirectory => EnvVars.Read(EnvVars.ConfigDir) ?? DefaultConfigDirectory;

    /// <summary>~/.config/ocnm on Linux, %APPDATA%\OCnM on Windows, ~/Library/Application Support/OCnM on macOS.</summary>
    public static string DefaultConfigDirectory => Path.Combine(AppDataRoot, UsesTitleCase ? "OCnM" : "ocnm");

    /// <summary>Where the app kept its settings when it was called Slotty (read once, for migration).</summary>
    public static string LegacyConfigDirectory => Path.Combine(AppDataRoot, UsesTitleCase ? "Slotty" : "slotty");

    public static string ConfigFile => Path.Combine(ConfigDirectory, "config.json");

    public static string LogFile => Path.Combine(ConfigDirectory, "ocnm.log");

    private static bool UsesTitleCase => OperatingSystem.IsWindows() || OperatingSystem.IsMacOS();

    private static string AppDataRoot
    {
        get
        {
            // ApplicationData maps to %APPDATA% on Windows, $XDG_CONFIG_HOME (or ~/.config) on Linux
            // and ~/Library/Application Support on macOS.
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            return string.IsNullOrEmpty(appData)
                ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".config")
                : appData;
        }
    }
}
