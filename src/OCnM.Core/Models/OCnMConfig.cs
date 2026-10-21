using System.Collections.Immutable;

namespace OCnM.Core.Models;

/// <summary>The whole persisted state. Always holds exactly <see cref="SliceCount"/> slices.</summary>
public sealed record OCnMConfig
{
    public const int CurrentVersion = 1;
    public const int SliceCount = 9;

    public int Version { get; init; } = CurrentVersion;

    public AppSettings Settings { get; init; } = new();

    public ImmutableArray<SliceConfig> Slices { get; init; } = CreateEmptySlices();

    public static OCnMConfig CreateDefault() => new();

    public static ImmutableArray<SliceConfig> CreateEmptySlices() =>
        Enumerable.Range(0, SliceCount).Select(SliceConfig.Empty).ToImmutableArray();

    public OCnMConfig WithSlice(SliceConfig slice)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(slice.Index);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(slice.Index, SliceCount);
        return this with { Slices = Slices.SetItem(slice.Index, slice) };
    }

    /// <summary>Swaps two slices, keeping each slice's Index equal to its position.</summary>
    public OCnMConfig SwapSlices(int a, int b)
    {
        if (a == b)
            return this;
        var sa = Slices[a];
        var sb = Slices[b];
        return this with
        {
            Slices = Slices.SetItem(a, sb with { Index = a }).SetItem(b, sa with { Index = b }),
        };
    }

    public bool HasAnyAssignedSlice => Slices.Any(s => s.IsAssigned);
}
