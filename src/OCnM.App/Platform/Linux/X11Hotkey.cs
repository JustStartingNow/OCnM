using OCnM.Core.Platform;

namespace OCnM.App.Platform.Linux;

/// <summary>Global hotkey on X11 via XGrabKey on the root window. Not available on Wayland by design.</summary>
internal sealed unsafe class X11Hotkey(XcbConnection connection) : IGlobalHotkeyService
{
    private static readonly ushort[] IgnoredCombos = [0, Xcb.LockMask, Xcb.Mod2Mask, Xcb.LockMask | Xcb.Mod2Mask];
    private readonly List<(byte Keycode, ushort Modifiers)> _grabs = [];
    private Thread? _thread;
    private volatile bool _stop;

    public bool IsSupported => true;

    public event EventHandler? Pressed;

    public string? Register(HotkeyGesture gesture)
    {
        Unregister();
        if (Keysym(gesture.Key) is not { } keysym)
            return $"The key \"{gesture.Key}\" is not supported.";
        var keycodes = connection.KeycodesFor(keysym);
        if (keycodes.Count == 0)
            return $"The key \"{gesture.Key}\" is not on this keyboard layout.";

        ushort mods = 0;
        if (gesture.Modifiers.HasFlag(HotkeyModifiers.Ctrl))
            mods |= Xcb.ControlMask;
        if (gesture.Modifiers.HasFlag(HotkeyModifiers.Alt))
            mods |= Xcb.Mod1Mask;
        if (gesture.Modifiers.HasFlag(HotkeyModifiers.Shift))
            mods |= Xcb.ShiftMask;
        if (gesture.Modifiers.HasFlag(HotkeyModifiers.Super))
            mods |= Xcb.Mod4Mask;

        foreach (var keycode in keycodes)
        {
            // Grab with and without CapsLock/NumLock so the shortcut works regardless of their state.
            foreach (var extra in IgnoredCombos)
            {
                var cookie = Xcb.GrabKeyChecked(connection.Handle, 1, connection.Root, (ushort)(mods | extra), keycode, Xcb.GrabModeAsync, Xcb.GrabModeAsync);
                var error = Xcb.RequestCheck(connection.Handle, cookie);
                if (error != 0)
                {
                    Xcb.Free(error);
                    Unregister();
                    return $"{gesture} is already used by another program.";
                }
                _grabs.Add((keycode, (ushort)(mods | extra)));
            }
        }
        Xcb.Flush(connection.Handle);

        _stop = false;
        var wanted = mods;
        var codes = keycodes.ToHashSet();
        _thread = new Thread(() => EventLoop(codes, wanted)) { IsBackground = true, Name = "Slotty X11 hotkey" };
        _thread.Start();
        return null;
    }

    private void EventLoop(HashSet<byte> keycodes, ushort mods)
    {
        var fd = Xcb.GetFileDescriptor(connection.Handle);
        const ushort relevant = Xcb.ShiftMask | Xcb.ControlMask | Xcb.Mod1Mask | Xcb.Mod4Mask;
        while (!_stop)
        {
            var pfd = new Xcb.PollFd { Fd = fd, Events = 1 /* POLLIN */ };
            Xcb.Poll(&pfd, 1, 250);
            nint ev;
            while (!_stop && (ev = Xcb.PollForEvent(connection.Handle)) != 0)
            {
                // xcb_key_press_event_t: response_type at 0, detail (keycode) at 1, state at 28.
                var type = *(byte*)ev & 0x7f;
                var keycode = *((byte*)ev + 1);
                var state = *(ushort*)((byte*)ev + 28);
                Xcb.Free(ev);
                if (type == Xcb.KeyPress && keycodes.Contains(keycode) && (state & relevant) == mods)
                    Pressed?.Invoke(this, EventArgs.Empty);
            }
            if (Xcb.ConnectionHasError(connection.Handle) != 0)
                return;
        }
    }

    public void Unregister()
    {
        _stop = true;
        _thread?.Join(TimeSpan.FromSeconds(1));
        _thread = null;
        foreach (var (keycode, modifiers) in _grabs)
            Xcb.UngrabKey(connection.Handle, keycode, connection.Root, modifiers);
        if (_grabs.Count > 0)
            Xcb.Flush(connection.Handle);
        _grabs.Clear();
    }

    public void Dispose() => Unregister();

    private static uint? Keysym(string key)
    {
        if (key.Length == 1 && char.IsAsciiLetterUpper(key[0]))
            return char.ToLowerInvariant(key[0]);
        if (key.Length == 1 && char.IsAsciiDigit(key[0]))
            return key[0];
        if (key[0] == 'F' && int.TryParse(key.AsSpan(1), out var f))
            return (uint)(0xffbe + f - 1);
        return key switch
        {
            "Space" => 0x20,
            "Enter" => 0xff0d,
            "Tab" => 0xff09,
            "Escape" => 0xff1b,
            "Backspace" => 0xff08,
            "Insert" => 0xff63,
            "Delete" => 0xffff,
            "Home" => 0xff50,
            "End" => 0xff57,
            "PageUp" => 0xff55,
            "PageDown" => 0xff56,
            "Left" => 0xff51,
            "Up" => 0xff52,
            "Right" => 0xff53,
            "Down" => 0xff54,
            _ => null,
        };
    }
}
