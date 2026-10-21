using OCnM.Core.Config;
using OCnM.Core.Models;

namespace OCnM.App.Services;

/// <summary>Holds the current config and saves every change immediately.</summary>
public sealed class ConfigService
{
    private readonly ConfigStore _store;

    public ConfigService(ConfigStore store, OCnMConfig initial)
    {
        _store = store;
        Current = initial;
    }

    public OCnMConfig Current { get; private set; }

    public event EventHandler<OCnMConfig>? Changed;

    /// <returns>Null on success, otherwise the save error (the change still applies for this session).</returns>
    public string? Update(Func<OCnMConfig, OCnMConfig> change)
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
