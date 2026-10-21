using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Slotty.App.Controls;
using Slotty.Core.Geometry;

namespace Slotty.App.Platform;

/// <summary>The app icon is drawn at runtime (a small 9-slice donut), so no binary assets are needed.</summary>
public static class AppIcon
{
    private static readonly string[] Colors =
        ["#5B5BD6", "#2F80ED", "#0EA5A4", "#22A355", "#E3A008", "#EA6A1E", "#E0393E", "#DB2F87", "#9B4DE0"];

    public static WindowIcon Create(int size = 64)
    {
        var bitmap = new RenderTargetBitmap(new PixelSize(size, size), new Vector(96, 96));
        using (var ctx = bitmap.CreateDrawingContext())
        {
            var c = new Point(size / 2.0, size / 2.0);
            var outer = size / 2.0 - 1;
            var inner = outer * 0.45;
            var sweep = 360.0 / Colors.Length;
            for (var i = 0; i < Colors.Length; i++)
            {
                var geometry = new StreamGeometry();
                using (var g = geometry.Open())
                {
                    Point At(double deg, double r)
                    {
                        var (x, y) = DonutGeometry.PointAt(deg, r);
                        return new Point(c.X + x, c.Y + y);
                    }
                    var a0 = i * sweep + 2;
                    var a1 = (i + 1) * sweep - 2;
                    g.BeginFigure(At(a0, outer), true);
                    g.ArcTo(At(a1, outer), new Size(outer, outer), 0, false, SweepDirection.Clockwise);
                    g.LineTo(At(a1, inner));
                    g.ArcTo(At(a0, inner), new Size(inner, inner), 0, false, SweepDirection.CounterClockwise);
                    g.EndFigure(true);
                }
                ctx.DrawGeometry(new SolidColorBrush(Color.Parse(Colors[i])), null, geometry);
            }
            ctx.DrawEllipse(new SolidColorBrush(DonutPalette.Light.Accent), null, c, inner * 0.55, inner * 0.55);
        }
        return new WindowIcon(bitmap);
    }
}
