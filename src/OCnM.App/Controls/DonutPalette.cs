using Avalonia.Media;

namespace OCnM.App.Controls;

/// <summary>Colors for the donut in one theme.</summary>
public sealed record DonutPalette(
    Color Surface,
    Color SurfaceStroke,
    Color Hub,
    Color Text,
    Color MutedText,
    Color EmptyFill,
    Color EmptyStroke,
    Color Accent,
    Color Warning,
    Color Success,
    Color Error)
{
    public static readonly DonutPalette Light = new(
        Surface: Color.Parse("#EEF6F6FA"),
        SurfaceStroke: Color.Parse("#D5D5E0"),
        Hub: Color.Parse("#FFFFFF"),
        Text: Color.Parse("#1B1B24"),
        MutedText: Color.Parse("#5D5D6E"),
        EmptyFill: Color.Parse("#0C1E1E28"),
        EmptyStroke: Color.Parse("#9A9AAA"),
        Accent: Color.Parse("#5254D0"),
        Warning: Color.Parse("#D97706"),
        Success: Color.Parse("#16A34A"),
        Error: Color.Parse("#DC2626"));

    public static readonly DonutPalette Dark = new(
        Surface: Color.Parse("#F01E1E26"),
        SurfaceStroke: Color.Parse("#3A3A48"),
        Hub: Color.Parse("#26262F"),
        Text: Color.Parse("#ECECF3"),
        MutedText: Color.Parse("#A3A3B5"),
        EmptyFill: Color.Parse("#14FFFFFF"),
        EmptyStroke: Color.Parse("#70707F"),
        Accent: Color.Parse("#8B8BF5"),
        Warning: Color.Parse("#F59E0B"),
        Success: Color.Parse("#22C55E"),
        Error: Color.Parse("#EF4444"));

    /// <summary>Swatches offered by "Change color".</summary>
    public static readonly IReadOnlyList<(string Name, string Hex)> Swatches =
    [
        ("Indigo", "#5B5BD6"), ("Blue", "#2F80ED"), ("Teal", "#0EA5A4"), ("Green", "#22A355"),
        ("Lime", "#84B814"), ("Amber", "#E3A008"), ("Orange", "#EA6A1E"), ("Red", "#E0393E"),
        ("Pink", "#DB2F87"), ("Purple", "#9B4DE0"), ("Brown", "#9A6B44"), ("Slate", "#64748B"),
    ];

    public static Color WithAlpha(Color c, double alpha) =>
        Color.FromArgb((byte)Math.Clamp(Math.Round(c.A * alpha), 0, 255), c.R, c.G, c.B);
}
