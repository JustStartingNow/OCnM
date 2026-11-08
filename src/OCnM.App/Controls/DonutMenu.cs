using System.Collections.Specialized;
using System.ComponentModel;
using System.Diagnostics;
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

    private bool _frameRequested;
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
    private double _lastFrame;

    // Open/close animation: 0 = hidden, 1 = fully shown.
    private double _appear = 1;
    private double _appearFrom = 1;
    private double _appearTo = 1;
    // Set on the first frame after the animation starts (NaN until then), so window creation can't eat into it.
    private double _appearStart = double.NaN;
    private int _appearStalls;
    private Action? _appearDone;
    // Which choreography _appear drives: the wipe (opening) or the arcs (closing). Both look identical at 0 and 1.
    private bool _appearWipe = true;

    static DonutMenu()
    {
        AffectsRender<DonutMenu>(SlicesProperty, IdleTitleProperty, IdleSubtitleProperty);
        FocusableProperty.OverrideDefaultValue<DonutMenu>(true);
    }

    public DonutMenu()
    {
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

    /// <summary>Opens with the clockwise wipe (<see cref="RenderWipe"/>). Instant with reduced motion.</summary>
    public void PlayEnter()
    {
        _focusVisible = false;
        _focusIndex = -1;
        // Interrupting a close continues from where it is instead of jumping back to hidden, and keeps the close's
        // style (played forwards), so nothing jumps.
        var interrupting = _appearDone is not null;
        if (!interrupting)
            _appearWipe = true;
        AnimateAppear(interrupting ? _appear : 0, 1, null);
    }

    /// <summary>
    /// Closes with the spinning arcs (<see cref="RenderArcs"/>), then calls <paramref name="done"/>. A close that
    /// interrupts an open keeps the open's style (played backwards).
    /// </summary>
    public void PlayExit(Action done)
    {
        if (_appear >= 1)
            _appearWipe = false;
        AnimateAppear(_appear, 0, done);
    }

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
        _appearStart = double.NaN;
        _appearStalls = 0;
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
        _frameRequested = false;
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

    private static double NowMs() => Stopwatch.GetTimestamp() * 1000.0 / Stopwatch.Frequency;

    /// <summary>Redraws now and keeps frames coming while anything is still moving.</summary>
    private void StartAnimation()
    {
        InvalidateVisual();
        if (!_frameRequested && NeedsFrames())
        {
            _lastFrame = NowMs();
            RequestFrame();
        }
    }

    /// <summary>
    /// Frames come from the render loop (in step with the display), not from a 16 ms timer: a timer runs at most
    /// at 60 Hz and drifts against the refresh, which looks stuttery next to a high-refresh compositor.
    /// </summary>
    private void RequestFrame()
    {
        if (TopLevel.GetTopLevel(this) is not { } top)
            return;
        _frameRequested = true;
        top.RequestAnimationFrame(_ =>
        {
            if (!_frameRequested)
                return;
            _frameRequested = false;
            OnAnimationFrame();
        });
    }

    private void OnAnimationFrame()
    {
        var now = NowMs();
        var dt = Math.Max(0.5, now - _lastFrame);
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
            if (double.IsNaN(_appearStart))
            {
                _appearStart = now;
            }
            else if (dt > MaxFrameGapMs && _appearStalls < MaxStalls)
            {
                // A stall (creating and mapping the window takes a few long frames) pauses the animation instead of
                // skipping a large part of it. Limited, so a machine that is always this slow still finishes.
                _appearStart += dt;
                _appearStalls++;
            }
            // _appear moves linearly in time; Render turns it into the eased, staggered choreography. Because the
            // mapping is the same both ways, a close interrupted by an open (or the reverse) continues seamlessly.
            var entering = _appearTo > _appearFrom;
            var duration = (entering ? Motion.EnterMs : Motion.ExitMs) * Math.Abs(_appearTo - _appearFrom);
            var t = duration <= 0 ? 1 : Math.Clamp((now - _appearStart) / duration, 0, 1);
            _appear = t >= 1 ? _appearTo : _appearFrom + (_appearTo - _appearFrom) * t;
            if (t >= 1)
            {
                var done = _appearDone;
                _appearDone = null;
                done?.Invoke();
            }
        }

        InvalidateVisual();
        if (NeedsFrames())
            RequestFrame();
    }

    private const double MaxFrameGapMs = 34;
    private const int MaxStalls = 4;

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
        // Always report Copy: the source app must never delete its files. Moves are done by OCnM itself.
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

        var g = Geometry;
        var c = Center;
        var now = Environment.TickCount64;
        if (_appearWipe)
            RenderWipe(context, g, c, now);
        else
            RenderArcs(context, g, c, now);
    }

    /// <summary>
    /// Close (after CSS spinner #1), described here as _appear goes from 0 to 1, i.e. as a close played backwards:
    /// two arcs spin in opposite directions on the outer and inner edge of the ring and grow into full circles, the
    /// donut fades in between them and the arcs fade away. Closing runs it from 1 to 0: the donut fades out inside
    /// two circles that then break open and spin away.
    /// </summary>
    private void RenderArcs(DrawingContext context, DonutGeometry g, Point c, long now)
    {
        var a = Motion.EaseOut(Math.Clamp(_appear / 0.65, 0, 1));
        var content = Math.Clamp((_appear - 0.35) / 0.65, 0, 1);

        using (PushAppear(context, c, 0.94 + 0.06 * Motion.EaseOut(content), 0, content))
        {
            DrawBackdrop(context, g, c);
            foreach (var i in SliceOrder(g))
                DrawSlice(context, g, c, i, SliceAt(i), now);
            DrawFocusIfShown(context, g, c);
            DrawHubLayer(context, g, c);
        }

        var arcOpacity = 1 - Math.Clamp((_appear - 0.7) / 0.3, 0, 1);
        if (arcOpacity <= 0)
            return;
        using (context.PushOpacity(arcOpacity))
        {
            var pen = new Pen(new SolidColorBrush(_palette.Accent), 3.5, lineCap: PenLineCap.Round);
            var sweep = 40 + 320 * a;
            DrawArc(context, pen, c, g.OuterRadius + 5, -90 + 300 * a, sweep);          // clockwise
            DrawArc(context, pen, c, g.InnerRadius - 5, 90 - 300 * a, sweep);           // counter-clockwise
        }
    }

    /// <summary>
    /// Open (after CSS spinner #5): the hub fades in, then the ring is revealed clockwise from the top like a clock
    /// hand, with an accent line on the moving edge.
    /// </summary>
    private void RenderWipe(DrawingContext context, DonutGeometry g, Point c, long now)
    {
        var hubT = Math.Clamp(_appear / 0.3, 0, 1);
        var w = Math.Clamp((_appear - 0.1) / 0.9, 0, 1);
        var sweep = 360 * Motion.EaseInOut(w);
        var radius = g.OuterRadius + 8;

        if (sweep > 0.5)
        {
            using (sweep < 359.5 ? context.PushGeometryClip(PieGeometry(c, radius, 0, sweep)) : (IDisposable)EmptyDisposable.Instance)
            {
                DrawBackdrop(context, g, c);
                foreach (var i in SliceOrder(g))
                    DrawSlice(context, g, c, i, SliceAt(i), now);
                DrawFocusIfShown(context, g, c);
            }
            if (sweep < 359.5)
            {
                // The "hand": a line along the moving edge, fading out as the circle closes.
                var (ix, iy) = DonutGeometry.PointAt(sweep, g.InnerRadius);
                var (ox, oy) = DonutGeometry.PointAt(sweep, g.OuterRadius + 5);
                var fade = Math.Min(1, (360 - sweep) / 40);
                var pen = new Pen(new SolidColorBrush(DonutPalette.WithAlpha(_palette.Accent, 0.9 * fade)), 3, lineCap: PenLineCap.Round);
                context.DrawLine(pen, new Point(c.X + ix, c.Y + iy), new Point(c.X + ox, c.Y + oy));
            }
        }

        using (PushAppear(context, c, 0.85 + 0.15 * Motion.EaseOut(hubT), 0, hubT))
            DrawHubLayer(context, g, c);
    }

    // ---- Shared drawing for the open styles ----

    private IDonutSlice? SliceAt(int i) => Slices is { } s && i < s.Count ? s[i] : null;

    /// <summary>Lifted (drag target) slices are drawn last so they sit on top of their neighbours.</summary>
    private IEnumerable<int> SliceOrder(DonutGeometry g) =>
        Enumerable.Range(0, g.SliceCount).OrderBy(i => i < _lift.Length ? _lift[i] : 0);

    private Pen BackdropPen => new(new SolidColorBrush(_palette.SurfaceStroke), 1);

    private void DrawBackdrop(DrawingContext context, DonutGeometry g, Point c) =>
        context.DrawEllipse(new SolidColorBrush(_palette.Surface), BackdropPen, c, g.OuterRadius + 5, g.OuterRadius + 5);

    private void DrawHubLayer(DrawingContext context, DonutGeometry g, Point c)
    {
        context.DrawEllipse(new SolidColorBrush(_palette.Hub), BackdropPen, c, g.HubRadius, g.HubRadius);
        DrawHub(context, g, c, Slices);
    }

    private void DrawFocusIfShown(DrawingContext context, DonutGeometry g, Point c)
    {
        if (_focusVisible && IsFocused && _focusIndex >= 0 && _appear >= 1)
            DrawFocusRing(context, g, c, _focusIndex);
    }

    /// <summary>An open arc; angles in degrees, 0 at 12 o'clock, positive sweep clockwise.</summary>
    private static void DrawArc(DrawingContext context, Pen pen, Point c, double radius, double start, double sweep)
    {
        sweep = Math.Clamp(sweep, 0.5, 359.5);
        var arc = new StreamGeometry();
        using (var ctx = arc.Open())
        {
            var (sx, sy) = DonutGeometry.PointAt(start, radius);
            var (ex, ey) = DonutGeometry.PointAt(start + sweep, radius);
            ctx.BeginFigure(new Point(c.X + sx, c.Y + sy), false);
            ctx.ArcTo(new Point(c.X + ex, c.Y + ey), new Size(radius, radius), 0, sweep > 180, SweepDirection.Clockwise);
            ctx.EndFigure(false);
        }
        context.DrawGeometry(null, pen, arc);
    }

    /// <summary>A filled pie wedge from the center; angles as in <see cref="DrawArc"/>.</summary>
    private static StreamGeometry PieGeometry(Point c, double radius, double start, double sweep)
    {
        var pie = new StreamGeometry();
        using var ctx = pie.Open();
        var (sx, sy) = DonutGeometry.PointAt(start, radius);
        var (ex, ey) = DonutGeometry.PointAt(start + sweep, radius);
        ctx.BeginFigure(c, true);
        ctx.LineTo(new Point(c.X + sx, c.Y + sy));
        ctx.ArcTo(new Point(c.X + ex, c.Y + ey), new Size(radius, radius), 0, sweep > 180, SweepDirection.Clockwise);
        ctx.EndFigure(true);
        return pie;
    }

    // ---- Open / close choreography helpers ----

    /// <summary>Scales and turns around the donut center and fades; nothing is pushed once fully shown.</summary>
    private static IDisposable PushAppear(DrawingContext context, Point c, double scale, double degrees, double opacity)
    {
        if (scale == 1 && degrees == 0 && opacity >= 1)
            return EmptyDisposable.Instance;
        var m = Matrix.CreateTranslation(-c.X, -c.Y) * Matrix.CreateScale(scale, scale)
            * Matrix.CreateRotation(degrees * Math.PI / 180) * Matrix.CreateTranslation(c.X, c.Y);
        var transform = context.PushTransform(m);
        var fade = context.PushOpacity(opacity);
        return new PushedPair(fade, transform);
    }

    private sealed class EmptyDisposable : IDisposable
    {
        public static readonly EmptyDisposable Instance = new();
        public void Dispose() { }
    }

    private sealed class PushedPair(IDisposable inner, IDisposable outer) : IDisposable
    {
        public void Dispose()
        {
            inner.Dispose();
            outer.Dispose();
        }
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
