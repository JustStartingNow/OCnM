namespace Slotty.Core.Geometry;

public readonly record struct ScreenPoint(int X, int Y);

public readonly record struct ScreenRect(int X, int Y, int Width, int Height)
{
    public int Right => X + Width;

    public int Bottom => Y + Height;

    public bool Contains(ScreenPoint p) => p.X >= X && p.X < Right && p.Y >= Y && p.Y < Bottom;
}

public static class ScreenMath
{
    /// <summary>
    /// Top-left position for a window of the given size centered on <paramref name="center"/>,
    /// moved so it stays inside <paramref name="workArea"/>. Windows larger than the area are pinned to its top-left.
    /// </summary>
    public static ScreenPoint CenterClamped(ScreenPoint center, int width, int height, ScreenRect workArea)
    {
        var x = center.X - width / 2;
        var y = center.Y - height / 2;
        x = Math.Max(workArea.X, Math.Min(x, workArea.Right - width));
        y = Math.Max(workArea.Y, Math.Min(y, workArea.Bottom - height));
        return new ScreenPoint(x, y);
    }
}
