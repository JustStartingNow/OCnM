namespace Slotty.Core.Models;

/// <summary>What happens to dropped items.</summary>
public enum DropAction
{
    Copy,
    Move,
}

/// <summary>Per-slice drop action; <see cref="Inherit"/> uses the global default.</summary>
public enum SliceAction
{
    Inherit,
    Copy,
    Move,
}

/// <summary>What to do when the destination already contains an item with the same name.</summary>
public enum ConflictPolicy
{
    AutoRename,
    Skip,
    Ask,
}

public enum ThemePreference
{
    System,
    Light,
    Dark,
}

/// <summary>Which Avalonia backend to use on Linux.</summary>
public enum LinuxBackend
{
    Auto,
    X11,
    Wayland,
}

/// <summary>Built-in slice icons. The App draws them as vector paths.</summary>
public enum SliceIcon
{
    Folder,
    Documents,
    Images,
    Music,
    Videos,
    Downloads,
    Archive,
    Code,
    Star,
    Heart,
    Home,
    Work,
}
