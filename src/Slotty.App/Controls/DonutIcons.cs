using Avalonia.Media;
using Slotty.Core.Models;

namespace Slotty.App.Controls;

/// <summary>Outline icons on a 24x24 grid, drawn with a stroke.</summary>
public static class DonutIcons
{
    public static readonly Geometry Plus = Parse("M12,5 V19 M5,12 H19");
    public static readonly Geometry Warning = Parse("M12,3 L22,20 H2 Z M12,9.5 V14 M12,16.8 V17.2");
    public static readonly Geometry Check = Parse("M5,12.5 L10,17 L19,7");
    public static readonly Geometry Cross = Parse("M6,6 L18,18 M18,6 L6,18");
    public static readonly Geometry Pin = Parse("M9,3 H15 M10,3 V9 L7,13 H17 L14,9 V3 M12,13 V21");
    public static readonly Geometry Sliders = Parse("M4,7 H20 M4,17 H20 M9,4 V10 M15,14 V20");
    public static readonly Geometry CopyInto = Parse("M12,4 V14 M7.5,9.5 L12,14 L16.5,9.5 M5,17.5 V20 H19 V17.5");
    public static readonly Geometry MoveInto = Parse("M4,12 H17 M12.5,7.5 L17,12 L12.5,16.5 M20,5 V19");
    public static readonly Geometry Info = Parse("M12,3 A9,9 0 1 0 12.01,3 M12,11 V16.5 M12,7.6 V8");
    public static readonly Geometry CheckCircle = Parse("M12,3 A9,9 0 1 0 12.01,3 M8,12.5 L11,15.5 L16.5,9.5");
    public static readonly Geometry ErrorCircle = Parse("M12,3 A9,9 0 1 0 12.01,3 M12,7.5 V13 M12,16.3 V16.7");
    public static readonly Geometry Grip = Parse("M9,6 V6.2 M15,6 V6.2 M9,12 V12.2 M15,12 V12.2 M9,18 V18.2 M15,18 V18.2");
    public static readonly Geometry ChevronUp = Parse("M6,15 L12,9 L18,15");
    public static readonly Geometry ChevronDown = Parse("M6,9 L12,15 L18,9");
    public static readonly Geometry FolderOpen = Parse("M3,7 A2,2 0 0 1 5,5 H9.5 L11.5,7 H19 A2,2 0 0 1 21,9 V17 A2,2 0 0 1 19,19 H5 A2,2 0 0 1 3,17 Z M3,11 H21");

    private static readonly Dictionary<SliceIcon, Geometry> SliceIcons = new()
    {
        [SliceIcon.Folder] = Parse("M3,7 A2,2 0 0 1 5,5 H9.5 L11.5,7 H19 A2,2 0 0 1 21,9 V17 A2,2 0 0 1 19,19 H5 A2,2 0 0 1 3,17 Z"),
        [SliceIcon.Documents] = Parse("M6,3 H14 L19,8 V21 H6 Z M14,3 V8 H19 M9,12.5 H16 M9,16.5 H16"),
        [SliceIcon.Images] = Parse("M3,5 H21 V19 H3 Z M3,16 L9,10 L14,15 L17,12 L21,16 M15,8 A1.2,1.2 0 1 0 15.01,8"),
        [SliceIcon.Music] = Parse("M9,18 V5 L20,3 V16 M3,18 A3,3 0 1 0 9,18 A3,3 0 1 0 3,18 M14,16 A3,3 0 1 0 20,16 A3,3 0 1 0 14,16"),
        [SliceIcon.Videos] = Parse("M3,6 H16 V18 H3 Z M16,10.5 L21,7.5 V16.5 L16,13.5"),
        [SliceIcon.Downloads] = Parse("M12,3 V15 M7,10 L12,15 L17,10 M4,17 V20 H20 V17"),
        [SliceIcon.Archive] = Parse("M3,4 H21 V8 H3 Z M5,8 V20 H19 V8 M10,12 H14"),
        [SliceIcon.Code] = Parse("M8,7 L3,12 L8,17 M16,7 L21,12 L16,17 M14,4 L10,20"),
        [SliceIcon.Star] = Parse("M12,3 L14.8,8.8 L21,9.6 L16.5,14 L17.6,20.2 L12,17.2 L6.4,20.2 L7.5,14 L3,9.6 L9.2,8.8 Z"),
        [SliceIcon.Heart] = Parse("M12,20 C12,20 3,14.5 3,8.7 A4.6,4.6 0 0 1 12,7 A4.6,4.6 0 0 1 21,8.7 C21,14.5 12,20 12,20 Z"),
        [SliceIcon.Home] = Parse("M3,11 L12,3 L21,11 M5,9.5 V21 H19 V9.5 M10,21 V15 H14 V21"),
        [SliceIcon.Work] = Parse("M3,8 H21 V20 H3 Z M8,8 V5 H16 V8 M3,13 H21"),
    };

    public static Geometry For(SliceIcon icon) => SliceIcons.TryGetValue(icon, out var g) ? g : SliceIcons[SliceIcon.Folder];

    private static Geometry Parse(string data) => Geometry.Parse(data);

    /// <summary>Draws a 24x24 icon centered on <paramref name="center"/> at the given pixel size.</summary>
    public static void Draw(DrawingContext context, Geometry icon, Avalonia.Point center, double size, IBrush brush, double strokeWidth = 1.8)
    {
        var scale = size / 24.0;
        var matrix = Avalonia.Matrix.CreateTranslation(-12, -12) * Avalonia.Matrix.CreateScale(scale, scale)
            * Avalonia.Matrix.CreateTranslation(center.X, center.Y);
        using (context.PushTransform(matrix))
        {
            var pen = new Pen(brush, strokeWidth / scale, lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Round);
            context.DrawGeometry(null, pen, icon);
        }
    }
}
