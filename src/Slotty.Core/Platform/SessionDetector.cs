namespace Slotty.Core.Platform;

public static class SessionDetector
{
    public static SessionKind Detect() => Detect(Environment.GetEnvironmentVariable);

    /// <summary>Linux sessions are told apart by WAYLAND_DISPLAY / XDG_SESSION_TYPE, then DISPLAY.</summary>
    public static SessionKind Detect(Func<string, string?> env)
    {
        if (OperatingSystem.IsWindows())
            return SessionKind.Windows;
        if (OperatingSystem.IsMacOS())
            return SessionKind.MacOS;
        if (!OperatingSystem.IsLinux() && !OperatingSystem.IsFreeBSD())
            return SessionKind.Other;
        return DetectUnix(env);
    }

    internal static SessionKind DetectUnix(Func<string, string?> env)
    {
        if (!string.IsNullOrEmpty(env("WAYLAND_DISPLAY")) ||
            string.Equals(env("XDG_SESSION_TYPE"), "wayland", StringComparison.OrdinalIgnoreCase))
            return SessionKind.LinuxWayland;
        if (!string.IsNullOrEmpty(env("DISPLAY")))
            return SessionKind.LinuxX11;
        return SessionKind.Other;
    }

    public static bool IsNiri() => !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("NIRI_SOCKET"));
}
