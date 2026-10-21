namespace OCnM.Core.Models;

public sealed record AppSettings
{
    public const string DefaultHotkey = "Ctrl+Alt+Space";
    public const int DefaultHideAfterDropMs = 4000;

    public DropAction DefaultAction { get; init; } = DropAction.Copy;

    public ConflictPolicy ConflictPolicy { get; init; } = ConflictPolicy.AutoRename;

    /// <summary>Global hotkey (Windows and X11 only). Empty disables it.</summary>
    public string Hotkey { get; init; } = DefaultHotkey;

    public ThemePreference Theme { get; init; } = ThemePreference.System;

    public bool LaunchAtLogin { get; init; }

    /// <summary>Hide the donut when its window loses focus (ignored while pinned).</summary>
    public bool HideOnFocusLoss { get; init; } = true;

    /// <summary>Delay before hiding after a successful drop; 0 keeps the donut open.</summary>
    public int HideAfterDropMs { get; init; } = DefaultHideAfterDropMs;

    /// <summary>Keeps the donut open while dragging from another window (useful on Wayland).</summary>
    public bool Pinned { get; init; }

    public bool ShowTrayIcon { get; init; } = true;

    /// <summary>Turns off the donut's open/close and hover animations (also follows the OS setting where detectable).</summary>
    public bool ReduceMotion { get; init; }

    /// <summary>Preferred Linux backend; the --backend flag and SLOTTY_BACKEND override it.</summary>
    public LinuxBackend LinuxBackend { get; init; } = LinuxBackend.Auto;
}
