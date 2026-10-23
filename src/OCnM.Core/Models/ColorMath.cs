using OCnM.Core.Config;

namespace OCnM.Core.Models;

/// <summary>Hue 0..360, saturation and value 0..1.</summary>
public readonly record struct Hsv(double H, double S, double V);

/// <summary>Color conversions for the custom color picker. Pure math, no UI types.</summary>
public static class ColorMath
{
    public static (byte R, byte G, byte B) ToRgb(Hsv c)
    {
        var h = ((c.H % 360) + 360) % 360;
        var s = Math.Clamp(c.S, 0, 1);
        var v = Math.Clamp(c.V, 0, 1);
        var chroma = v * s;
        var x = chroma * (1 - Math.Abs(h / 60 % 2 - 1));
        var m = v - chroma;
        var (r, g, b) = (int)(h / 60) switch
        {
            0 => (chroma, x, 0.0),
            1 => (x, chroma, 0.0),
            2 => (0.0, chroma, x),
            3 => (0.0, x, chroma),
            4 => (x, 0.0, chroma),
            _ => (chroma, 0.0, x),
        };
        return (To8Bit(r + m), To8Bit(g + m), To8Bit(b + m));
    }

    public static Hsv FromRgb(byte r, byte g, byte b)
    {
        double rf = r / 255.0, gf = g / 255.0, bf = b / 255.0;
        var max = Math.Max(rf, Math.Max(gf, bf));
        var min = Math.Min(rf, Math.Min(gf, bf));
        var delta = max - min;
        double h;
        if (delta == 0)
            h = 0;
        else if (max == rf)
            h = 60 * (((gf - bf) / delta % 6 + 6) % 6);
        else if (max == gf)
            h = 60 * ((bf - rf) / delta + 2);
        else
            h = 60 * ((rf - gf) / delta + 4);
        return new Hsv(h, max == 0 ? 0 : delta / max, max);
    }

    public static string ToHex(byte r, byte g, byte b) => $"#{r:X2}{g:X2}{b:X2}";

    public static string ToHex(Hsv c)
    {
        var (r, g, b) = ToRgb(c);
        return ToHex(r, g, b);
    }

    /// <summary>Accepts the same forms as the config file ("#RGB", "#RRGGBB", with or without '#').</summary>
    public static bool TryParseHex(string? text, out byte r, out byte g, out byte b)
    {
        r = g = b = 0;
        if (ConfigStore.NormalizeColor(text) is not { } hex)
            return false;
        r = Convert.ToByte(hex.Substring(1, 2), 16);
        g = Convert.ToByte(hex.Substring(3, 2), 16);
        b = Convert.ToByte(hex.Substring(5, 2), 16);
        return true;
    }

    private static byte To8Bit(double channel) => (byte)Math.Clamp(Math.Round(channel * 255), 0, 255);
}
