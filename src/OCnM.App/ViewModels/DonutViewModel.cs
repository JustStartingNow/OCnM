using System.Collections.ObjectModel;
using Avalonia.Input;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OCnM.App.Controls;
using OCnM.App.Services;
using OCnM.Core;
using OCnM.Core.FileOps;
using OCnM.Core.Models;
using OCnM.Core.Platform;

namespace OCnM.App.ViewModels;

/// <summary>Folder picker supplied by the window (it needs a TopLevel).</summary>
public interface IFolderPicker
{
    Task<string?> PickFolderAsync(string title, string? startFolder);
}

public sealed partial class DonutViewModel : ObservableObject
{
    public const string FirstRunHint = "Click a slice or drop a folder on it to set its destination";

    private readonly ConfigService _config;
    private readonly TransferService _transfers;
    private readonly IFolderOpener _folderOpener;
    private CancellationTokenSource? _transferCts;
    private int _activeTransfers;

    [ObservableProperty]
    private string _idleTitle = AppInfo.Name;

    [ObservableProperty]
    private string? _idleSubtitle;

    [ObservableProperty]
    private bool _isPinned;

    [ObservableProperty]
    private object? _overlay;

    /// <summary>True while a picker, menu or overlay needs the window to stay open.</summary>
    [ObservableProperty]
    private bool _isInteracting;

    public DonutViewModel(ConfigService config, TransferService transfers, IFolderOpener folderOpener)
    {
        _config = config;
        _transfers = transfers;
        _folderOpener = folderOpener;
        Slices = new ObservableCollection<SliceViewModel>(Enumerable.Range(0, Core.Models.OCnMConfig.SliceCount).Select(i => new SliceViewModel(i)));
        _config.Changed += (_, c) => Apply(c);
        Apply(_config.Current);
    }

    public ObservableCollection<SliceViewModel> Slices { get; }

    public IReadOnlyList<IDonutSlice> DonutSlices => Slices;

    public ToastViewModel Toast { get; } = new();

    public IFolderPicker? FolderPicker { get; set; }

    /// <summary>Raised when the donut should hide (after opening a folder, after a drop, Esc).</summary>
    public event EventHandler<TimeSpan>? HideRequested;

    public event EventHandler? SettingsRequested;

    /// <summary>Raised when the window must be visible (for example, a conflict question needs an answer).</summary>
    public event EventHandler? ShowRequested;

    public bool IsBusy => _activeTransfers > 0;

    private void Apply(Core.Models.OCnMConfig config)
    {
        for (var i = 0; i < Slices.Count; i++)
            Slices[i].Update(config.Slices[i], config.Settings.DefaultAction);
        IsPinned = config.Settings.Pinned;
        UpdateIdleText(config);
    }

    private void UpdateIdleText(Core.Models.OCnMConfig config)
    {
        if (!config.HasAnyAssignedSlice)
        {
            IdleTitle = "Welcome";
            IdleSubtitle = FirstRunHint;
        }
        else
        {
            IdleTitle = AppInfo.Name;
            IdleSubtitle = (config.Settings.DefaultAction == DropAction.Move ? "Drop files to move" : "Drop files to copy") + "\nPress 1–9 to open";
        }
    }

    /// <summary>Re-checks folder existence; called whenever the donut is shown.</summary>
    public void RefreshSliceStates()
    {
        foreach (var s in Slices)
            s.RefreshState();
    }

    partial void OnIsPinnedChanged(bool value)
    {
        if (_config.Current.Settings.Pinned != value)
            _config.Update(c => c with { Settings = c.Settings with { Pinned = value } });
    }

    [RelayCommand]
    private void OpenSettings() => SettingsRequested?.Invoke(this, EventArgs.Empty);

    // ---- Clicks and keys ----

    public async Task InvokeSliceAsync(int index)
    {
        var slice = Slices[index];
        slice.RefreshState();
        switch (slice.State)
        {
            case DonutSliceState.Empty:
            case DonutSliceState.Broken:
                await SetFolderAsync(index);
                break;
            default:
                OpenFolder(index);
                break;
        }
    }

    /// <summary>Keys 1–9: open the slice's folder (or pick one for an empty slice).</summary>
    public async Task OpenByKeyAsync(int index)
    {
        var slice = Slices[index];
        slice.RefreshState();
        if (slice.State == DonutSliceState.Broken)
        {
            Toast.Show(ToastKind.Error, $"The folder for \"{slice.Label}\" no longer exists.", "Right-click the slice to choose another folder.");
            slice.StartFlash(DonutFlash.Error);
            return;
        }
        await InvokeSliceAsync(index);
    }

    public void OpenFolder(int index)
    {
        var slice = Slices[index];
        slice.RefreshState();
        if (slice.State != DonutSliceState.Assigned)
        {
            Toast.Show(ToastKind.Error, slice.State == DonutSliceState.Empty ? "This slice has no folder yet." : "The folder no longer exists.", slice.Path);
            return;
        }
        var error = _folderOpener.OpenFolder(slice.Path!);
        if (error is not null)
        {
            Toast.Show(ToastKind.Error, "Could not open the folder.", error);
            return;
        }
        if (!IsPinned)
            HideRequested?.Invoke(this, TimeSpan.Zero);
    }

    // ---- Slice configuration ----

    public async Task SetFolderAsync(int index)
    {
        if (FolderPicker is null)
            return;
        var slice = Slices[index];
        string? path;
        IsInteracting = true;
        try
        {
            var start = slice.Config.Path is { } p && Directory.Exists(p) ? p : null;
            path = await FolderPicker.PickFolderAsync($"Choose a folder for slice {index + 1}", start);
        }
        finally
        {
            IsInteracting = false;
        }
        if (path is not null)
            AssignFolder(index, path);
    }

    public void AssignFolder(int index, string path)
    {
        var full = PathRules.Normalize(path);
        ReportSaveError(_config.UpdateSlice(index, s => s with { Path = full }));
        Slices[index].StartFlash(DonutFlash.Success);
        Toast.Show(ToastKind.Success, $"Slice {index + 1} → {Slices[index].Label}", full);
    }

    public void BeginRename(int index)
    {
        var slice = Slices[index];
        var placeholder = slice.Config.Path is { } p ? SliceConfig.FolderName(p) : "Label";
        Overlay = new RenameOverlayViewModel(index, slice.Config.Label ?? "", placeholder,
            (i, label) => ReportSaveError(_config.UpdateSlice(i, s => s with { Label = label })),
            CloseOverlay);
    }

    public void SetColor(int index, string? hex) => ReportSaveError(_config.UpdateSlice(index, s => s with { Color = hex }));

    public void SetIcon(int index, SliceIcon? icon) => ReportSaveError(_config.UpdateSlice(index, s => s with { Icon = icon }));

    public void SetAction(int index, SliceAction action) => ReportSaveError(_config.UpdateSlice(index, s => s with { Action = action }));

    public void ClearSlice(int index)
    {
        ReportSaveError(_config.UpdateSlice(index, _ => SliceConfig.Empty(index)));
        Toast.Show(ToastKind.Info, $"Slice {index + 1} cleared.");
    }

    public void CloseOverlay()
    {
        if (Overlay is ConflictOverlayViewModel conflict)
            conflict.Cancel();
        Overlay = null;
    }

    private void ReportSaveError(string? error)
    {
        if (error is not null)
            Toast.Show(ToastKind.Error, "Could not save settings.", error);
    }

    // ---- Dropping ----

    public async Task DropAsync(int index, IReadOnlyList<string> paths, KeyModifiers modifiers)
    {
        var slice = Slices[index];
        slice.RefreshState();
        if (paths.Count == 0)
        {
            Toast.Show(ToastKind.Error, "Nothing to drop.", "Only local files and folders can be dropped.");
            return;
        }

        if (slice.State != DonutSliceState.Assigned)
        {
            // Dropping a folder on an empty or broken slice assigns it.
            var folder = paths.FirstOrDefault(Directory.Exists);
            if (folder is null)
            {
                Toast.Show(ToastKind.Error,
                    slice.State == DonutSliceState.Empty ? "Drop a folder here to set this slice's destination." : "This slice's folder no longer exists.",
                    slice.State == DonutSliceState.Empty ? "Files can be dropped once the slice has a folder." : "Drop a folder on it or right-click to choose another one.");
                slice.StartFlash(DonutFlash.Error);
                return;
            }
            AssignFolder(index, folder);
            return;
        }

        // Shift forces Move and Ctrl forces Copy, like most file managers.
        var action = modifiers.HasFlag(KeyModifiers.Shift) ? DropAction.Move
            : modifiers.HasFlag(KeyModifiers.Control) ? DropAction.Copy
            : slice.EffectiveAction;
        await TransferAsync(slice, paths, action);
    }

    private async Task TransferAsync(SliceViewModel slice, IReadOnlyList<string> paths, DropAction action)
    {
        var policy = _config.Current.Settings.ConflictPolicy;
        var label = slice.Label;
        var verb = action == DropAction.Move ? "Moving" : "Copying";
        var count = paths.Count;
        var what = count == 1 ? $"\"{UserMessages.DisplayName(paths[0])}\"" : $"{count} items";

        _activeTransfers++;
        OnPropertyChanged(nameof(IsBusy));
        _transferCts = new CancellationTokenSource();
        slice.IsBusy = true;
        slice.Progress = -1;
        Toast.ShowProgress($"{verb} {what} to {label}…", 0);

        var progress = new Progress<TransferProgress>(p =>
        {
            slice.Progress = p.BytesTotal > 0 ? p.Fraction : -1;
            Toast.ShowProgress($"{verb} {what} to {label}… {p.Fraction:P0}", p.Fraction);
        });

        TransferResult result;
        try
        {
            var request = new TransferRequest(paths, slice.Path!, action, policy);
            result = await _transfers.RunAsync(request, progress, (q, ct) => AskConflictAsync(q, label, ct), _transferCts.Token);
        }
        catch (Exception ex)
        {
            Log.Error("Transfer crashed", ex);
            result = new TransferResult { Items = [], FatalError = ex.Message };
        }
        finally
        {
            slice.IsBusy = false;
            _activeTransfers--;
            OnPropertyChanged(nameof(IsBusy));
            _transferCts?.Dispose();
            _transferCts = null;
        }

        ReportResult(slice, result, action, label);
    }

    private void ReportResult(SliceViewModel slice, TransferResult result, DropAction action, string label)
    {
        var done = result.SucceededCount;
        var items = done == 1 ? "1 item" : $"{done} items";
        var verbPast = action == DropAction.Move ? "moved" : "copied";
        var hideAfterMs = _config.Current.Settings.HideAfterDropMs;

        if (result.IsCompleteSuccess)
        {
            slice.StartFlash(DonutFlash.Success);
            var skipped = result.SkippedCount > 0 ? $"{result.SkippedCount} skipped. " : "";
            Toast.Show(ToastKind.Success, $"{items} → {label}", skipped + result.FirstProblem, canUndo: _transfers.CanUndo);
            if (hideAfterMs > 0 && !IsPinned)
                HideRequested?.Invoke(this, TimeSpan.FromMilliseconds(hideAfterMs));
            return;
        }

        if (result.Cancelled)
        {
            slice.StartFlash(DonutFlash.Error);
            Toast.Show(ToastKind.Info, done > 0 ? $"Cancelled after {items} {verbPast}." : "Cancelled.", null, canUndo: done > 0 && _transfers.CanUndo);
            return;
        }

        if (done == 0 && result.FailedCount == 0 && result.FatalError is null)
        {
            // Everything skipped (already exists / already there).
            Toast.Show(ToastKind.Info, "Nothing was " + verbPast + ".", result.FirstProblem);
            return;
        }

        slice.StartFlash(DonutFlash.Error);
        slice.RefreshState();
        var title = done > 0
            ? $"{items} {verbPast}, {result.FailedCount} failed"
            : $"Could not {(action == DropAction.Move ? "move" : "copy")} to {label}";
        Toast.Show(ToastKind.Error, title, result.FirstProblem, canUndo: done > 0 && _transfers.CanUndo);
    }

    private async Task<ConflictAnswer> AskConflictAsync(ConflictQuestion question, string label, CancellationToken ct)
    {
        // Called from the transfer thread; the question is shown inline over the donut.
        var vm = await Dispatcher.UIThread.InvokeAsync(() =>
        {
            var overlay = new ConflictOverlayViewModel(question, label);
            Overlay = overlay;
            ShowRequested?.Invoke(this, EventArgs.Empty);
            return overlay;
        });
        using var registration = ct.Register(vm.Cancel);
        var answer = await vm.Answer;
        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            if (ReferenceEquals(Overlay, vm))
                Overlay = null;
        });
        return answer;
    }

    [RelayCommand]
    private void CancelTransfer() => _transferCts?.Cancel();

    [RelayCommand]
    private async Task UndoAsync()
    {
        Toast.ShowProgress("Undoing…", 0);
        Toast.CanCancel = false;
        var result = await _transfers.UndoLastAsync();
        foreach (var s in Slices)
            s.RefreshState();
        if (result is null)
            Toast.Show(ToastKind.Info, "Nothing to undo.");
        else if (result.IsCompleteSuccess)
            Toast.Show(ToastKind.Success, "Undone.", result.Reverted == 1 ? "1 item reverted." : $"{result.Reverted} items reverted.");
        else
            Toast.Show(ToastKind.Error, "Undo was incomplete.", string.Join(" ", result.Problems.Take(2)));
    }

    [RelayCommand]
    private void DismissToast() => Toast.Hide();
}
