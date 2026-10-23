using OCnM.App.ViewModels;

namespace OCnM.Tests;

public class ColorEditorTests
{
    [Fact]
    public void Starts_from_the_given_hex_and_moves_the_sliders()
    {
        var e = new ColorEditorViewModel("#FF0000");
        Assert.Equal("#FF0000", e.Hex);
        Assert.Equal(0, e.Hue);
        Assert.Equal(100, e.Saturation);
        Assert.Equal(100, e.Brightness);
        Assert.True(e.HasColor);
        Assert.False(e.IsDefault);
    }

    [Fact]
    public void Moving_a_slider_rewrites_the_hex()
    {
        var e = new ColorEditorViewModel("#FF0000");
        e.Hue = 120;
        Assert.Equal("#00FF00", e.Hex);
        e.Brightness = 50;
        Assert.Equal("#008000", e.Hex);
        e.Saturation = 0;
        Assert.Equal("#808080", e.Hex);
    }

    [Fact]
    public void Typing_a_hex_moves_the_sliders_and_a_short_form_works()
    {
        var e = new ColorEditorViewModel(null);
        e.Hex = "#0000ff";
        Assert.Equal(240, e.Hue);
        Assert.Equal(100, e.Saturation);
        e.Hex = "#fff";
        Assert.Equal(0, e.Saturation);
        Assert.Equal(100, e.Brightness);
    }

    [Fact]
    public void Gray_and_black_keep_the_last_hue()
    {
        var e = new ColorEditorViewModel("#0000FF");
        e.Hex = "#808080";
        Assert.Equal(240, e.Hue);
        e.Hex = "#000000";
        Assert.Equal(240, e.Hue);
    }

    [Fact]
    public void Invalid_text_flags_an_error_and_leaves_the_sliders_alone()
    {
        var e = new ColorEditorViewModel("#00FF00");
        e.Hex = "#12";
        Assert.True(e.IsInvalid);
        Assert.False(e.HasColor);
        Assert.Equal(120, e.Hue);
        e.Hex = "#123456";
        Assert.False(e.IsInvalid);
    }

    [Fact]
    public void Empty_means_default_and_picking_null_resets()
    {
        var e = new ColorEditorViewModel("#FF0000");
        e.Pick(null);
        Assert.True(e.IsDefault);
        Assert.False(e.IsInvalid);
        Assert.Equal("", e.Hex);
        Assert.Equal("#2F80ED", Pick(e, "#2F80ED"));
    }

    [Fact]
    public void A_slider_move_after_default_creates_a_color()
    {
        var e = new ColorEditorViewModel(null);
        Assert.True(e.IsDefault);
        e.Hue = 10;
        Assert.True(e.HasColor);
        Assert.Matches("^#[0-9A-F]{6}$", e.Hex);
    }

    [Fact]
    public void Palette_buttons_set_the_color()
    {
        var e = new ColorEditorViewModel(null);
        var red = e.Presets.First(p => p.Name == "Red");
        red.SelectCommand.Execute(null);
        Assert.Equal("#E0393E", e.Hex);
        e.Presets.First(p => p.Name == "Default").SelectCommand.Execute(null);
        Assert.True(e.IsDefault);
    }

    [Fact]
    public void Row_color_text_follows_the_editor_and_back()
    {
        // The owner is only used by the Browse and Move buttons, which this test doesn't press.
        var row = new SliceRowViewModel(null!, new OCnM.Core.Models.SliceConfig());
        row.ColorText = "#112233";
        Assert.Equal("#112233", row.ColorEditor.Hex);
        row.ColorEditor.Hue = 0;
        Assert.Equal(row.ColorEditor.Hex, row.ColorText);
        Assert.False(row.ColorInvalid);
        row.ColorText = "nope";
        Assert.True(row.ColorInvalid);
    }

    private static string Pick(ColorEditorViewModel e, string hex)
    {
        e.Pick(hex);
        return e.Hex;
    }
}
