using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using OCnM.App.Controls;
using OCnM.Core.Models;

namespace OCnM.App.ViewModels;

public sealed partial class SliceViewModel : ObservableObject, IDonutSlice
{
    [ObservableProperty]
    private DonutSliceState _state;

    [ObservableProperty]
    private string _label = "Empty";

    [ObservableProperty]
    private string? _path;

    [ObservableProperty]
    private Color? _color;

    [ObservableProperty]
    private SliceIcon _icon = SliceIcon.Folder;

    [ObservableProperty]
    private string _actionVerb = "Copy";

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private double _progress = -1;

    [ObservableProperty]
    private DonutFlash _flash;

    [ObservableProperty]
    private long _flashStartedAt;

    public SliceViewModel(int index)
    {
        Index = index;
        Config = SliceConfig.Empty(index);
    }

    public int Index { get; }

    public SliceConfig Config { get; private set; }

    public DropAction EffectiveAction { get; private set; }

    public void Update(SliceConfig config, DropAction globalDefault)
    {
        Config = config;
        EffectiveAction = config.EffectiveAction(globalDefault);
        Label = config.DisplayLabel;
        Path = config.Path;
        Color = config.Color is not null && Avalonia.Media.Color.TryParse(config.Color, out var c) ? c : null;
        Icon = config.Icon ?? SliceIcon.Folder;
        ActionVerb = EffectiveAction == DropAction.Move ? "Move" : "Copy";
        RefreshState();
    }

    /// <summary>Re-checks whether the folder still exists.</summary>
    public void RefreshState()
    {
        State = !Config.IsAssigned ? DonutSliceState.Empty
            : Directory.Exists(Config.Path) ? DonutSliceState.Assigned
            : DonutSliceState.Broken;
        OnPropertyChanged(nameof(Label));
    }

    public void StartFlash(DonutFlash flash)
    {
        FlashStartedAt = Environment.TickCount64;
        Flash = flash;
    }
}
