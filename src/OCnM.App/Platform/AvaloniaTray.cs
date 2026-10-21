using Avalonia;
using Avalonia.Controls;
using OCnM.Core.Platform;

namespace OCnM.App.Platform;

/// <summary>
/// Tray icon through Avalonia: a notification-area icon on Windows, StatusNotifierItem over D-Bus on Linux
/// (silently absent when no tray host runs, which is common on Niri).
/// </summary>
public sealed class AvaloniaTray : ITrayService
{
    private readonly TrayIcon _icon;
    private readonly NativeMenuItem _autostartItem;

    public AvaloniaTray(Application app, WindowIcon icon, bool autostartSupported)
    {
        var show = new NativeMenuItem("Show / hide donut");
        show.Click += (_, _) => ToggleRequested?.Invoke(this, EventArgs.Empty);
        var settings = new NativeMenuItem("Settings…");
        settings.Click += (_, _) => SettingsRequested?.Invoke(this, EventArgs.Empty);
        _autostartItem = new NativeMenuItem("Start at login")
        {
            ToggleType = MenuItemToggleType.CheckBox,
            IsVisible = autostartSupported,
        };
        _autostartItem.Click += (_, _) => AutostartToggled?.Invoke(this, !_autostartItem.IsChecked);
        var quit = new NativeMenuItem("Quit Slotty");
        quit.Click += (_, _) => QuitRequested?.Invoke(this, EventArgs.Empty);

        var menu = new NativeMenu();
        menu.Add(show);
        menu.Add(settings);
        menu.Add(_autostartItem);
        menu.Add(new NativeMenuItemSeparator());
        menu.Add(quit);

        _icon = new TrayIcon { Icon = icon, ToolTipText = "Slotty", Menu = menu, IsVisible = false };
        _icon.Clicked += (_, _) => ToggleRequested?.Invoke(this, EventArgs.Empty);
        TrayIcon.SetIcons(app, [_icon]);
    }

    public event EventHandler? ToggleRequested;

    public event EventHandler? SettingsRequested;

    public event EventHandler? QuitRequested;

    public event EventHandler<bool>? AutostartToggled;

    public void SetVisible(bool visible) => _icon.IsVisible = visible;

    public void SetAutostartChecked(bool isChecked) => _autostartItem.IsChecked = isChecked;

    public void Dispose()
    {
        _icon.IsVisible = false;
        _icon.Dispose();
    }
}
