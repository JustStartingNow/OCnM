using Slotty.Core.Geometry;
using Slotty.Core.Platform;

namespace Slotty.App.Platform.Linux;

internal sealed class X11Cursor(XcbConnection connection) : ICursorLocator
{
    public bool TryGetCursorPosition(out ScreenPoint position)
    {
        if (connection.TryQueryPointer(out var x, out var y))
        {
            position = new ScreenPoint(x, y);
            return true;
        }
        position = default;
        return false;
    }
}
