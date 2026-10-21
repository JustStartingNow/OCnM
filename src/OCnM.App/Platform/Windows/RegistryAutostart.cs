using System.Runtime.Versioning;
using Microsoft.Win32;
using OCnM.Core.Platform;

namespace OCnM.App.Platform.Windows;

/// <summary>HKCU\Software\Microsoft\Windows\CurrentVersion\Run\OCnM.</summary>
[SupportedOSPlatform("windows")]
public sealed class RegistryAutostart : IAutostartService
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "OCnM";

    /// <summary>The value written when the app was called Slotty.</summary>
    private const string LegacyValueName = "Slotty";

    public bool IsSupported => true;

    public bool IsEnabled()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey);
        return key?.GetValue(ValueName) is string;
    }

    public string? SetEnabled(bool enabled)
    {
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(RunKey, writable: true);
            if (enabled)
            {
                var (exe, args) = LaunchCommand.Current();
                var command = string.Join(' ', new[] { exe }.Concat(args).Append("--hide").Select(a => a.Contains(' ') ? $"\"{a}\"" : a));
                key.SetValue(ValueName, command);
            }
            else
            {
                key.DeleteValue(ValueName, throwOnMissingValue: false);
            }
            return null;
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException or IOException)
        {
            return $"Could not change the startup entry: {ex.Message}";
        }
    }

    public bool RemoveLegacyEntry(out string? error)
    {
        error = null;
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: true);
            if (key?.GetValue(LegacyValueName) is null)
                return false;
            key.DeleteValue(LegacyValueName, throwOnMissingValue: false);
            return true;
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException or IOException)
        {
            error = $"Could not remove the old startup entry: {ex.Message}";
            return false;
        }
    }
}
