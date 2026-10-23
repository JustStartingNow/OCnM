using System.ComponentModel;
using System.Diagnostics;
using OCnM.App.Platform.Linux;
using OCnM.Core.Platform;

namespace OCnM.App.Platform;

/// <summary>
/// Opens folders in the system file manager: File Explorer on Windows, Finder on macOS, xdg-open elsewhere.
/// With <c>preferNewTab</c>, Linux asks an open Dolphin window for a tab, and Windows goes through the shell so that
/// Windows 11's "open external folders in a new tab" option applies. Anything else opens a new window.
/// </summary>
public sealed class SystemFolderOpener : IFolderOpener
{
    public async Task<string?> OpenFolderAsync(string path, bool preferNewTab)
    {
        if (!Directory.Exists(path))
            return $"The folder no longer exists: {path}";

        if (preferNewTab)
        {
            if (OperatingSystem.IsLinux() && await DolphinTabs.TryOpenInTabAsync(path).ConfigureAwait(false))
                return null;
            if (OperatingSystem.IsWindows() && TryShellOpen(path))
                return null;
        }

        var program = OperatingSystem.IsWindows() ? "explorer.exe" : OperatingSystem.IsMacOS() ? "open" : "xdg-open";
        var psi = new ProcessStartInfo(program) { UseShellExecute = false };
        psi.ArgumentList.Add(path);
        try
        {
            using var process = Process.Start(psi);
            return process is null ? $"{program} did not start." : null;
        }
        catch (Win32Exception ex)
        {
            return OperatingSystem.IsLinux()
                ? $"Could not run xdg-open ({ex.Message}). Install xdg-utils."
                : $"Could not open the file manager ({ex.Message}).";
        }
    }

    /// <summary>
    /// Opening the folder itself through the shell is the "external folder link" route that Windows 11 can send to a
    /// new tab of an open Explorer window (Folder Options). Windows 10 has no tabs and opens a new window.
    /// </summary>
    private static bool TryShellOpen(string path)
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
            return true;
        }
        catch (Win32Exception ex)
        {
            Services.Log.Warn($"Shell open failed for {path}: {ex.Message}; using explorer.exe.");
            return false;
        }
    }
}
