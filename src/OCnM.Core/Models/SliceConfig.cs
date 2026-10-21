namespace OCnM.Core.Models;

/// <summary>One of the nine donut slices. A slice without <see cref="Path"/> is empty.</summary>
public sealed record SliceConfig
{
    public int Index { get; init; }

    /// <summary>User label; null means "use the folder name".</summary>
    public string? Label { get; init; }

    public string? Path { get; init; }

    /// <summary>Accent color as "#RRGGBB"; null means the theme accent.</summary>
    public string? Color { get; init; }

    public SliceIcon? Icon { get; init; }

    public SliceAction Action { get; init; } = SliceAction.Inherit;

    public bool IsAssigned => !string.IsNullOrWhiteSpace(Path);

    public static SliceConfig Empty(int index) => new() { Index = index };

    /// <summary>The label to display: the user label, else the folder name, else "Empty".</summary>
    public string DisplayLabel
    {
        get
        {
            if (!string.IsNullOrWhiteSpace(Label))
                return Label!;
            if (!IsAssigned)
                return "Empty";
            return FolderName(Path!);
        }
    }

    public DropAction EffectiveAction(DropAction globalDefault) => Action switch
    {
        SliceAction.Copy => DropAction.Copy,
        SliceAction.Move => DropAction.Move,
        _ => globalDefault,
    };

    public static string FolderName(string path)
    {
        var trimmed = System.IO.Path.TrimEndingDirectorySeparator(path);
        var name = System.IO.Path.GetFileName(trimmed);
        // A drive root such as "D:\" or "/" has no file name.
        return string.IsNullOrEmpty(name) ? trimmed : name;
    }
}
