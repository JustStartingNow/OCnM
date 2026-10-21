namespace OCnM.Core.Config;

/// <summary>
/// Environment variables. Each OCNM_* name falls back to the SLOTTY_* name the app used before it was renamed,
/// so existing scripts and shell profiles keep working.
/// </summary>
public static class EnvVars
{
    public const string ConfigDir = "OCNM_CONFIG_DIR";
    public const string Backend = "OCNM_BACKEND";
    public const string Solid = "OCNM_SOLID";

    private const string NewPrefix = "OCNM_";
    private const string LegacyPrefix = "SLOTTY_";

    /// <summary>The value of <paramref name="name"/>, else of its legacy SLOTTY_* twin; null when both are empty.</summary>
    public static string? Read(string name) => Read(name, Environment.GetEnvironmentVariable);

    public static string? Read(string name, Func<string, string?> getEnv)
    {
        var value = getEnv(name);
        if (!string.IsNullOrWhiteSpace(value))
            return value;
        if (!name.StartsWith(NewPrefix, StringComparison.Ordinal))
            return null;
        var legacy = getEnv(LegacyPrefix + name[NewPrefix.Length..]);
        return string.IsNullOrWhiteSpace(legacy) ? null : legacy;
    }
}
