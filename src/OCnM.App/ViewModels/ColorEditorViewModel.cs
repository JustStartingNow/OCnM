using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OCnM.App.Controls;
using OCnM.Core.Models;

namespace OCnM.App.ViewModels;

/// <summary>A palette button inside the color editor.</summary>
public sealed partial class ColorPreset(ColorEditorViewModel owner, string? hex, string name)
{
    public string Name { get; } = name;

    public IBrush Brush { get; } = hex is null ? Brushes.Transparent : new SolidColorBrush(Color.Parse(hex));

    [RelayCommand]
    private void Select() => owner.Pick(hex);
}

/// <summary>
/// Edits one color through a hex box, hue / saturation / brightness sliders and a palette. Hex is "#RRGGBB",
/// or empty for "use the default color". Typing a valid hex moves the sliders; moving a slider rewrites the hex.
/// </summary>
public sealed partial class ColorEditorViewModel : ObservableObject
{
    // Starting point for the sliders while no color is set: close to the theme accent.
    private static readonly Hsv StartColor = ColorMath.FromRgb(0x52, 0x54, 0xD0);

    private bool _updating;

    [ObservableProperty]
    private string _hex = "";

    /// <summary>0..360.</summary>
    [ObservableProperty]
    private double _hue = StartColor.H;

    /// <summary>0..100.</summary>
    [ObservableProperty]
    private double _saturation = StartColor.S * 100;

    /// <summary>0..100.</summary>
    [ObservableProperty]
    private double _brightness = StartColor.V * 100;

    public ColorEditorViewModel(string? hex)
    {
        Presets = [new ColorPreset(this, null, "Default"), .. DonutPalette.Swatches.Select(s => new ColorPreset(this, s.Hex, s.Name))];
        Hex = hex ?? "";
    }

    public IReadOnlyList<ColorPreset> Presets { get; }

    /// <summary>The rainbow behind the hue slider.</summary>
    public IBrush HueTrack { get; } = Gradient(
        Enumerable.Range(0, 7).Select(i => ColorFor(new Hsv(i * 60, 1, 1))).ToArray());

    public IBrush SaturationTrack => Gradient(ColorFor(new Hsv(Hue, 0, Brightness / 100)), ColorFor(new Hsv(Hue, 1, Brightness / 100)));

    public IBrush BrightnessTrack => Gradient(ColorFor(new Hsv(Hue, Saturation / 100, 0)), ColorFor(new Hsv(Hue, Saturation / 100, 1)));

    /// <summary>True for a parsable, non-empty hex.</summary>
    public bool HasColor => ColorMath.TryParseHex(Hex, out _, out _, out _);

    /// <summary>Non-empty text that isn't a color.</summary>
    public bool IsInvalid => !string.IsNullOrWhiteSpace(Hex) && !HasColor;

    public bool IsDefault => string.IsNullOrWhiteSpace(Hex);

    public IBrush PreviewBrush => ColorMath.TryParseHex(Hex, out var r, out var g, out var b)
        ? new SolidColorBrush(Color.FromRgb(r, g, b))
        : Brushes.Transparent;

    /// <summary>Sets the color (null or empty = default) and moves the sliders to match.</summary>
    public void Pick(string? hex) => Hex = hex ?? "";

    partial void OnHexChanged(string value)
    {
        if (!_updating && ColorMath.TryParseHex(value, out var r, out var g, out var b))
        {
            var hsv = ColorMath.FromRgb(r, g, b);
            _updating = true;
            // Hue is meaningless for grays and black; keep the last one so the slider doesn't jump to 0.
            if (hsv.S > 0 && hsv.V > 0)
                Hue = hsv.H;
            Saturation = hsv.S * 100;
            Brightness = hsv.V * 100;
            _updating = false;
        }
        RaiseDerived();
    }

    partial void OnHueChanged(double value) => OnSliderChanged();

    partial void OnSaturationChanged(double value) => OnSliderChanged();

    partial void OnBrightnessChanged(double value) => OnSliderChanged();

    private void OnSliderChanged()
    {
        if (!_updating)
        {
            _updating = true;
            Hex = ColorMath.ToHex(new Hsv(Hue, Saturation / 100, Brightness / 100));
            _updating = false;
        }
        RaiseDerived();
    }

    private void RaiseDerived()
    {
        OnPropertyChanged(nameof(HasColor));
        OnPropertyChanged(nameof(IsInvalid));
        OnPropertyChanged(nameof(IsDefault));
        OnPropertyChanged(nameof(PreviewBrush));
        OnPropertyChanged(nameof(SaturationTrack));
        OnPropertyChanged(nameof(BrightnessTrack));
    }

    private static Color ColorFor(Hsv hsv)
    {
        var (r, g, b) = ColorMath.ToRgb(hsv);
        return Color.FromRgb(r, g, b);
    }

    private static LinearGradientBrush Gradient(params Color[] colors)
    {
        var brush = new LinearGradientBrush
        {
            StartPoint = new Avalonia.RelativePoint(0, 0.5, Avalonia.RelativeUnit.Relative),
            EndPoint = new Avalonia.RelativePoint(1, 0.5, Avalonia.RelativeUnit.Relative),
        };
        for (var i = 0; i < colors.Length; i++)
            brush.GradientStops.Add(new GradientStop(colors[i], colors.Length == 1 ? 0 : i / (double)(colors.Length - 1)));
        return brush;
    }
}
