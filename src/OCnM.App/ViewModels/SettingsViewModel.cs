using System.Collections.ObjectModel;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OCnM.App.Controls;
using OCnM.App.Platform;
using OCnM.App.Services;
using OCnM.Core;
using OCnM.Core.Config;
using OCnM.Core.Models;
using OCnM.Core.Platform;

namespace OCnM.App.ViewModels;

public sealed record IconOption(SliceIcon Value, string Name, Geometry Glyph);

/// <summary>Edits a draft of the config; nothing is applied until Save.</summary>
public sealed partial class SettingsViewModel : ObservableObject
{
    private readonly ConfigService _config;
    private readonly PlatformServices _platform;
    private readonly Func<string> _backendName;

    [ObservableProperty]
    private int _defaultActionIndex;

    [ObservableProperty]
    private int _conflictPolicyIndex;

    [ObservableProperty]
    private int _themeIndex;

    [ObservableProperty]
    private int _linuxBackendIndex;

    [ObservableProperty]
    private string _hotkey = "";

    [ObservableProperty]
    private string? _hotkeyError;

    [ObservableProperty]
    private bool _launchAtLogin;

    [ObservableProperty]
    private bool _hideOnFocusLoss;

    [ObservableProperty]
    private double _hideAfterDropSeconds;

    [ObservableProperty]
    private bool _showTrayIcon;

    [ObservableProperty]
    private bool _reduceMotion;

    [ObservableProperty]
    private bool _openFoldersInTabs;

    [ObservableProperty]
    private string? _statusMessage;

    [ObservableProperty]
    private bool _statusIsError;

    public SettingsViewModel(ConfigService config, PlatformServices platform, Func<string> backendName)
    {
        _config = config;
        _platform = platform;
        _backendName = backendName;
        Load(config.Current);
    }

    public ObservableCollection<SliceRowViewModel> Rows { get; } = [];

    public static IReadOnlyList<IconOption> IconOptions { get; } =
        Enum.GetValues<SliceIcon>().Select(i => new IconOption(i, i.ToString(), DonutIcons.For(i))).ToList();

    public IFolderPicker? FolderPicker { get; set; }

    public event EventHandler? CloseRequested;

    public bool HotkeySupported => _platform.Hotkey.IsSupported;

    public string HotkeyNote => _platform.Hotkey.IsSupported
        ? "Shows the donut at the cursor from anywhere. Example: Ctrl+Alt+Space. Leave empty to disable."
        : _platform.IsWayland
            ? "Wayland compositors own global shortcuts. Bind \"ocnm --toggle\" to a key in your compositor (see README)."
            : "Global shortcuts are not available on this platform.";

    public bool AutostartSupported => _platform.Autostart.IsSupported;

    public bool IsLinux => OperatingSystem.IsLinux();

    public string EnvironmentInfo =>
        $"{AppInfo.FullName} {Program.Version}   ·   Session: {_platform.Session}   ·   Backend: {_backendName()}";

    public string ConfigPath => AppPaths.ConfigFile;

    public string AppTitle => $"{AppInfo.FullName} · version {Program.Version}";

    public void RefreshEnvironmentInfo() => OnPropertyChanged(nameof(EnvironmentInfo));

    private void Load(OCnMConfig config)
    {
        var s = config.Settings;
        DefaultActionIndex = (int)s.DefaultAction;
        ConflictPolicyIndex = (int)s.ConflictPolicy;
        ThemeIndex = (int)s.Theme;
        LinuxBackendIndex = (int)s.LinuxBackend;
        Hotkey = s.Hotkey;
        LaunchAtLogin = s.LaunchAtLogin;
        HideOnFocusLoss = s.HideOnFocusLoss;
        HideAfterDropSeconds = s.HideAfterDropMs / 1000.0;
        ShowTrayIcon = s.ShowTrayIcon;
        ReduceMotion = s.ReduceMotion;
        OpenFoldersInTabs = s.OpenFoldersInTabs;

        Rows.Clear();
        foreach (var slice in config.Slices)
            Rows.Add(new SliceRowViewModel(this, slice));
        Renumber();
    }

    private void Renumber()
    {
        for (var i = 0; i < Rows.Count; i++)
            Rows[i].Position = i;
    }

    /// <summary>
    /// How a reorder is carried out. The window replaces this to slide the rows into place; the default just does it.
    /// </summary>
    public Action<Action> ReorderRunner { get; set; } = move => move();

    internal void MoveRow(SliceRowViewModel row, int delta)
    {
        var from = Rows.IndexOf(row);
        var to = from + delta;
        if (from < 0 || to < 0 || to >= Rows.Count)
            return;
        ReorderRunner(() =>
        {
            Rows.Move(from, to);
            Renumber();
        });
    }

    /// <summary>Drag-and-drop reorder: moves <paramref name="row"/> to <paramref name="targetIndex"/>.</summary>
    public void MoveRowTo(SliceRowViewModel row, int targetIndex)
    {
        var from = Rows.IndexOf(row);
        if (from < 0 || targetIndex < 0 || targetIndex >= Rows.Count || from == targetIndex)
            return;
        ReorderRunner(() =>
        {
            Rows.Move(from, targetIndex);
            Renumber();
        });
    }

    internal async Task BrowseAsync(SliceRowViewModel row)
    {
        if (FolderPicker is null)
            return;
        var start = !string.IsNullOrWhiteSpace(row.Path) && Directory.Exists(row.Path) ? row.Path : null;
        var picked = await FolderPicker.PickFolderAsync($"Choose a folder for slice {row.Number}", start);
        if (picked is not null)
            row.Path = picked;
    }

    partial void OnHotkeyChanged(string value) => HotkeyError = ValidateHotkey(value);

    private string? ValidateHotkey(string value)
    {
        if (!HotkeySupported || string.IsNullOrWhiteSpace(value))
            return null;
        return HotkeyGesture.TryParse(value, out _, out var error) ? null : error;
    }

    [RelayCommand]
    private void Save()
    {
        HotkeyError = ValidateHotkey(Hotkey);
        if (HotkeyError is not null)
        {
            SetStatus("Fix the global shortcut first.", isError: true);
            return;
        }

        var badColor = Rows.FirstOrDefault(r => r.ColorInvalid);
        if (badColor is not null)
        {
            SetStatus($"Slice {badColor.Number}: \"{badColor.ColorText}\" is not a color like #3366FF (marked in the list).", isError: true);
            return;
        }

        var hotkey = string.IsNullOrWhiteSpace(Hotkey) ? "" :
            HotkeyGesture.TryParse(Hotkey, out var g, out _) ? g!.ToString() : Hotkey.Trim();

        var old = _config.Current;
        var settings = old.Settings with
        {
            DefaultAction = (DropAction)DefaultActionIndex,
            ConflictPolicy = (ConflictPolicy)ConflictPolicyIndex,
            Theme = (ThemePreference)ThemeIndex,
            LinuxBackend = (LinuxBackend)LinuxBackendIndex,
            Hotkey = hotkey,
            HideOnFocusLoss = HideOnFocusLoss,
            HideAfterDropMs = (int)Math.Clamp(Math.Round(HideAfterDropSeconds * 1000), 0, 60_000),
            ShowTrayIcon = ShowTrayIcon,
            ReduceMotion = ReduceMotion,
            OpenFoldersInTabs = OpenFoldersInTabs,
            LaunchAtLogin = LaunchAtLogin,
        };

        string? autostartError = null;
        if (AutostartSupported && LaunchAtLogin != _platform.Autostart.IsEnabled())
        {
            autostartError = _platform.Autostart.SetEnabled(LaunchAtLogin);
            if (autostartError is not null)
                settings = settings with { LaunchAtLogin = !LaunchAtLogin };
        }

        var slices = Rows.Select((r, i) => r.ToConfig(i)).ToList();
        var saveError = _config.Update(c => c with { Settings = settings, Slices = [.. slices] });

        if (saveError is not null || autostartError is not null)
        {
            SetStatus(saveError ?? autostartError!, isError: true);
            return;
        }
        var restartNote = settings.LinuxBackend != old.Settings.LinuxBackend ? " The backend change applies after a restart." : "";
        // The window stays open so several changes can be made and saved in a row.
        SetStatus("Saved." + restartNote, isError: false);
    }

    [RelayCommand]
    private void Cancel() => CloseRequested?.Invoke(this, EventArgs.Empty);

    [RelayCommand]
    private void Revert()
    {
        Load(_config.Current);
        SetStatus("Changes discarded.", isError: false);
    }

    private void SetStatus(string message, bool isError)
    {
        StatusMessage = message;
        StatusIsError = isError;
    }
}

public sealed partial class SliceRowViewModel : ObservableObject
{
    private readonly SettingsViewModel _owner;

    [ObservableProperty]
    private int _position;

    [ObservableProperty]
    private string _label;

    [ObservableProperty]
    private string _path;

    [ObservableProperty]
    private int _iconIndex;

    [ObservableProperty]
    private int _actionIndex;

    public SliceRowViewModel(SettingsViewModel owner, SliceConfig slice)
    {
        _owner = owner;
        _label = slice.Label ?? "";
        _path = slice.Path ?? "";
        ColorEditor = new ColorEditorViewModel(slice.Color);
        ColorEditor.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(ColorEditorViewModel.Hex))
            {
                OnPropertyChanged(nameof(ColorText));
                OnPropertyChanged(nameof(SwatchBrush));
                OnPropertyChanged(nameof(ColorInvalid));
            }
        };
        _iconIndex = (int)(slice.Icon ?? SliceIcon.Folder);
        _actionIndex = (int)slice.Action;
    }

    /// <summary>The popup next to the color swatch: palette, hex box and hue / saturation / brightness sliders.</summary>
    public ColorEditorViewModel ColorEditor { get; }

    /// <summary>Hex text as typed ("" = default color).</summary>
    public string ColorText
    {
        get => ColorEditor.Hex;
        set => ColorEditor.Hex = value ?? "";
    }

    /// <summary>True while this row is being dragged to a new position (drives the lifted look).</summary>
    [ObservableProperty]
    private bool _isDragging;

    public int Number => Position + 1;

    public bool CanMoveUp => Position > 0;

    public bool CanMoveDown => Position < OCnMConfig.SliceCount - 1;

    public string LabelPlaceholder => string.IsNullOrWhiteSpace(Path) ? "Empty" : SliceConfig.FolderName(Path);

    public bool PathMissing => !string.IsNullOrWhiteSpace(Path) && !Directory.Exists(Path);

    /// <summary>Inline validation: shown on the swatch and the hex box as soon as the text is wrong.</summary>
    public bool ColorInvalid => !string.IsNullOrWhiteSpace(ColorText) && ConfigStore.NormalizeColor(ColorText) is null;

    public string RowName => $"Slice {Number}";

    public IBrush SwatchBrush => ConfigStore.NormalizeColor(ColorText) is { } hex
        ? new SolidColorBrush(Color.Parse(hex))
        : Brushes.Transparent;

    public IReadOnlyList<IconOption> IconOptions => SettingsViewModel.IconOptions;

    partial void OnPositionChanged(int value)
    {
        OnPropertyChanged(nameof(Number));
        OnPropertyChanged(nameof(RowName));
        OnPropertyChanged(nameof(CanMoveUp));
        OnPropertyChanged(nameof(CanMoveDown));
    }

    partial void OnPathChanged(string value)
    {
        OnPropertyChanged(nameof(LabelPlaceholder));
        OnPropertyChanged(nameof(PathMissing));
    }

    [RelayCommand]
    private Task Browse() => _owner.BrowseAsync(this);

    [RelayCommand]
    private void MoveUp() => _owner.MoveRow(this, -1);

    [RelayCommand]
    private void MoveDown() => _owner.MoveRow(this, +1);

    [RelayCommand]
    private void Clear()
    {
        Label = "";
        Path = "";
        ColorText = "";
        IconIndex = 0;
        ActionIndex = 0;
    }

    public SliceConfig ToConfig(int index) => new()
    {
        Index = index,
        Label = string.IsNullOrWhiteSpace(Label) ? null : Label.Trim(),
        Path = string.IsNullOrWhiteSpace(Path) ? null : Path.Trim(),
        Color = ConfigStore.NormalizeColor(ColorText),
        Icon = IconIndex <= 0 ? null : (SliceIcon)IconIndex,
        Action = (SliceAction)Math.Clamp(ActionIndex, 0, 2),
    };
}
