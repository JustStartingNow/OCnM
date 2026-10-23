using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Media.Transformation;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Avalonia.VisualTree;
using OCnM.App.Services;
using OCnM.App.ViewModels;

namespace OCnM.App.Views;

public partial class SettingsWindow : Window, IFolderPicker
{
    private SliceRowViewModel? _draggedRow;

    public SettingsWindow()
    {
        InitializeComponent();
        // Reordering is done with pointer capture inside the list, so it works without OS drag and drop.
        RowsList.AddHandler(PointerPressedEvent, OnRowsPointerPressed, RoutingStrategies.Tunnel);
        RowsList.AddHandler(PointerMovedEvent, OnRowsPointerMoved, RoutingStrategies.Tunnel);
        RowsList.AddHandler(PointerReleasedEvent, OnRowsPointerReleased, RoutingStrategies.Tunnel);
        RowsList.AddHandler(PointerCaptureLostEvent, (_, _) => EndDrag(), RoutingStrategies.Tunnel);
        Classes.Set("reducedMotion", Motion.Reduced);
        DataContextChanged += (_, _) =>
        {
            if (ViewModel is { } vm)
                vm.ReorderRunner = AnimateReorder;
        };
    }

    private SettingsViewModel? ViewModel => DataContext as SettingsViewModel;

    private void OnRowsPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.Source is not Visual source || !e.GetCurrentPoint(RowsList).Properties.IsLeftButtonPressed)
            return;
        var grip = source.FindAncestorOfType<Border>(includeSelf: true);
        while (grip is not null && !grip.Classes.Contains("grip"))
            grip = grip.FindAncestorOfType<Border>();
        if (grip?.DataContext is not SliceRowViewModel row)
            return;
        _draggedRow = row;
        row.IsDragging = true;
        e.Pointer.Capture(RowsList);
        e.Handled = true;
    }

    private void OnRowsPointerMoved(object? sender, PointerEventArgs e)
    {
        if (_draggedRow is null || ViewModel is not { } vm)
            return;
        var y = e.GetPosition(RowsList).Y;
        for (var i = 0; i < vm.Rows.Count; i++)
        {
            if (RowsList.ContainerFromIndex(i) is not { } container)
                continue;
            var b = container.Bounds;
            if (y >= b.Top && y < b.Bottom)
            {
                vm.MoveRowTo(_draggedRow, i);
                break;
            }
        }
        e.Handled = true;
    }

    private void OnRowsPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (_draggedRow is null)
            return;
        EndDrag();
        e.Pointer.Capture(null);
        e.Handled = true;
    }

    private void EndDrag()
    {
        if (_draggedRow is not null)
            _draggedRow.IsDragging = false;
        _draggedRow = null;
    }

    // ---- Reorder animation ----

    /// <summary>
    /// Runs <paramref name="move"/> and slides every row that changed place from where it was to where it is now
    /// (the "FLIP" technique: measure, change, then animate from the old position). Instant with reduced motion.
    /// </summary>
    private void AnimateReorder(Action move)
    {
        if (ViewModel is not { } vm || Motion.Reduced)
        {
            move();
            return;
        }

        // Include any slide still in flight, so a quick second move continues from where the row visually is.
        var before = new Dictionary<SliceRowViewModel, double>();
        foreach (var row in vm.Rows)
        {
            if (RowsList.ContainerFromItem(row) is Control container)
                before[row] = container.Bounds.Top + CurrentOffset(container);
        }

        move();
        RowsList.UpdateLayout();

        var moved = new List<Control>();
        foreach (var row in vm.Rows)
        {
            if (RowsList.ContainerFromItem(row) is not Control container || !before.TryGetValue(row, out var oldTop))
                continue;
            var delta = oldTop - container.Bounds.Top;
            if (Math.Abs(delta) < 0.5)
                continue;
            container.Transitions = null;
            container.RenderTransform = Translate(delta);
            moved.Add(container);
        }
        if (moved.Count == 0)
            return;

        // One frame later the transition is switched on and the target is zero, which plays the slide.
        DispatcherTimer.RunOnce(() =>
        {
            foreach (var container in moved)
            {
                container.Transitions =
                [
                    new TransformOperationsTransition
                    {
                        Property = Visual.RenderTransformProperty,
                        Duration = TimeSpan.FromMilliseconds(Motion.CardMs),
                        Easing = new CubicEaseOut(),
                    },
                ];
                container.RenderTransform = Translate(0);
            }
        }, TimeSpan.FromMilliseconds(16));
    }

    private static double CurrentOffset(Control container) => container.RenderTransform?.Value.M32 ?? 0;

    private static TransformOperations Translate(double y) =>
        TransformOperations.Parse(FormattableString.Invariant($"translateY({y}px)"));

    public async Task<string?> PickFolderAsync(string title, string? startFolder)
    {
        var options = new FolderPickerOpenOptions { Title = title, AllowMultiple = false };
        if (startFolder is not null)
            options.SuggestedStartLocation = await StorageProvider.TryGetFolderFromPathAsync(startFolder);
        var result = await StorageProvider.OpenFolderPickerAsync(options);
        return result.Count > 0 ? result[0].TryGetLocalPath() : null;
    }
}
