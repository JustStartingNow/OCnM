using System.Runtime.Versioning;
using OCnM.Core.Geometry;
using OCnM.Core.Platform;

namespace OCnM.App.Platform.Windows;

[SupportedOSPlatform("windows")]
public sealed class Win32Cursor : ICursorLocator
{
    public bool TryGetCursorPosition(out ScreenPoint position)
    {
        if (Win32Native.GetCursorPos(out var p))
        {
            position = new ScreenPoint(p.X, p.Y);
            return true;
        }
        position = default;
        return false;
    }
}
