using OCnM.App.Platform.Ipc;
using OCnM.App.Platform.Linux;
using OCnM.App.Platform.Mac;
using OCnM.App.Platform.Windows;
using OCnM.Core.Geometry;
using OCnM.Core.Platform;

namespace OCnM.App.Platform;

/// <summary>Picks the per-OS implementation of every platform interface.</summary>
public sealed class PlatformServices : IDisposable
{
    private readonly XcbConnection? _xcb;

    private PlatformServices(SessionKind session)
    {
        Session = session;
        FolderOpener = new SystemFolderOpener();

        if (OperatingSystem.IsWindows())
        {
            Autostart = new RegistryAutostart();
            Hotkey = new Win32Hotkey();
            Cursor = new Win32Cursor();
        }
        else if (OperatingSystem.IsMacOS())
        {
            Autostart = new LaunchAgentAutostart();
            Hotkey = new NoHotkey("Global shortcuts are not available on macOS yet.");
            Cursor = new NoCursor();
        }
        else
        {
            Autostart = new XdgAutostart();
            // Only a real X11 session can grab keys and read the pointer globally. Under Wayland,
            // XWayland would only see input aimed at X11 windows, so these are disabled there.
            _xcb = session == SessionKind.LinuxX11 ? XcbConnection.TryOpen() : null;
            Hotkey = _xcb is not null
                ? new X11Hotkey(_xcb)
                : new NoHotkey("On Wayland, bind \"slotty --toggle\" to a key in your compositor instead.");
            Cursor = _xcb is not null ? new X11Cursor(_xcb) : new NoCursor();
        }
    }

    public SessionKind Session { get; }

    public IFolderOpener FolderOpener { get; }

    public IAutostartService Autostart { get; }

    public IGlobalHotkeyService Hotkey { get; }

    public ICursorLocator Cursor { get; }

    /// <summary>Windows and X11 let a client place its own windows; Wayland compositors don't.</summary>
    public bool CanPositionWindows => Session is SessionKind.Windows or SessionKind.LinuxX11 or SessionKind.MacOS;

    public bool IsWayland => Session == SessionKind.LinuxWayland;

    public static PlatformServices Create() => new(SessionDetector.Detect());

    public static IInstanceChannel CreateInstanceChannel() =>
        OperatingSystem.IsWindows()
            ? new NamedPipeChannel(NamedPipeChannel.DefaultName())
            : new UnixSocketChannel(UnixSocketChannel.DefaultPath());

    public void Dispose()
    {
        Hotkey.Dispose();
        _xcb?.Dispose();
    }

    private sealed class NoHotkey(string reason) : IGlobalHotkeyService
    {
        public bool IsSupported => false;

        public event EventHandler? Pressed
        {
            add { }
            remove { }
        }

        public string? Register(HotkeyGesture gesture) => reason;

        public void Unregister()
        {
        }

        public void Dispose()
        {
        }
    }

    private sealed class NoCursor : ICursorLocator
    {
        public bool TryGetCursorPosition(out ScreenPoint position)
        {
            position = default;
            return false;
        }
    }
}
