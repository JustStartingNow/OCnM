using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Styling;
using Avalonia.Threading;
using OCnM.App.Platform;
using OCnM.App.Services;
using OCnM.App.ViewModels;
using OCnM.App.Views;
using OCnM.Core.Config;
using OCnM.Core.Geometry;
using OCnM.Core.Ipc;
using OCnM.Core.Models;
using OCnM.Core.Platform;

namespace OCnM.App;

/// <summary>Owns the windows, platform services and app lifetime; turns commands into window actions.</summary>
internal sealed class AppController : IDisposable
{
    private readonly App _app;
    private readonly IClassicDesktopStyleApplicationLifetime _desktop;
    private readonly StartupContext _startup;
    private readonly TransferService _transfers = new();
    private PlatformServices _platform = null!;
    private ConfigService _config = null!;
    private DonutViewModel _donutVm = null!;
    private DonutWindow _donut = null!;
    private WindowIcon _icon = null!;
    private ITrayService? _tray;
    private SettingsWindow? _settings;
    private DispatcherTimer? _hideTimer;
    private string? _registeredHotkey;
    private bool _quitting;
    private bool _systemReducedMotion;
    private bool _disposed;

    public AppController(App app, IClassicDesktopStyleApplicationLifetime desktop, StartupContext startup)
    {
        _app = app;
        _desktop = desktop;
        _startup = startup;
    }

    /// <summary>Which windowing backend actually runs (after any fallback).</summary>
    public string BackendName { get; private set; } = "unknown";

    public void Start()
    {
        _platform = PlatformServices.Create();
        _systemReducedMotion = Motion.SystemPrefersReducedMotion();
        _config = new ConfigService(_startup.Store, _startup.Load.Config);
        _icon = AppIcon.Create();

        _donutVm = new DonutViewModel(_config, _transfers, _platform.FolderOpener);
        _donut = new DonutWindow
        {
            DataContext = _donutVm,
            Icon = _icon,
            // Always-on-top is only meaningful where we also place the window ourselves.
            Topmost = _platform.CanPositionWindows,
        };
        _donutVm.FolderPicker = _donut;
        _donutVm.HideRequested += (_, delay) =>
        {
            if (delay <= TimeSpan.Zero)
                Hide();
            else
                ScheduleHide(delay);
        };
        _donutVm.SettingsRequested += (_, _) => OpenSettings();
        _donutVm.ShowRequested += (_, _) =>
        {
            if (!_donut.IsVisible)
                Show(atCursor: false);
        };
        _donut.DismissRequested += (_, reason) => OnDismiss(reason);
        _donut.Closing += (_, e) =>
        {
            // A compositor "close window" just hides the donut; Quit is explicit.
            if (!_quitting)
            {
                e.Cancel = true;
                Hide();
            }
        };
        _donut.Opened += (_, _) => DetectBackend(_donut);

        _config.Changed += (_, c) => ApplySettings(c.Settings);
        ApplySettings(_config.Current.Settings);
        SyncAutostart();

        _startup.Sink.Attach(command => Dispatcher.UIThread.InvokeAsync(() => Handle(command)).GetTask());

        switch (_startup.Options.Command)
        {
            case IpcCommand.Hide:
                break;
            case IpcCommand.Settings:
                OpenSettings();
                break;
            default:
                Show(atCursor: true);
                break;
        }

        ReportConfigProblems();
    }

    private void ReportConfigProblems()
    {
        var load = _startup.Load;
        if (load.Status == ConfigLoadStatus.RecoveredFromCorruption)
        {
            var where = load.BackupPath is null ? "" : $" The old file was saved as {load.BackupPath}.";
            _donutVm.Toast.Show(ToastKind.Error, "The settings file was damaged and has been reset.", where.Trim());
            Log.Warn($"Config was corrupt ({load.Error}); backup: {load.BackupPath}");
        }
        else if (load.Error is not null)
        {
            _donutVm.Toast.Show(ToastKind.Error, "The settings file could not be read; using defaults.", load.Error);
        }
    }

    /// <summary>Works from any open window; the first one to open decides.</summary>
    private void DetectBackend(Window window)
    {
        if (BackendName != "unknown")
            return;
        var descriptor = window.TryGetPlatformHandle()?.HandleDescriptor ?? "";
        var implName = window.PlatformImpl?.GetType().FullName ?? "";
        BackendName = OperatingSystem.IsLinux()
            ? implName.Contains("Wayland", StringComparison.OrdinalIgnoreCase) ? "Wayland (native)" : "X11" + (_platform.IsWayland ? " (XWayland)" : "")
            : OperatingSystem.IsWindows() ? "Win32" : OperatingSystem.IsMacOS() ? "macOS" : descriptor;
        Log.Info($"Windowing backend: {BackendName} ({implName}, {descriptor})");
    }

    // ---- Commands ----

    private void Handle(IpcCommand command)
    {
        Log.Info($"Command: {command}");
        switch (command)
        {
            case IpcCommand.Toggle:
                Toggle(atCursor: true);
                break;
            case IpcCommand.Show:
                Show(atCursor: true);
                break;
            case IpcCommand.Hide:
                Hide();
                break;
            case IpcCommand.Settings:
                OpenSettings();
                break;
            case IpcCommand.Quit:
                Quit();
                break;
        }
    }

    /// <param name="hideOnlyIfFocused">
    /// For keys and the CLI: a donut that is mapped but unfocused (behind other windows or on another
    /// workspace) is brought forward instead of hidden, so the key never seems dead. The tray passes
    /// false because clicking the tray itself takes focus away from the donut.
    /// </param>
    public void Toggle(bool atCursor, bool hideOnlyIfFocused = true)
    {
        if (_donut.IsVisible && (_donut.IsActive || !hideOnlyIfFocused))
            Hide();
        else
            Show(atCursor);
    }

    public void Show(bool atCursor)
    {
        CancelHide();
        _donutVm.RefreshSliceStates();
        if (_platform.CanPositionWindows)
            PlaceWindow(atCursor);
        _donut.MarkShown();
        _donut.Show();
        _donut.Activate();
        _donut.FocusDonut();
        _donut.PlayEnter();
    }

    public void Hide()
    {
        CancelHide();
        if (_donutVm.Overlay is RenameOverlayViewModel or ColorOverlayViewModel)
            _donutVm.CloseOverlay();
        _donut.HideAnimated();
    }

    private void OnDismiss(DismissReason reason)
    {
        Log.Info($"Dismiss requested: {reason}");
        switch (reason)
        {
            case DismissReason.Escape:
                Hide();
                break;
            case DismissReason.OutsideClick when !_donutVm.IsPinned:
                Hide();
                break;
            case DismissReason.FocusLost:
                Hide();
                break;
        }
    }

    /// <summary>Centers the donut on the cursor (Windows/X11), kept inside the monitor's work area.</summary>
    private void PlaceWindow(bool atCursor)
    {
        ScreenPoint? cursor = atCursor && _platform.Cursor.TryGetCursorPosition(out var p) ? p : null;
        var screens = _donut.Screens;
        var screen = cursor is { } c ? screens.ScreenFromPoint(new PixelPoint(c.X, c.Y)) : null;
        screen ??= screens.Primary ?? screens.All.FirstOrDefault();
        if (screen is null)
            return;

        var work = screen.WorkingArea;
        var area = new ScreenRect(work.X, work.Y, work.Width, work.Height);
        var center = cursor ?? new ScreenPoint(work.X + work.Width / 2, work.Y + work.Height / 2);
        var width = (int)Math.Ceiling(_donut.Width * screen.Scaling);
        var height = (int)Math.Ceiling(_donut.Height * screen.Scaling);
        // Center the ring (not the whole window, which has the toast strip below) on the cursor.
        var ringOffset = (int)((_donut.Height - _donut.Width) / 2 * screen.Scaling);
        var pos = ScreenMath.CenterClamped(center with { Y = center.Y + ringOffset }, width, height, area);
        _donut.Position = new PixelPoint(pos.X, pos.Y);
    }

    private void ScheduleHide(TimeSpan delay)
    {
        CancelHide();
        _hideTimer = new DispatcherTimer { Interval = delay };
        _hideTimer.Tick += (_, _) =>
        {
            // Keep the donut while the user is pointing at it (e.g. about to press Undo) or something is pending.
            if (_donut.IsPointerOver || _donutVm.Overlay is not null || _donutVm.IsBusy || _donutVm.IsInteracting || _donutVm.IsPinned)
            {
                _hideTimer!.Interval = TimeSpan.FromMilliseconds(1500);
                if (_donutVm.IsPinned)
                    CancelHide();
                return;
            }
            Hide();
        };
        _hideTimer.Start();
    }

    private void CancelHide()
    {
        _hideTimer?.Stop();
        _hideTimer = null;
    }

    public void OpenSettings()
    {
        if (_settings is null)
        {
            var vm = new SettingsViewModel(_config, _platform, () => BackendName);
            _settings = new SettingsWindow { DataContext = vm, Icon = _icon };
            _settings.Opened += (_, _) =>
            {
                DetectBackend(_settings);
                vm.RefreshEnvironmentInfo();
            };
            vm.FolderPicker = _settings;
            vm.CloseRequested += (_, _) => _settings?.Close();
            _settings.Closed += (_, _) => _settings = null;
        }
        if (!_platform.CanPositionWindows)
            Hide();
        _settings.Show();
        _settings.Activate();
    }

    public void Quit()
    {
        if (_donutVm.IsBusy)
        {
            Show(atCursor: false);
            _donutVm.Toast.Show(ToastKind.Error, "A transfer is still running.", "Cancel it or wait for it to finish, then quit again.");
            return;
        }
        _quitting = true;
        _settings?.Close();
        _donut.Close();
        _desktop.Shutdown();
    }

    // ---- Settings that affect platform services ----

    private void ApplySettings(AppSettings settings)
    {
        _app.RequestedThemeVariant = settings.Theme switch
        {
            ThemePreference.Light => ThemeVariant.Light,
            ThemePreference.Dark => ThemeVariant.Dark,
            _ => ThemeVariant.Default,
        };
        _donut.HideOnFocusLoss = settings.HideOnFocusLoss;
        Motion.Reduced = settings.ReduceMotion || _systemReducedMotion;
        _donut.SetReducedMotion(Motion.Reduced);
        UpdateTray(settings.ShowTrayIcon);
        UpdateHotkey(settings.Hotkey);
    }

    private void UpdateTray(bool visible)
    {
        if (visible && _tray is null)
        {
            try
            {
                var tray = new AvaloniaTray(_app, _icon, _platform.Autostart.IsSupported);
                tray.ToggleRequested += (_, _) => Dispatcher.UIThread.Post(() => Toggle(atCursor: true, hideOnlyIfFocused: false));
                tray.SettingsRequested += (_, _) => Dispatcher.UIThread.Post(OpenSettings);
                tray.QuitRequested += (_, _) => Dispatcher.UIThread.Post(Quit);
                tray.AutostartToggled += (_, on) => Dispatcher.UIThread.Post(() => SetLaunchAtLogin(on));
                _tray = tray;
            }
            catch (Exception ex)
            {
                // No tray host is fine: the CLI and hotkey still work.
                Log.Warn($"Tray icon unavailable: {ex.Message}");
            }
        }
        if (_tray is null)
            return;
        _tray.SetVisible(visible);
        _tray.SetAutostartChecked(_config.Current.Settings.LaunchAtLogin);
    }

    private void UpdateHotkey(string hotkey)
    {
        if (!_platform.Hotkey.IsSupported || hotkey == _registeredHotkey)
            return;
        _platform.Hotkey.Unregister();
        _platform.Hotkey.Pressed -= OnHotkeyPressed;
        _registeredHotkey = hotkey;
        if (string.IsNullOrWhiteSpace(hotkey))
            return;
        if (!HotkeyGesture.TryParse(hotkey, out var gesture, out var parseError))
        {
            _donutVm.Toast.Show(ToastKind.Error, "The global shortcut is invalid.", parseError);
            return;
        }
        var error = _platform.Hotkey.Register(gesture!);
        if (error is not null)
        {
            Log.Warn($"Hotkey {hotkey}: {error}");
            _donutVm.Toast.Show(ToastKind.Error, "The global shortcut could not be registered.", error);
            return;
        }
        _platform.Hotkey.Pressed += OnHotkeyPressed;
        Log.Info($"Registered hotkey {gesture}");
    }

    private void OnHotkeyPressed(object? sender, EventArgs e) => Dispatcher.UIThread.Post(() => Toggle(atCursor: true));

    private void SyncAutostart()
    {
        var autostart = _platform.Autostart;
        if (!autostart.IsSupported)
            return;
        // An entry from the Slotty days would start the old binary; the setting below recreates it under the new name.
        if (autostart.RemoveLegacyEntry(out var legacyError))
            Log.Info("Removed the old Slotty start-at-login entry.");
        else if (legacyError is not null)
            Log.Warn(legacyError);
        var wanted = _config.Current.Settings.LaunchAtLogin;
        if (autostart.IsEnabled() != wanted && autostart.SetEnabled(wanted) is { } error)
            Log.Warn($"Autostart sync failed: {error}");
    }

    public void SetLaunchAtLogin(bool enabled)
    {
        var error = _platform.Autostart.SetEnabled(enabled);
        if (error is not null)
        {
            _donutVm.Toast.Show(ToastKind.Error, "Could not change start at login.", error);
            _tray?.SetAutostartChecked(!enabled);
            return;
        }
        _config.Update(c => c with { Settings = c.Settings with { LaunchAtLogin = enabled } });
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        CancelHide();
        _transfers.DiscardUndo();
        _tray?.Dispose();
        _platform?.Dispose();
        Log.Info("Exited.");
    }
}
