using Avalonia;
using Avalonia.Automation.Peers;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Media;

namespace OCnM.App.Controls;

/// <summary>
/// A horizontal slider whose track is painted with any brush (a hue rainbow, a white-to-color ramp, …).
/// Pointer drag, arrow keys (Shift = ×10), Page Up/Down, Home and End all work; the thumb gets a focus ring on keyboard use.
/// </summary>
public sealed class GradientSlider : RangeBase
{
    private const double TrackHeight = 12;
    private const double ThumbRadius = 10;

    public static readonly StyledProperty<IBrush?> TrackBrushProperty =
        AvaloniaProperty.Register<GradientSlider, IBrush?>(nameof(TrackBrush));

    private bool _dragging;
    private bool _focusVisible;

    static GradientSlider()
    {
        AffectsRender<GradientSlider>(ValueProperty, MinimumProperty, MaximumProperty, TrackBrushProperty);
        FocusableProperty.OverrideDefaultValue<GradientSlider>(true);
        MaximumProperty.OverrideDefaultValue<GradientSlider>(100);
        SmallChangeProperty.OverrideDefaultValue<GradientSlider>(1);
        LargeChangeProperty.OverrideDefaultValue<GradientSlider>(10);
    }

    public IBrush? TrackBrush
    {
        get => GetValue(TrackBrushProperty);
        set => SetValue(TrackBrushProperty, value);
    }

    protected override Size MeasureOverride(Size availableSize) =>
        new(double.IsInfinity(availableSize.Width) ? 200 : availableSize.Width, ThumbRadius * 2 + 6);

    protected override AutomationPeer OnCreateAutomationPeer() => new SliderPeer(this);

    /// <summary>Exposes the control to screen readers as a slider with a value range.</summary>
    private sealed class SliderPeer(GradientSlider owner) : RangeBaseAutomationPeer(owner)
    {
        protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.Slider;
    }

    private double ThumbX
    {
        get
        {
            var span = Maximum - Minimum;
            var t = span <= 0 ? 0 : (Value - Minimum) / span;
            return ThumbRadius + Math.Clamp(t, 0, 1) * Math.Max(0, Bounds.Width - 2 * ThumbRadius);
        }
    }

    public override void Render(DrawingContext context)
    {
        var cy = Bounds.Height / 2;
        var track = new Rect(ThumbRadius, cy - TrackHeight / 2, Math.Max(0, Bounds.Width - 2 * ThumbRadius), TrackHeight);
        var edge = new Pen(new SolidColorBrush(Color.FromArgb(70, 128, 128, 128)), 1);
        context.DrawRectangle(TrackBrush, edge, new RoundedRect(track, TrackHeight / 2));

        var center = new Point(ThumbX, cy);
        if (_focusVisible && IsFocused)
            context.DrawEllipse(null, new Pen(new SolidColorBrush(Color.FromRgb(0x5E, 0x81, 0xF4)), 2), center, ThumbRadius + 3, ThumbRadius + 3);
        // White disc with a dark hairline: readable on every color in both themes.
        context.DrawEllipse(new SolidColorBrush(Color.FromArgb(60, 0, 0, 0)), null, center + new Vector(0, 1), ThumbRadius, ThumbRadius);
        context.DrawEllipse(Brushes.White, new Pen(new SolidColorBrush(Color.FromArgb(150, 0, 0, 0)), 1.5), center, ThumbRadius - 1, ThumbRadius - 1);
    }

    // ---- Pointer ----

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            return;
        _dragging = true;
        _focusVisible = false;
        e.Pointer.Capture(this);
        Focus();
        SetFromX(e.GetPosition(this).X);
        e.Handled = true;
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (_dragging)
            SetFromX(e.GetPosition(this).X);
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (!_dragging)
            return;
        _dragging = false;
        e.Pointer.Capture(null);
    }

    private void SetFromX(double x)
    {
        var width = Math.Max(1, Bounds.Width - 2 * ThumbRadius);
        var t = Math.Clamp((x - ThumbRadius) / width, 0, 1);
        Value = Minimum + t * (Maximum - Minimum);
    }

    // ---- Keyboard ----

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        var step = e.KeyModifiers.HasFlag(KeyModifiers.Shift) ? LargeChange : SmallChange;
        double? next = e.Key switch
        {
            Key.Left or Key.Down => Value - step,
            Key.Right or Key.Up => Value + step,
            Key.PageDown => Value - LargeChange,
            Key.PageUp => Value + LargeChange,
            Key.Home => Minimum,
            Key.End => Maximum,
            _ => null,
        };
        if (next is null)
            return;
        _focusVisible = true;
        Value = Math.Clamp(next.Value, Minimum, Maximum);
        InvalidateVisual();
        e.Handled = true;
    }

    protected override void OnLostFocus(FocusChangedEventArgs e)
    {
        base.OnLostFocus(e);
        InvalidateVisual();
    }
}
