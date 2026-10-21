using System.ComponentModel;
using Avalonia.Media;
using Slotty.Core.Models;

namespace Slotty.App.Controls;

public enum DonutSliceState
{
    Empty,
    Assigned,

    /// <summary>The assigned folder no longer exists.</summary>
    Broken,
}

public enum DonutFlash
{
    None,
    Success,
    Error,
}

/// <summary>What <see cref="DonutMenu"/> needs to draw one slice.</summary>
public interface IDonutSlice : INotifyPropertyChanged
{
    DonutSliceState State { get; }

    string Label { get; }

    string? Path { get; }

    /// <summary>Null uses the theme accent.</summary>
    Color? Color { get; }

    SliceIcon Icon { get; }

    /// <summary>"Copy" or "Move", shown in the hub while dragging over the slice.</summary>
    string ActionVerb { get; }

    bool IsBusy { get; }

    /// <summary>0..1, or a negative value when the total is unknown.</summary>
    double Progress { get; }

    DonutFlash Flash { get; }

    /// <summary>Environment.TickCount64 when <see cref="Flash"/> was set.</summary>
    long FlashStartedAt { get; }
}
