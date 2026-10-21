using OCnM.Core.FileOps;

namespace OCnM.App.Services;

/// <summary>Runs transfers one at a time and remembers the last one for Undo.</summary>
public sealed class TransferService
{
    private readonly FileTransferEngine _engine = new();
    private readonly SemaphoreSlim _gate = new(1, 1);
    private UndoRecord? _last;

    public bool CanUndo => _last is { IsSpent: false };

    public async Task<TransferResult> RunAsync(TransferRequest request, IProgress<TransferProgress> progress,
        ConflictPrompt prompt, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var result = await _engine.RunAsync(request, progress, prompt, cancellationToken);
            if (result.Undo is not null)
            {
                // Only the latest operation can be undone; the previous one's replace-backups can go.
                if (_last is not null)
                    FileTransferEngine.Discard(_last);
                _last = result.Undo;
            }
            Log.Info($"Transfer to {request.DestinationDirectory}: {result.SucceededCount} ok, {result.FailedCount} failed, " +
                     $"{result.SkippedCount} skipped{(result.Cancelled ? ", cancelled" : "")}. {result.FirstProblem}");
            return result;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<UndoResult?> UndoLastAsync()
    {
        if (_last is not { IsSpent: false } record)
            return null;
        await _gate.WaitAsync();
        try
        {
            var result = await _engine.UndoAsync(record);
            FileTransferEngine.Discard(record);
            Log.Info($"Undo: {result.Reverted} reverted, problems: {string.Join(" | ", result.Problems)}");
            return result;
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>Called on exit: the last operation can no longer be undone.</summary>
    public void DiscardUndo()
    {
        if (_last is not null)
            FileTransferEngine.Discard(_last);
        _last = null;
    }
}
