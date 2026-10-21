using System.Runtime.Versioning;
using Microsoft.Win32;
using Slotty.Core.Platform;

namespace Slotty.App.Platform.Windows;

/// <summary>HKCU\Software\Microsoft\Windows\CurrentVersion\Run\Slotty.</summary>
[SupportedOSPlatform("windows")]
public sealed class RegistryAutostart : IAutostartService
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "Slotty";

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
}
