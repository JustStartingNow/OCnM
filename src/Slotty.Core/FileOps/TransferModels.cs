using Slotty.Core.Models;

namespace Slotty.Core.FileOps;

public sealed record TransferRequest(
    IReadOnlyList<string> Sources,
    string DestinationDirectory,
    DropAction Action,
    ConflictPolicy ConflictPolicy);

public enum ConflictChoice
{
    KeepBoth,
    Skip,

    /// <summary>Replace an existing file. The old file is kept as a hidden backup until the undo record is discarded.</summary>
    Replace,

    /// <summary>Stop the whole operation.</summary>
    Cancel,
}

/// <param name="CanReplace">False for folders: Slotty never merges or replaces folders.</param>
public sealed record ConflictQuestion(string SourcePath, string ExistingPath, bool CanReplace);

public readonly record struct ConflictAnswer(ConflictChoice Choice, bool ApplyToAll = false);

public delegate Task<ConflictAnswer> ConflictPrompt(ConflictQuestion question, CancellationToken cancellationToken);

public readonly record struct TransferProgress(long BytesDone, long BytesTotal, int ItemsDone, int ItemsTotal, string? CurrentItem)
{
    public double Fraction => BytesTotal > 0
        ? Math.Clamp((double)BytesDone / BytesTotal, 0, 1)
        : ItemsTotal > 0 ? (double)ItemsDone / ItemsTotal : 0;
}

public enum ItemOutcome
{
    Copied,
    Moved,
    Skipped,
    Failed,
}

public sealed record TransferItemResult(string SourcePath, string? DestinationPath, ItemOutcome Outcome, string? Message = null);

public sealed class TransferResult
{
    public required IReadOnlyList<TransferItemResult> Items { get; init; }

    /// <summary>What to revert for undo; null when nothing was changed.</summary>
    public UndoRecord? Undo { get; init; }

    public bool Cancelled { get; init; }

    /// <summary>Set when nothing could be attempted (for example, the destination folder is gone).</summary>
    public string? FatalError { get; init; }

    public int SucceededCount => Items.Count(i => i.Outcome is ItemOutcome.Copied or ItemOutcome.Moved);

    public int FailedCount => Items.Count(i => i.Outcome == ItemOutcome.Failed);

    public int SkippedCount => Items.Count(i => i.Outcome == ItemOutcome.Skipped);

    public bool IsCompleteSuccess => FatalError is null && !Cancelled && FailedCount == 0 && SucceededCount > 0;

    /// <summary>The first message worth showing to the user.</summary>
    public string? FirstProblem => FatalError
        ?? Items.FirstOrDefault(i => i.Outcome == ItemOutcome.Failed)?.Message
        ?? Items.FirstOrDefault(i => i.Message is not null)?.Message;
}

public abstract record UndoEntry;

/// <summary>A file or symlink that Slotty created. Length/LastWriteUtc detect later edits (null = don't check).</summary>
public sealed record CreatedFileEntry(string Path, long? Length, DateTime? LastWriteUtc) : UndoEntry;

public sealed record CreatedDirectoryEntry(string Path) : UndoEntry;

public sealed record MovedEntry(string OriginalPath, string CurrentPath, bool IsDirectory) : UndoEntry;

/// <summary>An existing file at <paramref name="Path"/> that was renamed to <paramref name="BackupPath"/> before being replaced.</summary>
public sealed record ReplacedEntry(string Path, string BackupPath) : UndoEntry;

public sealed class UndoRecord
{
    public UndoRecord(DropAction action, string destination, DateTime completedUtc, IReadOnlyList<UndoEntry> entries)
    {
        Action = action;
        Destination = destination;
        CompletedUtc = completedUtc;
        Entries = entries;
    }

    public DropAction Action { get; }

    public string Destination { get; }

    public DateTime CompletedUtc { get; }

    public IReadOnlyList<UndoEntry> Entries { get; }

    public bool IsSpent { get; internal set; }
}

public sealed record UndoResult(int Reverted, IReadOnlyList<string> Problems)
{
    public bool IsCompleteSuccess => Problems.Count == 0;
}
