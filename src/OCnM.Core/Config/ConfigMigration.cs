namespace OCnM.Core.Config;

public enum MigrationOutcome
{
    /// <summary>Nothing to do: the new folder already exists, or there is no old folder.</summary>
    NotNeeded,
    Migrated,
    Failed,
}

public sealed record MigrationResult(MigrationOutcome Outcome, string? Message = null);

/// <summary>
/// One-time copy of the settings folder from the app's old name (Slotty) to the new one (OCnM).
/// The old folder is never modified or deleted, and an existing new folder is never overwritten.
/// </summary>
public static class ConfigMigration
{
    /// <summary>Migrates the OS default location. Skipped when the config folder is set by an environment variable.</summary>
    public static MigrationResult MigrateDefaultLocation() =>
        AppPaths.IsOverridden
            ? new MigrationResult(MigrationOutcome.NotNeeded)
            : Migrate(AppPaths.LegacyConfigDirectory, AppPaths.DefaultConfigDirectory);

    public static MigrationResult Migrate(string legacyDir, string newDir)
    {
        if (Directory.Exists(newDir) || !Directory.Exists(legacyDir))
            return new MigrationResult(MigrationOutcome.NotNeeded);

        // Copy into a private sibling first and rename it into place at the end, so an interrupted copy is never
        // mistaken for a finished migration (the new folder only appears once it is complete).
        var staging = $"{Path.TrimEndingDirectorySeparator(newDir)}.migrating-{Environment.ProcessId}";
        try
        {
            if (Directory.Exists(staging))
                Directory.Delete(staging, recursive: true);
            CopyTree(legacyDir, staging);
            Directory.Move(staging, newDir);
            return new MigrationResult(MigrationOutcome.Migrated, $"Copied settings from {legacyDir} to {newDir}.");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            TryDelete(staging);
            // Another instance may have finished the same migration a moment earlier.
            if (Directory.Exists(newDir))
                return new MigrationResult(MigrationOutcome.NotNeeded);
            return new MigrationResult(MigrationOutcome.Failed, $"Could not copy settings from {legacyDir}: {ex.Message}");
        }
    }

    private static void CopyTree(string source, string target)
    {
        Directory.CreateDirectory(target);
        var options = new EnumerationOptions { AttributesToSkip = 0, RecurseSubdirectories = false };
        foreach (var file in Directory.EnumerateFiles(source, "*", options))
            File.Copy(file, Path.Combine(target, Path.GetFileName(file)), overwrite: false);
        foreach (var dir in Directory.EnumerateDirectories(source, "*", options))
            CopyTree(dir, Path.Combine(target, Path.GetFileName(dir)));
    }

    private static void TryDelete(string dir)
    {
        try
        {
            if (Directory.Exists(dir))
                Directory.Delete(dir, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Leftover staging folder; harmless and retried next start.
        }
    }
}
