using System.Collections.Specialized;
using System.ComponentModel;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Styling;
using Avalonia.Threading;
using OCnM.App.Services;
using OCnM.Core.Geometry;

namespace OCnM.App.Controls;

public sealed class DonutSliceEventArgs(RoutedEvent routedEvent, int index, bool fromKeyboard = false) : RoutedEventArgs(routedEvent)
{
    public int Index { get; } = index;

    /// <summary>True when raised by the keyboard (menus then open at the slice instead of the pointer).</summary>
    public bool FromKeyboard { get; } = fromKeyboard;
}

public sealed class DonutDropEventArgs(RoutedEvent routedEvent, int index, IReadOnlyList<string> paths, KeyModifiers modifiers)
    : RoutedEventArgs(routedEvent)
{
    public int Index { get; } = index;

    public IReadOnlyList<string> Paths { get; } = paths;

    public KeyModifiers Modifiers { get; } = modifiers;
}

/// <summary>
/// A ring of slices drawn with geometry (no child controls). Hit-testing uses <see cref="DonutGeometry"/>.
/// Raises routed events for clicks, context requests and file drops; it never decides what they mean.
/// Arrow keys move a focus ring between slices (Tab still leaves the control).
/// State changes (hover, drag, focus, open/close) animate with the shared <see cref="Motion"/> tokens.
/// </summary>
public sealed class DonutMenu : Control
{
    public const double FlashDurationMs = 1100;
    private const double GapDegrees = 1.4;
    private const double Padding = 10;
    private const double LiftPixels = 7;

    public static readonly StyledProperty<IReadOnlyList<IDonutSlice>?> SlicesProperty =
        AvaloniaProperty.Register<DonutMenu, IReadOnlyList<IDonutSlice>?>(nameof(Slices));

    public static readonly StyledProperty<string?> IdleTitleProperty =
        AvaloniaProperty.Register<DonutMenu, string?>(nameof(IdleTitle));

    public static readonly StyledProperty<string?> IdleSubtitleProperty =
        AvaloniaProperty.Register<DonutMenu, string?>(nameof(IdleSubtitle));

    public static readonly RoutedEvent<DonutSliceEventArgs> SliceClickedEvent =
        RoutedEvent.Register<DonutMenu, DonutSliceEventArgs>(nameof(SliceClicked), RoutingStrategies.Bubble);

    public static readonly RoutedEvent<DonutSliceEventArgs> SliceContextRequestedEvent =
        RoutedEvent.Register<DonutMenu, DonutSliceEventArgs>(nameof(SliceContextRequested), RoutingStrategies.Bubble);

    public static readonly RoutedEvent<DonutDropEventArgs> FilesDroppedEvent =
        RoutedEvent.Register<DonutMenu, DonutDropEventArgs>(nameof(FilesDropped), RoutingStrategies.Bubble);

    public static readonly RoutedEvent<RoutedEventArgs> OutsideClickedEvent =
        RoutedEvent.Register<DonutMenu, RoutedEventArgs>(nameof(OutsideClicked), RoutingStrategies.Bubble);

    private readonly DispatcherTimer _animationTimer;
    private readonly List<IDonutSlice> _observedSlices = [];
    private DonutPalette _palette = DonutPalette.Light;
    private INotifyCollectionChanged? _observedCollection;

    private int _hoverIndex = -1;
    private int _dragIndex = -1;
    private int _focusIndex = -1;
    private bool _focusVisible;
    private bool _dragActive;
    private DonutHit _pressedHit;
    private PointerUpdateKind _pressedButton;

    // Animated per-slice values, eased towards their targets on every frame.
    private double[] _emphasis = [];
    private double[] _lift = [];
    private long _lastFrame;

    // Open/close animation: 0 = hidden, 1 = fully shown.
    private double _appear = 1;
    private double _appearFrom = 1;
    private double _appearTo = 1;
    private long _appearStart;
    private Action? _appearDone;

    static DonutMenu()
    {
        AffectsRender<DonutMenu>(SlicesProperty, IdleTitleProperty, IdleSubtitleProperty);
        FocusableProperty.OverrideDefaultValue<DonutMenu>(true);
    }

    public DonutMenu()
    {
        _animationTimer = new DispatcherTimer(TimeSpan.FromMilliseconds(16), DispatcherPriority.Render, OnAnimationTick);
        DragDrop.SetAllowDrop(this, true);
        AddHandler(DragDrop.DragEnterEvent, OnDragOver);
        AddHandler(DragDrop.DragOverEvent, OnDragOver);
        AddHandler(DragDrop.DragLeaveEvent, OnDragLeave);
        AddHandler(DragDrop.DropEvent, OnDrop);
        ActualThemeVariantChanged += (_, _) =>
        {
            _palette = ActualThemeVariant == ThemeVariant.Dark ? DonutPalette.Dark : DonutPalette.Light;
            InvalidateVisual();
        };
    }

    public IReadOnlyList<IDonutSlice>? Slices
    {
        get => GetValue(SlicesProperty);
        set => SetValue(SlicesProperty, value);
    }

    public string? IdleTitle
    {
        get => GetValue(IdleTitleProperty);
        set => SetValue(IdleTitleProperty, value);
    }

    public string? IdleSubtitle
    {
        get => GetValue(IdleSubtitleProperty);
        set => SetValue(IdleSubtitleProperty, value);
    }

    public event EventHandler<DonutSliceEventArgs> SliceClicked
    {
        add => AddHandler(SliceClickedEvent, value);
        remove => RemoveHandler(SliceClickedEvent, value);
    }

    public event EventHandler<DonutSliceEventArgs> SliceContextRequested
    {
        add => AddHandler(SliceContextRequestedEvent, value);
        remove => RemoveHandler(SliceContextRequestedEvent, value);
    }

    public event EventHandler<DonutDropEventArgs> FilesDropped
    {
        add => AddHandler(FilesDroppedEvent, value);
        remove => RemoveHandler(FilesDroppedEvent, value);
    }

    public event EventHandler<RoutedEventArgs> OutsideClicked
    {
        add => AddHandler(OutsideClickedEvent, value);
        remove => RemoveHandler(OutsideClickedEvent, value);
    }

    /// <summary>Raised when the hub is pressed; the window uses it to start a move drag.</summary>
    public event EventHandler<PointerPressedEventArgs>? HubPressed;

    /// <summary>True while an external drag is over the control.</summary>
    public bool IsDragActive => _dragActive;

    public DonutGeometry Geometry
    {
        get
        {
            var outer = Math.Max(20, Math.Min(Bounds.Width, Bounds.Height) / 2 - Padding);
            var inner = outer * 0.47;
            return new DonutGeometry(outer, inner, inner - 10);
        }
    }

    private Point Center => new(Bounds.Width / 2, Bounds.Height / 2);

    public DonutHit HitTestPoint(Point p) => Geometry.HitTest(p.X - Center.X, p.Y - Center.Y);

    /// <summary>Center of a slice in control coordinates (for placing context menus).</summary>
    public Point SliceCenter(int index)
    {
        var g = Geometry;
        var (x, y) = DonutGeometry.PointAt(g.SliceMidAngle(index), (g.InnerRadius + g.OuterRadius) / 2);
        return new Point(Center.X + x, Center.Y + y);
    }

    // ---- Open / close animation ----

    /// <summary>Scales and fades the ring in (decelerating). Instant with reduced motion.</summary>
    public void PlayEnter()
    {
        _focusVisible = false;
        _focusIndex = -1;
        // Interrupting a close continues from where it is instead of jumping back to hidden.
        AnimateAppear(_appearDone is not null ? _appear : 0, 1, null);
    }

    /// <summary>Fades the ring out (accelerating, shorter than enter), then calls <paramref name="done"/>.</summary>
    public void PlayExit(Action done) => AnimateAppear(_appear, 0, done);

    private void AnimateAppear(double from, double to, Action? done)
    {
        // A newer animation cancels the callback of the one it interrupts (e.g. a pending hide).
        _appearDone = null;
        if (Motion.Reduced || !IsEffectivelyVisible)
        {
            _appear = _appearTo = to;
            InvalidateVisual();
            done?.Invoke();
            return;
        }
        _appearFrom = from;
        _appearTo = to;
        _appear = from;
        _appearStart = Environment.TickCount64;
        _appearDone = done;
        StartAnimation();
    }

    // ---- Slices and animation ----

    protected override Size MeasureOverride(Size availableSize)
    {
        var w = double.IsInfinity(availableSize.Width) ? 420 : availableSize.Width;
        var h = double.IsInfinity(availableSize.Height) ? 420 : availableSize.Height;
        var side = Math.Min(w, h);
        return new Size(side, side);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == SlicesProperty)
            ObserveSlices(change.GetNewValue<IReadOnlyList<IDonutSlice>?>());
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _palette = ActualThemeVariant == ThemeVariant.Dark ? DonutPalette.Dark : DonutPalette.Light;
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        _animationTimer.Stop();
    }

    private void ObserveSlices(IReadOnlyList<IDonutSlice>? slices)
    {
        foreach (var s in _observedSlices)
            s.PropertyChanged -= OnSliceChanged;
        _observedSlices.Clear();
        if (_observedCollection is not null)
            _observedCollection.CollectionChanged -= OnCollectionChanged;
        _observedCollection = slices as INotifyCollectionChanged;
        if (_observedCollection is not null)
            _observedCollection.CollectionChanged += OnCollectionChanged;
        if (slices is not null)
        {
            foreach (var s in slices)
            {
                s.PropertyChanged += OnSliceChanged;
                _observedSlices.Add(s);
            }
        }
        var count = slices?.Count ?? 0;
        if (_emphasis.Length != count)
        {
            _emphasis = new double[count];
            _lift = new double[count];
        }
        StartAnimation();
    }

    private void OnCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e) => ObserveSlices(Slices);

    private void OnSliceChanged(object? sender, PropertyChangedEventArgs e) => StartAnimation();

    private double EmphasisTarget(int i) =>
        i == _dragIndex ? 1.0
        : !_dragActive && (i == _hoverIndex || (_focusVisible && i == _focusIndex)) ? 0.5
        : 0.0;

    private double LiftTarget(int i) => i == _dragIndex ? 1.0 : 0.0;

    private bool NeedsFrames()
    {
        if (_appear != _appearTo)
            return true;
        for (var i = 0; i < _emphasis.Length; i++)
        {
            if (_emphasis[i] != EmphasisTarget(i) || _lift[i] != LiftTarget(i))
                return true;
        }
        var now = Environment.TickCount64;
        return _observedSlices.Any(s =>
            (s.IsBusy && s.Progress < 0) || (s.Flash != DonutFlash.None && now - s.FlashStartedAt < FlashDurationMs));
    }

    /// <summary>Redraws now and keeps the frame timer running while anything is still moving.</summary>
    private void StartAnimation()
    {
        InvalidateVisual();
        if (!_animationTimer.IsEnabled && NeedsFrames())
        {
            _lastFrame = Environment.TickCount64;
            _animationTimer.Start();
        }
    }

    private void OnAnimationTick(object? sender, EventArgs e)
    {
        var now = Environment.TickCount64;
        var dt = Math.Max(1, now - _lastFrame);
        _lastFrame = now;

        // Highlights move linearly towards their target over Motion.StateMs (instant with reduced motion).
        var step = Motion.Reduced ? 1.0 : dt / Motion.StateMs;
        for (var i = 0; i < _emphasis.Length; i++)
        {
            _emphasis[i] = Approach(_emphasis[i], EmphasisTarget(i), step);
            _lift[i] = Approach(_lift[i], LiftTarget(i), step);
        }

        if (_appear != _appearTo)
        {
            var entering = _appearTo > _appearFrom;
            var duration = entering ? Motion.EnterMs : Motion.ExitMs;
            var t = Math.Clamp((now - _appearStart) / duration, 0, 1);
            var eased = entering ? Motion.EaseOut(t) : Motion.EaseIn(t);
            _appear = t >= 1 ? _appearTo : _appearFrom + (_appearTo - _appearFrom) * eased;
            if (t >= 1)
            {
                var done = _appearDone;
                _appearDone = null;
                done?.Invoke();
            }
        }

        InvalidateVisual();
        if (!NeedsFrames())
            _animationTimer.Stop();
    }

    private static double Approach(double current, double target, double step) =>
        current < target ? Math.Min(target, current + step) : Math.Max(target, current - step);

    // ---- Pointer ----

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        var hit = HitTestPoint(e.GetPosition(this));
        _focusVisible = false;
        SetHover(hit.IsSlice ? hit.SliceIndex : -1);
        Cursor = hit.IsSlice ? new Cursor(StandardCursorType.Hand) : Cursor.Default;
    }

    protected override void OnPointerExited(PointerEventArgs e)
    {
        base.OnPointerExited(e);
        SetHover(-1);
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        var point = e.GetCurrentPoint(this);
        _pressedHit = HitTestPoint(point.Position);
        _pressedButton = point.Properties.PointerUpdateKind;
        _focusVisible = false;
        Focus();

        switch (_pressedHit.Kind)
        {
            case DonutHitKind.Hub when _pressedButton == PointerUpdateKind.LeftButtonPressed:
                HubPressed?.Invoke(this, e);
                e.Handled = true;
                break;
            case DonutHitKind.Outside or DonutHitKind.Hole when _pressedButton == PointerUpdateKind.LeftButtonPressed:
                RaiseEvent(new RoutedEventArgs(OutsideClickedEvent));
                e.Handled = true;
                break;
            case DonutHitKind.Slice:
                e.Handled = true;
                break;
        }
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        var hit = HitTestPoint(e.GetPosition(this));
        if (!hit.IsSlice || !_pressedHit.IsSlice || hit.SliceIndex != _pressedHit.SliceIndex)
            return;

        if (_pressedButton == PointerUpdateKind.LeftButtonPressed && e.InitialPressMouseButton == MouseButton.Left)
            RaiseEvent(new DonutSliceEventArgs(SliceClickedEvent, hit.SliceIndex));
        else if (_pressedButton == PointerUpdateKind.RightButtonPressed && e.InitialPressMouseButton == MouseButton.Right)
            RaiseEvent(new DonutSliceEventArgs(SliceContextRequestedEvent, hit.SliceIndex));
        e.Handled = true;
        _pressedHit = default;
    }

    private void SetHover(int index)
    {
        if (_hoverIndex == index)
            return;
        _hoverIndex = index;
        StartAnimation();
    }

    // ---- Keyboard: arrows move the focus ring; Enter/Space opens; Menu or Shift+F10 opens the context menu ----

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        var count = Slices?.Count ?? 0;
        if (e.Handled || count == 0)
            return;

        var start = _focusIndex >= 0 ? _focusIndex : _hoverIndex >= 0 ? _hoverIndex : -1;
        // Some backends report the Menu key only as a physical key.
        var key = e.PhysicalKey == PhysicalKey.ContextMenu ? Key.Apps : e.Key;
        switch (key)
        {
            case Key.Right or Key.Down:
                MoveFocus(start < 0 ? 0 : (start + 1) % count);
                e.Handled = true;
                break;
            case Key.Left or Key.Up:
                MoveFocus(start < 0 ? count - 1 : (start - 1 + count) % count);
                e.Handled = true;
                break;
            case Key.Home:
                MoveFocus(0);
                e.Handled = true;
                break;
            case Key.End:
                MoveFocus(count - 1);
                e.Handled = true;
                break;
            case Key.Enter or Key.Space when _focusVisible && _focusIndex >= 0:
                RaiseEvent(new DonutSliceEventArgs(SliceClickedEvent, _focusIndex, fromKeyboard: true));
                e.Handled = true;
                break;
            case Key.Apps:
            case Key.F10 when e.KeyModifiers == KeyModifiers.Shift:
                if (_focusIndex < 0)
                    MoveFocus(0);
                RaiseEvent(new DonutSliceEventArgs(SliceContextRequestedEvent, _focusIndex, fromKeyboard: true));
                e.Handled = true;
                break;
        }
    }

    private void MoveFocus(int index)
    {
        _focusIndex = index;
        _focusVisible = true;
        StartAnimation();
    }

    protected override void OnLostFocus(FocusChangedEventArgs e)
    {
        base.OnLostFocus(e);
        StartAnimation();
    }

    // ---- Drag and drop ----

    private void OnDragOver(object? sender, DragEventArgs e)
    {
        _dragActive = true;
        _focusVisible = false;
        var hit = HitTestPoint(e.GetPosition(this));
        var index = hit.IsSlice && e.DataTransfer.Contains(DataFormat.File) ? hit.SliceIndex : -1;
        // Always report Copy: the source app must never delete its files. Moves are done by Slotty itself.
        e.DragEffects = index >= 0 ? DragDropEffects.Copy : DragDropEffects.None;
        _dragIndex = index;
        StartAnimation();
        e.Handled = true;
    }

    private void OnDragLeave(object? sender, DragEventArgs e)
    {
        _dragActive = false;
        _dragIndex = -1;
        StartAnimation();
    }

    private void OnDrop(object? sender, DragEventArgs e)
    {
        var hit = HitTestPoint(e.GetPosition(this));
        _dragActive = false;
        _dragIndex = -1;
        StartAnimation();
        if (!hit.IsSlice)
        {
            e.DragEffects = DragDropEffects.None;
            return;
        }

        var paths = (e.DataTransfer.TryGetFiles() ?? [])
            .Select(item => item.TryGetLocalPath())
            .OfType<string>()
            .ToList();
        e.DragEffects = paths.Count > 0 ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
        RaiseEvent(new DonutDropEventArgs(FilesDroppedEvent, hit.SliceIndex, paths, e.KeyModifiers));
    }

    // ---- Rendering ----

    public override void Render(DrawingContext context)
    {
        if (_appear <= 0.001)
            return;

        var slices = Slices;
        var g = Geometry;
        var c = Center;
        var p = _palette;
        var now = Environment.TickCount64;

        // Open/close: a slight scale around the center plus a fade.
        var scale = 0.9 + 0.1 * _appear;
        var transform = Matrix.CreateTranslation(-c.X, -c.Y) * Matrix.CreateScale(scale, scale) * Matrix.CreateTranslation(c.X, c.Y);
        using var _ = context.PushTransform(transform);
        using var __ = context.PushOpacity(_appear);

        // Backdrop: one translucent disc behind everything, so the ring reads on any wallpaper.
        var backdropPen = new Pen(new SolidColorBrush(p.SurfaceStroke), 1);
        context.DrawEllipse(new SolidColorBrush(p.Surface), backdropPen, c, g.OuterRadius + 5, g.OuterRadius + 5);
        context.DrawEllipse(new SolidColorBrush(p.Hub), backdropPen, c, g.HubRadius, g.HubRadius);

        // Lifted (drag target) slices are drawn last so they sit on top of their neighbours.
        var order = Enumerable.Range(0, g.SliceCount).OrderBy(i => i < _lift.Length ? _lift[i] : 0);
        foreach (var i in order)
        {
            var slice = slices is not null && i < slices.Count ? slices[i] : null;
            DrawSlice(context, g, c, i, slice, now);
        }

        if (_focusVisible && IsFocused && _focusIndex >= 0)
            DrawFocusRing(context, g, c, _focusIndex);

        DrawHub(context, g, c, slices);
    }

    private void DrawSlice(DrawingContext context, DonutGeometry g, Point c, int i, IDonutSlice? slice, long now)
    {
        var p = _palette;
        var state = slice?.State ?? DonutSliceState.Empty;
        var level = i < _emphasis.Length ? _emphasis[i] : 0;
        var lift = i < _lift.Length ? _lift[i] : 0;
        var mid = g.SliceMidAngle(i);

        // The drop target moves outwards a few pixels: a clear "this one" without changing hit areas.
        var (ox, oy) = DonutGeometry.PointAt(mid, LiftPixels * Motion.EaseOut(lift));
        using var _ = context.PushTransform(Matrix.CreateTranslation(ox, oy));

        var start = g.SliceStartAngle(i) + GapDegrees / 2;
        var end = g.SliceStartAngle(i) + g.SliceSweep - GapDegrees / 2;
        var shape = SectorGeometry(c, g.InnerRadius, g.OuterRadius, start, end);

        var color = state switch
        {
            DonutSliceState.Broken => p.Warning,
            DonutSliceState.Assigned => slice?.Color ?? p.Accent,
            _ => p.EmptyStroke,
        };

        if (state == DonutSliceState.Empty)
        {
            var fill = Lerp(p.EmptyFill, DonutPalette.WithAlpha(p.Accent, 0.38), level);
            var strokeColor = Lerp(p.EmptyStroke, p.Accent, Math.Min(1, level * 2));
            context.DrawGeometry(new SolidColorBrush(fill),
                new Pen(new SolidColorBrush(strokeColor), 1.3 + level * 1.2, dashStyle: new DashStyle([4, 3], 0)), shape);
        }
        else
        {
            var fillAlpha = 0.2 + 0.44 * level;
            var stroke = new Pen(new SolidColorBrush(DonutPalette.WithAlpha(color, 0.8 + 0.2 * level)), 1.5 + 1.5 * level);
            context.DrawGeometry(new SolidColorBrush(DonutPalette.WithAlpha(color, fillAlpha)), stroke, shape);
        }

        // Success / error flash fades over the slice.
        var flash = slice?.Flash ?? DonutFlash.None;
        var flashT = flash == DonutFlash.None ? 1 : Math.Clamp((now - slice!.FlashStartedAt) / FlashDurationMs, 0, 1);
        var flashing = flash != DonutFlash.None && flashT < 1;
        if (flashing)
        {
            var flashColor = flash == DonutFlash.Success ? p.Success : p.Error;
            context.DrawGeometry(new SolidColorBrush(DonutPalette.WithAlpha(flashColor, 0.6 * (1 - Motion.EaseIn(flashT)))), null, shape);
        }

        var (cx, cy) = DonutGeometry.PointAt(mid, (g.InnerRadius + g.OuterRadius) / 2);
        var center = new Point(c.X + cx, c.Y + cy);
        var iconCenter = new Point(center.X, center.Y - 13);
        var iconSize = 22 + 2 * level;

        if (flashing)
        {
            var flashColor = flash == DonutFlash.Success ? p.Success : p.Error;
            DonutIcons.Draw(context, flash == DonutFlash.Success ? DonutIcons.Check : DonutIcons.Cross, iconCenter, 26,
                new SolidColorBrush(flashColor), 2.6);
        }
        else if (slice is { IsBusy: true })
        {
            DrawProgressRing(context, iconCenter, 12, slice.Progress, now, slice.Color ?? p.Accent);
        }
        else
        {
            var icon = state switch
            {
                DonutSliceState.Empty => DonutIcons.Plus,
                DonutSliceState.Broken => DonutIcons.Warning,
                _ => DonutIcons.For(slice!.Icon),
            };
            var iconColor = state switch
            {
                DonutSliceState.Empty => Lerp(p.MutedText, p.Accent, Math.Min(1, level * 2)),
                DonutSliceState.Broken => p.Warning,
                _ => color,
            };
            DonutIcons.Draw(context, icon, iconCenter, iconSize, new SolidColorBrush(iconColor));
        }

        // Label: up to two lines so long folder names stay readable.
        var textBrush = new SolidColorBrush(state == DonutSliceState.Empty ? p.MutedText : p.Text);
        var maxWidth = (g.OuterRadius - g.InnerRadius) * 0.9;
        var label = Text(slice?.Label ?? "Empty", 12.5, textBrush, maxWidth, FontWeight.SemiBold, maxLines: 2);
        DrawCentered(context, label, center.X, center.Y + 2);

        // Shortcut digit (keys 1-9) in the slice's leading outer corner, clear of two-line labels.
        var (nx, ny) = DonutGeometry.PointAt(g.SliceStartAngle(i) + 7, g.OuterRadius - 13);
        var digit = Text((i + 1).ToString(CultureInfo.InvariantCulture), 11, new SolidColorBrush(p.MutedText), 20, FontWeight.Medium);
        DrawCentered(context, digit, c.X + nx, c.Y + ny - digit.Height / 2);
    }

    /// <summary>Keyboard focus: a 2px accent outline just outside the focused slice.</summary>
    private void DrawFocusRing(DrawingContext context, DonutGeometry g, Point c, int i)
    {
        var lift = i < _lift.Length ? _lift[i] : 0;
        var (ox, oy) = DonutGeometry.PointAt(g.SliceMidAngle(i), LiftPixels * Motion.EaseOut(lift));
        using var _ = context.PushTransform(Matrix.CreateTranslation(ox, oy));
        var start = g.SliceStartAngle(i) + GapDegrees / 2 - 0.8;
        var end = g.SliceStartAngle(i) + g.SliceSweep - GapDegrees / 2 + 0.8;
        var ring = SectorGeometry(c, g.InnerRadius - 3.5, g.OuterRadius + 3.5, start, end);
        context.DrawGeometry(null, new Pen(new SolidColorBrush(_palette.Accent), 2.2), ring);
    }

    private void DrawProgressRing(DrawingContext context, Point center, double radius, double progress, long now, Color color)
    {
        var p = _palette;
        context.DrawEllipse(null, new Pen(new SolidColorBrush(DonutPalette.WithAlpha(p.MutedText, 0.35)), 3), center, radius, radius);
        double start, sweep;
        if (progress < 0)
        {
            start = now % 1000 / 1000.0 * 360;
            sweep = 90;
        }
        else
        {
            start = 0;
            sweep = Math.Clamp(progress, 0, 1) * 359.9;
        }
        if (sweep <= 0.5)
            return;
        var arc = new StreamGeometry();
        using (var ctx = arc.Open())
        {
            var (sx, sy) = DonutGeometry.PointAt(start, radius);
            var (ex, ey) = DonutGeometry.PointAt(start + sweep, radius);
            ctx.BeginFigure(new Point(center.X + sx, center.Y + sy), false);
            ctx.ArcTo(new Point(center.X + ex, center.Y + ey), new Size(radius, radius), 0, sweep > 180, SweepDirection.Clockwise);
            ctx.EndFigure(false);
        }
        context.DrawGeometry(null, new Pen(new SolidColorBrush(color), 3, lineCap: PenLineCap.Round), arc);
    }

    private void DrawHub(DrawingContext context, DonutGeometry g, Point c, IReadOnlyList<IDonutSlice>? slices)
    {
        var p = _palette;
        string? title;
        string? subtitle;
        Geometry? icon = null;
        var iconColor = p.Accent;
        var focusIndex = _dragIndex >= 0 ? _dragIndex
            : _hoverIndex >= 0 ? _hoverIndex
            : _focusVisible && IsFocused ? _focusIndex
            : -1;
        var slice = slices is not null && focusIndex >= 0 && focusIndex < slices.Count ? slices[focusIndex] : null;

        if (slice is not null && _dragIndex >= 0)
        {
            (title, subtitle, icon) = slice.State switch
            {
                DonutSliceState.Assigned => ($"{slice.ActionVerb} to {slice.Label}", slice.Path,
                    slice.ActionVerb == "Move" ? DonutIcons.MoveInto : DonutIcons.CopyInto),
                DonutSliceState.Broken => ("Folder missing", "Drop a folder to re-assign", DonutIcons.Warning),
                _ => ("Set destination", "Drop a folder to assign it", DonutIcons.Plus),
            };
            if (slice.State == DonutSliceState.Broken)
                iconColor = p.Warning;
        }
        else if (_dragActive)
        {
            (title, subtitle, icon) = ("Drop on a slice", null, DonutIcons.CopyInto);
            iconColor = p.MutedText;
        }
        else if (slice is not null)
        {
            (title, subtitle) = slice.State switch
            {
                DonutSliceState.Assigned => (slice.Label, slice.Path),
                DonutSliceState.Broken => ("Folder missing", "Right-click to choose another folder"),
                _ => ("Empty slice", "Click to choose a folder"),
            };
        }
        else
        {
            (title, subtitle) = (IdleTitle, IdleSubtitle);
        }

        if (subtitle is not null && (subtitle.Contains('/') || subtitle.Contains('\\')))
            subtitle = Core.FileOps.PathDisplay.Shorten(subtitle, 64);
        var width = g.HubRadius * 1.6;
        var titleText = Text(title ?? "", 15, new SolidColorBrush(p.Text), width, FontWeight.SemiBold, maxLines: 2);
        var subText = string.IsNullOrEmpty(subtitle)
            ? null
            : Text(subtitle, 11.5, new SolidColorBrush(p.MutedText), width, maxLines: 4, wrapAnywhere: subtitle.Contains('/') || subtitle.Contains('\\'));
        const double iconSize = 22;
        var iconBlock = icon is null ? 0 : iconSize + 6;
        var total = iconBlock + titleText.Height + (subText is null ? 0 : subText.Height + 4);
        var y = c.Y - total / 2;
        if (icon is not null)
            DonutIcons.Draw(context, icon, new Point(c.X, y + iconSize / 2), iconSize, new SolidColorBrush(iconColor), 2);
        y += iconBlock;
        DrawCentered(context, titleText, c.X, y);
        if (subText is not null)
            DrawCentered(context, subText, c.X, y + titleText.Height + 4);
    }

    /// <summary>Text built by <see cref="Text"/> is centered inside its MaxTextWidth box.</summary>
    private static void DrawCentered(DrawingContext context, FormattedText text, double centerX, double top) =>
        context.DrawText(text, new Point(centerX - text.MaxTextWidth / 2, top));

    private static FormattedText Text(string text, double size, IBrush brush, double maxWidth,
        FontWeight? weight = null, int maxLines = 1, bool wrapAnywhere = false)
    {
        // Paths break at separators: insert zero-width spaces so long paths wrap instead of being clipped.
        if (wrapAnywhere)
            text = text.Replace("/", "/​").Replace("\\", "\\​");
        return new FormattedText(text, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight,
            new Typeface(FontFamily.Default, FontStyle.Normal, weight ?? FontWeight.Normal), size, brush)
        {
            MaxTextWidth = maxWidth,
            MaxLineCount = maxLines,
            Trimming = TextTrimming.CharacterEllipsis,
            TextAlignment = TextAlignment.Center,
        };
    }

    private static Color Lerp(Color a, Color b, double t)
    {
        t = Math.Clamp(t, 0, 1);
        byte L(byte x, byte y) => (byte)Math.Round(x + (y - x) * t);
        return Color.FromArgb(L(a.A, b.A), L(a.R, b.R), L(a.G, b.G), L(a.B, b.B));
    }

    private static Geometry SectorGeometry(Point c, double inner, double outer, double startDeg, double endDeg)
    {
        var geometry = new StreamGeometry();
        using var ctx = geometry.Open();
        Point At(double deg, double r)
        {
            var (x, y) = DonutGeometry.PointAt(deg, r);
            return new Point(c.X + x, c.Y + y);
        }
        var large = endDeg - startDeg > 180;
        ctx.BeginFigure(At(startDeg, outer), true);
        ctx.ArcTo(At(endDeg, outer), new Size(outer, outer), 0, large, SweepDirection.Clockwise);
        ctx.LineTo(At(endDeg, inner));
        ctx.ArcTo(At(startDeg, inner), new Size(inner, inner), 0, large, SweepDirection.CounterClockwise);
        ctx.EndFigure(true);
        return geometry;
    }
}
