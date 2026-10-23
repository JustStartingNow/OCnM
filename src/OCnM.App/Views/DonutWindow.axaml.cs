using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Avalonia.VisualTree;
using OCnM.App.Controls;
using OCnM.App.ViewModels;
using OCnM.Core.Models;

namespace OCnM.App.Views;

public partial class DonutWindow : Window, IFolderPicker
{
    private const long FocusGraceMs = 1000;
    private bool _menuOpen;
    private bool _activatedSinceShown;
    private long _shownAt;

    public DonutWindow()
    {
        InitializeComponent();
        Donut.SliceClicked += OnSliceClicked;
        Donut.SliceContextRequested += OnSliceContextRequested;
        Donut.FilesDropped += OnFilesDropped;
        Donut.OutsideClicked += (_, _) => RequestDismiss();
        Donut.HubPressed += OnHubPressed;
        Activated += (_, _) => _activatedSinceShown = true;
        Deactivated += OnDeactivated;
        Opened += (_, _) => ApplyChrome();
        Toast.PointerEntered += (_, _) => SetToastHovered(true);
        Toast.PointerExited += (_, _) => SetToastHovered(false);
        OverlayHost.PropertyChanged += (_, e) =>
        {
            if (e.Property == ContentControl.ContentProperty)
                Dispatcher.UIThread.Post(FocusOverlay, DispatcherPriority.Loaded);
        };
    }

    /// <summary>Moves keyboard focus into a newly shown overlay (the rename box), or back to the donut.</summary>
    private void FocusOverlay()
    {
        if (OverlayHost.Content is null)
        {
            Donut.Focus();
            return;
        }
        var textBox = OverlayHost.GetVisualDescendants().OfType<TextBox>().FirstOrDefault();
        if (textBox is not null)
        {
            textBox.Focus();
            textBox.SelectAll();
        }
        else
        {
            OverlayHost.GetVisualDescendants().OfType<Button>().FirstOrDefault(b => b.IsDefault)?.Focus();
        }
    }

    public DonutViewModel? ViewModel => DataContext as DonutViewModel;

    /// <summary>Raised for Esc, outside clicks and focus loss. The controller decides whether to hide.</summary>
    public event EventHandler<DismissReason>? DismissRequested;

    /// <summary>When true, a solid rounded background is drawn (no transparency or forced by OCNM_SOLID=1).</summary>
    public bool UsesSolidBackground { get; private set; }

    public bool HideOnFocusLoss { get; set; } = true;

    private void ApplyChrome()
    {
        var forced = Core.Config.EnvVars.Read(Core.Config.EnvVars.Solid) is "1" or "true";
        var noTransparency = ActualTransparencyLevel == WindowTransparencyLevel.None;
        UsesSolidBackground = forced || noTransparency;
        if (UsesSolidBackground)
        {
            Frame.Background = this.FindResource(ActualThemeVariant, "OCnMSurface") as IBrush;
            Frame.BorderBrush = this.FindResource(ActualThemeVariant, "OCnMStroke") as IBrush;
            Frame.BorderThickness = new Thickness(1);
            // Without transparency the window corners would show black; paint them in the same color.
            Background = noTransparency ? Frame.Background : Brushes.Transparent;
        }
        else
        {
            Frame.Background = Brushes.Transparent;
            Background = Brushes.Transparent;
        }
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == ActualThemeVariantProperty && IsLoaded)
            ApplyChrome();
    }

    public void FocusDonut() => Donut.Focus();

    private void SetToastHovered(bool hovered)
    {
        if (ViewModel is { } vm)
            vm.Toast.IsHovered = hovered;
    }

    /// <summary>Plays the open animation; call right after Show().</summary>
    public void PlayEnter() => Donut.PlayEnter();

    /// <summary>Plays the close animation, then hides the window.</summary>
    public void HideAnimated()
    {
        if (!IsVisible)
            return;
        Donut.PlayExit(() =>
        {
            Hide();
            if (ViewModel is { } vm)
                vm.Toast.IsHovered = false;
        });
    }

    public void SetReducedMotion(bool reduced) => Frame.Classes.Set("reducedMotion", reduced);

    /// <summary>
    /// Called before each Show. A window mapped during a drag (Wayland) may never receive focus;
    /// it must not count as "focus lost" until it has actually been active once.
    /// </summary>
    public void MarkShown()
    {
        _activatedSinceShown = false;
        _shownAt = Environment.TickCount64;
    }

    // ---- Input ----

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Handled || ViewModel is not { } vm)
            return;

        if (e.Key == Key.Escape)
        {
            if (vm.Overlay is not null)
                vm.CloseOverlay();
            else
                DismissRequested?.Invoke(this, DismissReason.Escape);
            e.Handled = true;
            return;
        }

        if (vm.Overlay is not null || e.KeyModifiers != KeyModifiers.None)
            return;
        var index = e.Key switch
        {
            >= Key.D1 and <= Key.D9 => e.Key - Key.D1,
            >= Key.NumPad1 and <= Key.NumPad9 => e.Key - Key.NumPad1,
            _ => -1,
        };
        if (index >= 0)
        {
            e.Handled = true;
            _ = vm.OpenByKeyAsync(index);
        }
    }

    private void OnHubPressed(object? sender, PointerPressedEventArgs e)
    {
        // Dragging the hub moves the window (where the platform allows it).
        BeginMoveDrag(e);
    }

    private void OnDeactivated(object? sender, EventArgs e)
    {
        // Shown in the middle of a drag (keybind while dragging), Niri focuses the window and immediately
        // returns focus to the drag source. Focus changes right after showing are therefore ignored.
        if (Environment.TickCount64 - _shownAt < FocusGraceMs)
            return;
        // Focus loss is unreliable (menus, pickers, Wayland focus quirks): re-check after a short delay.
        DispatcherTimer.RunOnce(() =>
        {
            if (!IsVisible || IsActive || !_activatedSinceShown || _menuOpen || Donut.IsDragActive || ViewModel is not { } vm)
                return;
            if (!HideOnFocusLoss || vm.IsPinned || vm.IsInteracting || vm.Overlay is not null || vm.IsBusy)
                return;
            DismissRequested?.Invoke(this, DismissReason.FocusLost);
        }, TimeSpan.FromMilliseconds(200));
    }

    private void RequestDismiss()
    {
        if (ViewModel is { Overlay: not null } vm)
        {
            if (vm.Overlay is RenameOverlayViewModel or ColorOverlayViewModel)
                vm.CloseOverlay();
            return;
        }
        DismissRequested?.Invoke(this, DismissReason.OutsideClick);
    }

    private async void OnSliceClicked(object? sender, DonutSliceEventArgs e)
    {
        if (ViewModel is { Overlay: null } vm)
            await vm.InvokeSliceAsync(e.Index);
    }

    private async void OnFilesDropped(object? sender, DonutDropEventArgs e)
    {
        if (ViewModel is { } vm)
            await vm.DropAsync(e.Index, e.Paths, e.Modifiers);
    }

    // ---- Context menu ----

    private void OnSliceContextRequested(object? sender, DonutSliceEventArgs e)
    {
        if (ViewModel is not { } vm)
            return;
        var i = e.Index;
        var slice = vm.Slices[i];
        slice.RefreshState();
        var assigned = slice.Config.IsAssigned;
        var exists = slice.State == DonutSliceState.Assigned;

        var colorItems = new List<object>
        {
            Item("Default", () => vm.SetColor(i, null), icon: Swatch(null), isChecked: slice.Config.Color is null),
            new Separator(),
        };
        colorItems.AddRange(DonutPalette.Swatches.Select(s =>
            (object)Item(s.Name, () => vm.SetColor(i, s.Hex), icon: Swatch(s.Hex), isChecked: string.Equals(slice.Config.Color, s.Hex, StringComparison.OrdinalIgnoreCase))));

        colorItems.Add(new Separator());
        colorItems.Add(Item("Custom color…", () => vm.BeginCustomColor(i)));

        var iconItems = Enum.GetValues<SliceIcon>().Select(icon =>
            (object)Item(icon.ToString(), () => vm.SetIcon(i, icon == SliceIcon.Folder ? null : icon),
                icon: Glyph(DonutIcons.For(icon)), isChecked: (slice.Config.Icon ?? SliceIcon.Folder) == icon)).ToList();

        var defaultAction = slice.EffectiveAction;
        var actionItems = new List<object>
        {
            Item("Use default", () => vm.SetAction(i, SliceAction.Inherit), isChecked: slice.Config.Action == SliceAction.Inherit),
            Item("Always copy", () => vm.SetAction(i, SliceAction.Copy), isChecked: slice.Config.Action == SliceAction.Copy),
            Item("Always move", () => vm.SetAction(i, SliceAction.Move), isChecked: slice.Config.Action == SliceAction.Move),
        };

        var menu = new ContextMenu
        {
            ItemsSource = new List<object>
            {
                Item(assigned ? "Change folder…" : "Set folder…", () => _ = vm.SetFolderAsync(i), icon: Glyph(DonutIcons.For(SliceIcon.Folder))),
                Item("Rename…", () => vm.BeginRename(i), enabled: assigned),
                Submenu("Change color", colorItems, assigned),
                Submenu("Change icon", iconItems, assigned),
                Submenu($"Drop action ({(defaultAction == DropAction.Move ? "move" : "copy")})", actionItems, assigned),
                new Separator(),
                Item("Open folder", () => _ = vm.OpenFolderAsync(i), enabled: exists),
                Item("Clear slice", () => vm.ClearSlice(i), enabled: assigned),
            },
        };
        if (e.FromKeyboard)
        {
            // Opened from the keyboard: anchor the menu at the slice, not at the (possibly far away) pointer.
            var center = Donut.SliceCenter(i);
            menu.Placement = PlacementMode.Bottom;
            menu.PlacementRect = new Rect(center.X - 1, center.Y - 1, 2, 2);
        }
        else
        {
            menu.Placement = PlacementMode.Pointer;
        }
        menu.Opened += (_, _) => _menuOpen = true;
        menu.Closed += (_, _) =>
        {
            _menuOpen = false;
            Donut.Focus();
        };
        menu.Open(Donut);
    }

    private static MenuItem Item(string header, Action action, bool enabled = true, Control? icon = null, bool? isChecked = null)
    {
        var item = new MenuItem { Header = header, IsEnabled = enabled, Icon = icon };
        if (isChecked is { } c)
        {
            item.ToggleType = MenuItemToggleType.Radio;
            item.IsChecked = c;
        }
        item.Click += (_, _) => action();
        return item;
    }

    private static MenuItem Submenu(string header, List<object> items, bool enabled) =>
        new() { Header = header, ItemsSource = items, IsEnabled = enabled };

    private static Control Swatch(string? hex) => new Ellipse
    {
        Width = 14,
        Height = 14,
        Fill = hex is null ? Brushes.Transparent : new SolidColorBrush(Color.Parse(hex)),
        Stroke = Brushes.Gray,
        StrokeThickness = 1,
    };

    private static Control Glyph(Geometry icon) => new IconGlyph { Icon = icon, Width = 16, Height = 16 };

    // ---- IFolderPicker ----

    public async Task<string?> PickFolderAsync(string title, string? startFolder)
    {
        var options = new FolderPickerOpenOptions { Title = title, AllowMultiple = false };
        if (startFolder is not null)
            options.SuggestedStartLocation = await StorageProvider.TryGetFolderFromPathAsync(startFolder);
        var result = await StorageProvider.OpenFolderPickerAsync(options);
        return result.Count > 0 ? result[0].TryGetLocalPath() : null;
    }
}

public enum DismissReason
{
    Escape,
    OutsideClick,
    FocusLost,
}
