using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace OCnM.App.Controls;

/// <summary>Draws one of the stroked <see cref="DonutIcons"/> (PathIcon would fill them).</summary>
public sealed class IconGlyph : Control
{
    public static readonly StyledProperty<Geometry?> IconProperty =
        AvaloniaProperty.Register<IconGlyph, Geometry?>(nameof(Icon));

    /// <summary>Shares TextElement.Foreground so icons follow their button's state (hover, checked, disabled).</summary>
    public static readonly StyledProperty<IBrush?> ForegroundProperty =
        Avalonia.Controls.Documents.TextElement.ForegroundProperty.AddOwner<IconGlyph>();

    public static readonly StyledProperty<double> StrokeWidthProperty =
        AvaloniaProperty.Register<IconGlyph, double>(nameof(StrokeWidth), 1.8);

    static IconGlyph()
    {
        AffectsRender<IconGlyph>(IconProperty, ForegroundProperty, StrokeWidthProperty);
    }

    public Geometry? Icon
    {
        get => GetValue(IconProperty);
        set => SetValue(IconProperty, value);
    }

    public IBrush? Foreground
    {
        get => GetValue(ForegroundProperty);
        set => SetValue(ForegroundProperty, value);
    }

    public double StrokeWidth
    {
        get => GetValue(StrokeWidthProperty);
        set => SetValue(StrokeWidthProperty, value);
    }

    protected override Size MeasureOverride(Size availableSize) => new(
        double.IsNaN(Width) ? 16 : Width,
        double.IsNaN(Height) ? 16 : Height);

    public override void Render(DrawingContext context)
    {
        if (Icon is null)
            return;
        var size = Math.Min(Bounds.Width, Bounds.Height);
        DonutIcons.Draw(context, Icon, new Point(Bounds.Width / 2, Bounds.Height / 2), size, Foreground ?? Brushes.Gray, StrokeWidth);
    }
}
