using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Slotty.Core.Platform;

namespace Slotty.App.Platform.Windows;

/// <summary>RegisterHotKey on a dedicated thread with its own message loop (no window needed).</summary>
[SupportedOSPlatform("windows")]
public sealed class Win32Hotkey : IGlobalHotkeyService
{
    private const int HotkeyId = 0x5107;
    private Thread? _thread;
    private uint _threadId;

    public bool IsSupported => true;

    public event EventHandler? Pressed;

    public string? Register(HotkeyGesture gesture)
    {
        Unregister();
        if (VirtualKey(gesture.Key) is not { } vk)
            return $"The key \"{gesture.Key}\" is not supported.";

        uint mods = Win32Native.MOD_NOREPEAT;
        if (gesture.Modifiers.HasFlag(HotkeyModifiers.Ctrl))
            mods |= Win32Native.MOD_CONTROL;
        if (gesture.Modifiers.HasFlag(HotkeyModifiers.Alt))
            mods |= Win32Native.MOD_ALT;
        if (gesture.Modifiers.HasFlag(HotkeyModifiers.Shift))
            mods |= Win32Native.MOD_SHIFT;
        if (gesture.Modifiers.HasFlag(HotkeyModifiers.Super))
            mods |= Win32Native.MOD_WIN;

        string? error = null;
        using var ready = new ManualResetEventSlim();
        _thread = new Thread(() =>
        {
            _threadId = Win32Native.GetCurrentThreadId();
            // Create the message queue before signalling, so PostThreadMessage can reach it.
            Win32Native.PeekMessage(out _, 0, 0, 0, 0);
            if (!Win32Native.RegisterHotKey(0, HotkeyId, mods, vk))
            {
                error = $"{gesture} is already used by another program (error {Marshal.GetLastPInvokeError()}).";
                ready.Set();
                return;
            }
            ready.Set();
            while (Win32Native.GetMessage(out var msg, 0, 0, 0) > 0)
            {
                if (msg.message == Win32Native.WM_HOTKEY && (int)msg.wParam == HotkeyId)
                    Pressed?.Invoke(this, EventArgs.Empty);
                else if (msg.message == Win32Native.WM_APP_STOP)
                    break;
            }
            Win32Native.UnregisterHotKey(0, HotkeyId);
        })
        { IsBackground = true, Name = "Slotty hotkey" };
        _thread.Start();
        ready.Wait(TimeSpan.FromSeconds(5));
        if (error is not null)
            _thread = null;
        return error;
    }

    public void Unregister()
    {
        if (_thread is null)
            return;
        Win32Native.PostThreadMessage(_threadId, Win32Native.WM_APP_STOP, 0, 0);
        _thread.Join(TimeSpan.FromSeconds(2));
        _thread = null;
    }

    public void Dispose() => Unregister();

    private static uint? VirtualKey(string key)
    {
        if (key.Length == 1 && char.IsAsciiLetterUpper(key[0]))
            return key[0];
        if (key.Length == 1 && char.IsAsciiDigit(key[0]))
            return key[0];
        if (key[0] == 'F' && int.TryParse(key.AsSpan(1), out var f))
            return (uint)(0x70 + f - 1);
        return key switch
        {
            "Space" => 0x20,
            "Enter" => 0x0D,
            "Tab" => 0x09,
            "Escape" => 0x1B,
            "Backspace" => 0x08,
            "Insert" => 0x2D,
            "Delete" => 0x2E,
            "Home" => 0x24,
            "End" => 0x23,
            "PageUp" => 0x21,
            "PageDown" => 0x22,
            "Left" => 0x25,
            "Up" => 0x26,
            "Right" => 0x27,
            "Down" => 0x28,
            _ => null,
        };
    }
}
