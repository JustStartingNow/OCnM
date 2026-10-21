using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Slotty.App.ViewModels;

public enum ToastKind
{
    Info,
    Success,
    Error,
    Progress,
}

/// <summary>
/// The status line under the donut. Finished messages dismiss themselves (longer for errors and
/// for toasts that offer Undo) but stay while the pointer rests on them.
/// </summary>
public sealed partial class ToastViewModel : ObservableObject
{
    private static readonly TimeSpan InfoDuration = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan UndoDuration = TimeSpan.FromSeconds(7);
    private static readonly TimeSpan ErrorDuration = TimeSpan.FromSeconds(9);
    private static readonly TimeSpan ResumeDuration = TimeSpan.FromSeconds(2);

    private DispatcherTimer? _dismissTimer;

    [ObservableProperty]
    private bool _isVisible;

    [ObservableProperty]
    private ToastKind _kind;

    [ObservableProperty]
    private string _message = "";

    [ObservableProperty]
    private string? _detail;

    [ObservableProperty]
    private bool _canUndo;

    [ObservableProperty]
    private bool _canCancel;

    [ObservableProperty]
    private double _progress;

    [ObservableProperty]
    private bool _isHovered;

    public bool IsError => Kind == ToastKind.Error;

    public bool IsSuccess => Kind == ToastKind.Success;

    public bool IsInfo => Kind == ToastKind.Info;

    public bool IsProgress => Kind == ToastKind.Progress;

    public bool HasDetail => !string.IsNullOrEmpty(Detail);

    partial void OnKindChanged(ToastKind value)
    {
        OnPropertyChanged(nameof(IsError));
        OnPropertyChanged(nameof(IsSuccess));
        OnPropertyChanged(nameof(IsInfo));
        OnPropertyChanged(nameof(IsProgress));
    }

    partial void OnDetailChanged(string? value) => OnPropertyChanged(nameof(HasDetail));

    partial void OnIsHoveredChanged(bool value)
    {
        if (value)
            _dismissTimer?.Stop();
        else if (IsVisible && Kind != ToastKind.Progress)
            ScheduleDismiss(ResumeDuration);
    }

    public void Show(ToastKind kind, string message, string? detail = null, bool canUndo = false)
    {
        Kind = kind;
        Message = message;
        Detail = string.IsNullOrWhiteSpace(detail) ? null : detail;
        CanUndo = canUndo;
        CanCancel = false;
        IsVisible = true;
        ScheduleDismiss(kind == ToastKind.Error ? ErrorDuration : canUndo ? UndoDuration : InfoDuration);
    }

    public void ShowProgress(string message, double progress)
    {
        _dismissTimer?.Stop();
        Kind = ToastKind.Progress;
        Message = message;
        Detail = null;
        CanUndo = false;
        CanCancel = true;
        Progress = progress;
        IsVisible = true;
    }

    public void Hide()
    {
        _dismissTimer?.Stop();
        IsVisible = false;
    }

    private void ScheduleDismiss(TimeSpan after)
    {
        _dismissTimer?.Stop();
        if (IsHovered)
            return;
        _dismissTimer = new DispatcherTimer { Interval = after };
        _dismissTimer.Tick += (_, _) =>
        {
            _dismissTimer?.Stop();
            if (!IsHovered && Kind != ToastKind.Progress)
                IsVisible = false;
        };
        _dismissTimer.Start();
    }
}
