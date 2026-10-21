using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.VisualTree;
using Slotty.App.ViewModels;

namespace Slotty.App.Views;

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
        RowsList.AddHandler(PointerCaptureLostEvent, (_, _) => _draggedRow = null, RoutingStrategies.Tunnel);
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
        _draggedRow = null;
        e.Pointer.Capture(null);
        e.Handled = true;
    }

    public async Task<string?> PickFolderAsync(string title, string? startFolder)
    {
        var options = new FolderPickerOpenOptions { Title = title, AllowMultiple = false };
        if (startFolder is not null)
            options.SuggestedStartLocation = await StorageProvider.TryGetFolderFromPathAsync(startFolder);
        var result = await StorageProvider.OpenFolderPickerAsync(options);
        return result.Count > 0 ? result[0].TryGetLocalPath() : null;
    }
}
