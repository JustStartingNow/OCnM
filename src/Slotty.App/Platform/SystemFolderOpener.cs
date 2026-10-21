using System.ComponentModel;
using System.Diagnostics;
using Slotty.Core.Platform;

namespace Slotty.App.Platform;

/// <summary>explorer on Windows, open on macOS, xdg-open elsewhere.</summary>
public sealed class SystemFolderOpener : IFolderOpener
{
    public string? OpenFolder(string path)
    {
        if (!Directory.Exists(path))
            return $"The folder no longer exists: {path}";

        var program = OperatingSystem.IsWindows() ? "explorer.exe" : OperatingSystem.IsMacOS() ? "open" : "xdg-open";
        var psi = new ProcessStartInfo(program) { UseShellExecute = false };
        // explorer.exe needs a trailing separator for drive roots and handles the rest as-is.
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
}
