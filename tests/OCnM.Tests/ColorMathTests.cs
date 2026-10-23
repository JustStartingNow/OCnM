using OCnM.Core.Models;

namespace OCnM.Tests;

public class ColorMathTests
{
    [Theory]
    [InlineData(0, 1, 1, "#FF0000")]
    [InlineData(120, 1, 1, "#00FF00")]
    [InlineData(240, 1, 1, "#0000FF")]
    [InlineData(60, 1, 1, "#FFFF00")]
    [InlineData(180, 1, 1, "#00FFFF")]
    [InlineData(300, 1, 1, "#FF00FF")]
    [InlineData(0, 0, 1, "#FFFFFF")]
    [InlineData(0, 0, 0, "#000000")]
    [InlineData(0, 0, 0.5, "#808080")]
    [InlineData(200, 0.5, 0.5, "#406A80")]
    public void Hsv_to_hex(double h, double s, double v, string expected)
    {
        Assert.Equal(expected, ColorMath.ToHex(new Hsv(h, s, v)));
    }

    [Theory]
    [InlineData(360, "#FF0000")]
    [InlineData(-120, "#0000FF")]
    [InlineData(480, "#00FF00")]
    public void Hue_wraps_around(double h, string expected)
    {
        Assert.Equal(expected, ColorMath.ToHex(new Hsv(h, 1, 1)));
    }

    [Fact]
    public void Out_of_range_saturation_and_value_are_clamped()
    {
        Assert.Equal("#FFFFFF", ColorMath.ToHex(new Hsv(0, -3, 7)));
        Assert.Equal("#000000", ColorMath.ToHex(new Hsv(0, 4, -1)));
    }

    [Fact]
    public void Rgb_to_hsv_known_values()
    {
        var red = ColorMath.FromRgb(255, 0, 0);
        Assert.Equal((0, 1, 1), (red.H, red.S, red.V));
        var blue = ColorMath.FromRgb(0, 0, 255);
        Assert.Equal((240, 1, 1), (blue.H, blue.S, blue.V));
        var gray = ColorMath.FromRgb(128, 128, 128);
        Assert.Equal(0, gray.S);
        Assert.Equal(128 / 255.0, gray.V, 6);
        var black = ColorMath.FromRgb(0, 0, 0);
        Assert.Equal((0, 0, 0), (black.H, black.S, black.V));
    }

    [Fact]
    public void Every_color_on_a_coarse_grid_survives_rgb_hsv_rgb()
    {
        for (var r = 0; r <= 255; r += 15)
        {
            for (var g = 0; g <= 255; g += 15)
            {
                for (var b = 0; b <= 255; b += 15)
                {
                    var hsv = ColorMath.FromRgb((byte)r, (byte)g, (byte)b);
                    Assert.Equal((r, g, b), ColorMath.ToRgb(hsv));
                }
            }
        }
    }

    [Theory]
    [InlineData("#3366FF", 0x33, 0x66, 0xFF)]
    [InlineData("3366ff", 0x33, 0x66, 0xFF)]
    [InlineData("#abc", 0xAA, 0xBB, 0xCC)]
    public void Parses_hex(string text, int r, int g, int b)
    {
        Assert.True(ColorMath.TryParseHex(text, out var rr, out var gg, out var bb));
        Assert.Equal((r, g, b), (rr, gg, bb));
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("#12345")]
    [InlineData("#GG0000")]
    public void Rejects_bad_hex(string? text)
    {
        Assert.False(ColorMath.TryParseHex(text, out _, out _, out _));
    }
}
