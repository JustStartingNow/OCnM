using System.Runtime.InteropServices;

namespace Slotty.App.Platform.Linux;

/// <summary>
/// Minimal libxcb bindings. XCB is used instead of Xlib because Xlib's error handler is process-global
/// and owned by Avalonia's X11 backend; XCB reports errors per request.
/// </summary>
internal static unsafe partial class Xcb
{
    private const string Lib = "libxcb.so.1";

    public const ushort ShiftMask = 1;
    public const ushort LockMask = 2;
    public const ushort ControlMask = 4;
    public const ushort Mod1Mask = 8;   // Alt
    public const ushort Mod2Mask = 16;  // NumLock
    public const ushort Mod4Mask = 64;  // Super
    public const byte GrabModeAsync = 1;
    public const byte KeyPress = 2;

    [StructLayout(LayoutKind.Sequential)]
    public struct ScreenIterator
    {
        public nint Data;
        public int Rem;
        public int Index;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct Cookie
    {
        public uint Sequence;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct PollFd
    {
        public int Fd;
        public short Events;
        public short REvents;
    }

    [LibraryImport(Lib, EntryPoint = "xcb_connect")]
    public static partial nint Connect(nint displayName, out int screen);

    [LibraryImport(Lib, EntryPoint = "xcb_connection_has_error")]
    public static partial int ConnectionHasError(nint c);

    [LibraryImport(Lib, EntryPoint = "xcb_disconnect")]
    public static partial void Disconnect(nint c);

    [LibraryImport(Lib, EntryPoint = "xcb_get_setup")]
    public static partial nint GetSetup(nint c);

    [LibraryImport(Lib, EntryPoint = "xcb_setup_roots_iterator")]
    public static partial ScreenIterator SetupRootsIterator(nint setup);

    [LibraryImport(Lib, EntryPoint = "xcb_get_keyboard_mapping")]
    public static partial Cookie GetKeyboardMapping(nint c, byte firstKeycode, byte count);

    [LibraryImport(Lib, EntryPoint = "xcb_get_keyboard_mapping_reply")]
    public static partial nint GetKeyboardMappingReply(nint c, Cookie cookie, nint error);

    [LibraryImport(Lib, EntryPoint = "xcb_get_keyboard_mapping_keysyms")]
    public static partial uint* GetKeyboardMappingKeysyms(nint reply);

    [LibraryImport(Lib, EntryPoint = "xcb_get_keyboard_mapping_keysyms_length")]
    public static partial int GetKeyboardMappingKeysymsLength(nint reply);

    [LibraryImport(Lib, EntryPoint = "xcb_grab_key_checked")]
    public static partial Cookie GrabKeyChecked(nint c, byte ownerEvents, uint window, ushort modifiers, byte key, byte pointerMode, byte keyboardMode);

    [LibraryImport(Lib, EntryPoint = "xcb_ungrab_key")]
    public static partial Cookie UngrabKey(nint c, byte key, uint window, ushort modifiers);

    [LibraryImport(Lib, EntryPoint = "xcb_request_check")]
    public static partial nint RequestCheck(nint c, Cookie cookie);

    [LibraryImport(Lib, EntryPoint = "xcb_flush")]
    public static partial int Flush(nint c);

    [LibraryImport(Lib, EntryPoint = "xcb_poll_for_event")]
    public static partial nint PollForEvent(nint c);

    [LibraryImport(Lib, EntryPoint = "xcb_get_file_descriptor")]
    public static partial int GetFileDescriptor(nint c);

    [LibraryImport(Lib, EntryPoint = "xcb_query_pointer")]
    public static partial Cookie QueryPointer(nint c, uint window);

    [LibraryImport(Lib, EntryPoint = "xcb_query_pointer_reply")]
    public static partial nint QueryPointerReply(nint c, Cookie cookie, nint error);

    [LibraryImport("libc", EntryPoint = "free")]
    public static partial void Free(nint ptr);

    [LibraryImport("libc", EntryPoint = "poll", SetLastError = true)]
    public static partial int Poll(PollFd* fds, nuint count, int timeoutMs);
}

/// <summary>One XCB connection shared by the X11 cursor locator and hotkey.</summary>
internal sealed unsafe class XcbConnection : IDisposable
{
    private XcbConnection(nint handle, uint root, byte minKeycode, byte maxKeycode)
    {
        Handle = handle;
        Root = root;
        MinKeycode = minKeycode;
        MaxKeycode = maxKeycode;
    }

    public nint Handle { get; private set; }

    public uint Root { get; }

    public byte MinKeycode { get; }

    public byte MaxKeycode { get; }

    public static XcbConnection? TryOpen()
    {
        try
        {
            var c = Xcb.Connect(0, out var screenNumber);
            if (c == 0 || Xcb.ConnectionHasError(c) != 0)
            {
                if (c != 0)
                    Xcb.Disconnect(c);
                return null;
            }
            var setup = Xcb.GetSetup(c);
            var it = Xcb.SetupRootsIterator(setup);
            // xcb_screen_t starts with the root window id; screens follow each other but have variable size,
            // so only the default (first) screen is used when the screen number is 0.
            if (it.Rem == 0 || screenNumber != 0 && screenNumber >= it.Rem)
            {
                Xcb.Disconnect(c);
                return null;
            }
            var root = *(uint*)it.Data;
            // xcb_setup_t: min_keycode at byte 34, max_keycode at byte 35.
            var min = *((byte*)setup + 34);
            var max = *((byte*)setup + 35);
            return new XcbConnection(c, root, min, max);
        }
        catch (DllNotFoundException)
        {
            return null;
        }
        catch (EntryPointNotFoundException)
        {
            return null;
        }
    }

    public bool TryQueryPointer(out int x, out int y)
    {
        x = y = 0;
        if (Handle == 0)
            return false;
        var reply = Xcb.QueryPointerReply(Handle, Xcb.QueryPointer(Handle, Root), 0);
        if (reply == 0)
            return false;
        // xcb_query_pointer_reply_t: root_x at byte 16, root_y at byte 18 (int16).
        x = *(short*)((byte*)reply + 16);
        y = *(short*)((byte*)reply + 18);
        Xcb.Free(reply);
        return true;
    }

    /// <summary>All keycodes whose first two columns produce the keysym.</summary>
    public List<byte> KeycodesFor(uint keysym)
    {
        var result = new List<byte>();
        var count = (byte)(MaxKeycode - MinKeycode + 1);
        var reply = Xcb.GetKeyboardMappingReply(Handle, Xcb.GetKeyboardMapping(Handle, MinKeycode, count), 0);
        if (reply == 0)
            return result;
        try
        {
            var perKeycode = *((byte*)reply + 1);
            var syms = Xcb.GetKeyboardMappingKeysyms(reply);
            var length = Xcb.GetKeyboardMappingKeysymsLength(reply);
            if (perKeycode == 0)
                return result;
            for (var i = 0; i * perKeycode < length; i++)
            {
                for (var col = 0; col < Math.Min(2, (int)perKeycode); col++)
                {
                    if (syms[i * perKeycode + col] == keysym)
                    {
                        result.Add((byte)(MinKeycode + i));
                        break;
                    }
                }
            }
        }
        finally
        {
            Xcb.Free(reply);
        }
        return result;
    }

    public void Dispose()
    {
        if (Handle != 0)
            Xcb.Disconnect(Handle);
        Handle = 0;
    }
}
