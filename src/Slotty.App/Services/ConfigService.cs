using Slotty.Core.Config;
using Slotty.Core.Models;

namespace Slotty.App.Services;

/// <summary>Holds the current config and saves every change immediately.</summary>
public sealed class ConfigService
{
    private readonly ConfigStore _store;

    public ConfigService(ConfigStore store, SlottyConfig initial)
    {
        _store = store;
        Current = initial;
    }

    public SlottyConfig Current { get; private set; }

    public event EventHandler<SlottyConfig>? Changed;

    /// <returns>Null on success, otherwise the save error (the change still applies for this session).</returns>
    public string? Update(Func<SlottyConfig, SlottyConfig> change)
    {
        Current = change(Current);
        string? error = null;
        if (!_store.TrySave(Current, out error))
            Log.Error($"Saving config failed: {error}");
        Changed?.Invoke(this, Current);
        return error is null ? null : $"Settings could not be saved: {error}";
    }

    public string? UpdateSlice(int index, Func<SliceConfig, SliceConfig> change) =>
        Update(c => c.WithSlice(change(c.Slices[index]) with { Index = index }));
}
