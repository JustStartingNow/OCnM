using System.Diagnostics;
using System.Runtime.InteropServices;

namespace OCnM.App.Services;

/// <summary>
/// Shared motion tokens. Animations decelerate (start fast, settle); exits are shorter than enters.
/// <see cref="Reduced"/> turns all of them into instant state changes.
/// </summary>
public static partial class Motion
{
    /// <summary>Hover / focus / drag highlight changes.</summary>
    public const double StateMs = 120;

    /// <summary>The donut appearing: the hub fades in, then the ring is wiped in clockwise from the top.</summary>
    public const double EnterMs = 380;

    /// <summary>The donut disappearing: it fades out inside two counter-rotating arcs that spin away.</summary>
    public const double ExitMs = 240;

    /// <summary>Toast and overlay cards.</summary>
    public const double CardMs = 180;

    public static bool Reduced { get; set; }

    public static double EaseOut(double t) => 1 - Math.Pow(1 - Math.Clamp(t, 0, 1), 3);

    public static double EaseIn(double t) => Math.Pow(Math.Clamp(t, 0, 1), 3);

    public static double EaseInOut(double t)
    {
        t = Math.Clamp(t, 0, 1);
        return t < 0.5 ? 4 * t * t * t : 1 - Math.Pow(-2 * t + 2, 3) / 2;
    }

    /// <summary>Best-effort read of the OS "reduce animations" preference.</summary>
    public static bool SystemPrefersReducedMotion()
    {
        try
        {
            if (OperatingSystem.IsWindows())
                return SystemParametersInfo(0x1042 /* SPI_GETCLIENTAREAANIMATION */, 0, out var on, 0) && !on;
            if (OperatingSystem.IsLinux())
                return KdeAnimationsOff() || GnomeAnimationsOff();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            Log.Warn($"Could not read the reduced-motion preference: {ex.Message}");
        }
        return false;
    }

    private static bool KdeAnimationsOff()
    {
        var configHome = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME");
        if (string.IsNullOrEmpty(configHome))
            configHome = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".config");
        var path = Path.Combine(configHome, "kdeglobals");
        if (!File.Exists(path))
            return false;
        var line = File.ReadLines(path).FirstOrDefault(l => l.StartsWith("AnimationDurationFactor=", StringComparison.Ordinal));
        return line is not null && double.TryParse(line["AnimationDurationFactor=".Length..],
            System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var factor) && factor == 0;
    }

    private static bool GnomeAnimationsOff()
    {
        var psi = new ProcessStartInfo("gsettings", "get org.gnome.desktop.interface enable-animations")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        using var p = Process.Start(psi);
        if (p is null || !p.WaitForExit(500))
            return false;
        return p.StandardOutput.ReadToEnd().Trim() == "false";
    }

    [LibraryImport("user32.dll", EntryPoint = "SystemParametersInfoW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SystemParametersInfo(uint action, uint param, [MarshalAs(UnmanagedType.Bool)] out bool value, uint winIni);
}
