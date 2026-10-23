using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OCnM.Core.FileOps;

namespace OCnM.App.ViewModels;

/// <summary>Inline rename card shown over the donut (no extra window, so it works the same on Wayland).</summary>
public sealed partial class RenameOverlayViewModel(int index, string current, string placeholder, Action<int, string?> commit, Action close) : ObservableObject
{
    [ObservableProperty]
    private string _text = current;

    public string Placeholder { get; } = placeholder;

    public string Title { get; } = $"Rename slice {index + 1}";

    [RelayCommand]
    private void Save()
    {
        commit(index, string.IsNullOrWhiteSpace(Text) ? null : Text.Trim());
        close();
    }

    [RelayCommand]
    private void Cancel() => close();
}

/// <summary>Inline custom-color card (hex box, palette and sliders) shown over the donut.</summary>
public sealed partial class ColorOverlayViewModel : ObservableObject
{
    private readonly int _index;
    private readonly Action<int, string?> _commit;
    private readonly Action _close;

    public ColorOverlayViewModel(int index, string? currentHex, Action<int, string?> commit, Action close)
    {
        _index = index;
        _commit = commit;
        _close = close;
        Editor = new ColorEditorViewModel(currentHex);
        Editor.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(ColorEditorViewModel.IsInvalid))
                SaveCommand.NotifyCanExecuteChanged();
        };
        Title = $"Color for slice {index + 1}";
    }

    public string Title { get; }

    public ColorEditorViewModel Editor { get; }

    private bool CanSave() => !Editor.IsInvalid;

    [RelayCommand(CanExecute = nameof(CanSave))]
    private void Save()
    {
        // An empty box means "use the default color".
        _commit(_index, Editor.HasColor ? OCnM.Core.Config.ConfigStore.NormalizeColor(Editor.Hex) : null);
        _close();
    }

    [RelayCommand]
    private void Cancel() => _close();
}

/// <summary>Inline "file already exists" question for the Ask conflict policy.</summary>
public sealed partial class ConflictOverlayViewModel : ObservableObject
{
    private readonly TaskCompletionSource<ConflictAnswer> _answer = new(TaskCreationOptions.RunContinuationsAsynchronously);

    [ObservableProperty]
    private bool _applyToAll;

    public ConflictOverlayViewModel(ConflictQuestion question, string destinationLabel)
    {
        Question = question;
        Title = $"\"{UserMessages.DisplayName(question.ExistingPath)}\" already exists";
        Message = $"There is already an item with this name in {destinationLabel}.";
    }

    public ConflictQuestion Question { get; }

    public string Title { get; }

    public string Message { get; }

    public bool CanReplace => Question.CanReplace;

    public Task<ConflictAnswer> Answer => _answer.Task;

    [RelayCommand]
    private void KeepBoth() => _answer.TrySetResult(new ConflictAnswer(ConflictChoice.KeepBoth, ApplyToAll));

    [RelayCommand]
    private void Skip() => _answer.TrySetResult(new ConflictAnswer(ConflictChoice.Skip, ApplyToAll));

    [RelayCommand]
    private void Replace() => _answer.TrySetResult(new ConflictAnswer(ConflictChoice.Replace, ApplyToAll));

    [RelayCommand]
    public void Cancel() => _answer.TrySetResult(new ConflictAnswer(ConflictChoice.Cancel));
}
