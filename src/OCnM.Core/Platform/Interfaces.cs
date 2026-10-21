using OCnM.Core.Geometry;
using OCnM.Core.Ipc;

namespace OCnM.Core.Platform;

public enum SessionKind
{
    Windows,
    MacOS,
    LinuxX11,
    LinuxWayland,
    Other,
}

/// <summary>Opens a folder in the system file manager.</summary>
public interface IFolderOpener
{
    /// <returns>Null on success, otherwise a user-facing error.</returns>
    string? OpenFolder(string path);
}

/// <summary>Start-at-login registration.</summary>
public interface IAutostartService
{
    bool IsSupported { get; }

    bool IsEnabled();

    /// <returns>Null on success, otherwise a user-facing error.</returns>
    string? SetEnabled(bool enabled);
}

/// <summary>A system-wide keyboard shortcut. Only Windows and X11 can offer one; Wayland compositors own global keys.</summary>
public interface IGlobalHotkeyService : IDisposable
{
    bool IsSupported { get; }

    /// <summary>Raised on a background thread.</summary>
    event EventHandler? Pressed;

    /// <returns>Null on success, otherwise a user-facing error (for example, the key is taken).</returns>
    string? Register(HotkeyGesture gesture);

    void Unregister();
}

/// <summary>Reads the global pointer position. Unsupported on Wayland, where clients can't see the global cursor.</summary>
public interface ICursorLocator
{
    bool TryGetCursorPosition(out ScreenPoint position);
}

/// <summary>System tray / StatusNotifierItem. Never the only way into the app.</summary>
public interface ITrayService : IDisposable
{
    event EventHandler? ToggleRequested;

    event EventHandler? SettingsRequested;

    event EventHandler? QuitRequested;

    event EventHandler<bool>? AutostartToggled;

    void SetVisible(bool visible);

    void SetAutostartChecked(bool isChecked);
}

/// <summary>Single-instance channel: a Unix domain socket on Linux/macOS, a named pipe on Windows.</summary>
public interface IInstanceChannel : IDisposable
{
    /// <summary>Sends a command to a running instance. Returns false when no instance answers.</summary>
    Task<bool> TrySendAsync(IpcCommand command, TimeSpan timeout, CancellationToken cancellationToken = default);

    /// <summary>Starts listening. Returns false if another instance already owns the channel.</summary>
    bool TryStartServer(Func<IpcCommand, Task> handler);
}
