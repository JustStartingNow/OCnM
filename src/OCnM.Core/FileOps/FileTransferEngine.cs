using System.Buffers;
using System.Diagnostics;
using System.Security.Cryptography;
using OCnM.Core.Models;

namespace OCnM.Core.FileOps;

/// <summary>
/// Copies or moves dropped items into a folder. Guarantees: existing files are never overwritten
/// unless the user chose Replace (and even then the old file is kept until undo expires); a folder
/// is never copied into itself; a move across volumes only deletes the source after the copy was
/// verified; an interrupted item leaves no half-written file under its final name.
/// </summary>
public sealed class FileTransferEngine
{
    private const int BufferSize = 1 << 20;
    private const string PartialSuffix = ".slotty-partial";
    private static readonly TimeSpan ProgressInterval = TimeSpan.FromMilliseconds(50);

    private static readonly EnumerationOptions AllEntries = new()
    {
        AttributesToSkip = 0,
        IgnoreInaccessible = false,
        RecurseSubdirectories = false,
        ReturnSpecialDirectories = false,
    };

    /// <summary>Decides between rename and copy+verify+delete. Replaceable so tests can force the cross-volume path.</summary>
    public Func<string, string, bool> IsSameVolume { get; init; } = PathRules.AreOnSameVolume;

    public TimeProvider Time { get; init; } = TimeProvider.System;

    public Task<TransferResult> RunAsync(
        TransferRequest request,
        IProgress<TransferProgress>? progress = null,
        ConflictPrompt? prompt = null,
        CancellationToken cancellationToken = default)
    {
        // File-system calls are synchronous in places (enumeration, rename); keep them off the caller's thread.
        return Task.Run(() => RunCoreAsync(request, progress, prompt, cancellationToken), CancellationToken.None);
    }

    private async Task<TransferResult> RunCoreAsync(
        TransferRequest request, IProgress<TransferProgress>? progress, ConflictPrompt? prompt, CancellationToken ct)
    {
        var dest = request.DestinationDirectory;
        if (!Directory.Exists(dest))
        {
            return new TransferResult
            {
                Items = request.Sources.Select(s => new TransferItemResult(s, null, ItemOutcome.Failed, "Destination missing.")).ToList(),
                FatalError = $"The folder \"{UserMessages.DisplayName(dest)}\" no longer exists. Right-click the slice to choose another folder.",
            };
        }

        dest = PathRules.Normalize(dest);
        var sources = request.Sources
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .Select(PathRules.Normalize)
            .Distinct(PathRules.PathComparison == StringComparison.Ordinal ? StringComparer.Ordinal : StringComparer.OrdinalIgnoreCase)
            .ToList();

        var run = new RunState(progress, sources.Count, Time);
        foreach (var s in sources)
            run.BytesTotal += MeasureBytes(s);
        run.Report(force: true);

        var results = new List<TransferItemResult>(sources.Count);
        var cancelled = false;
        foreach (var source in sources)
        {
            if (cancelled || ct.IsCancellationRequested)
            {
                cancelled = true;
                results.Add(new TransferItemResult(source, null, ItemOutcome.Skipped, "Cancelled."));
                continue;
            }

            run.CurrentItem = UserMessages.DisplayName(source);
            var itemStartBytes = run.BytesDone;
            var itemBytes = MeasureBytes(source);
            try
            {
                results.Add(await ProcessItemAsync(source, dest, request, run, prompt, ct).ConfigureAwait(false));
            }
            catch (OperationCanceledException)
            {
                cancelled = true;
                results.Add(new TransferItemResult(source, null, ItemOutcome.Skipped, "Cancelled."));
            }

            // Skipped or failed items still count as handled for the progress bar.
            run.BytesDone = Math.Max(run.BytesDone, itemStartBytes + itemBytes);
            run.ItemsDone++;
            run.Report(force: true);
        }

        var undo = run.Undo.Count > 0
            ? new UndoRecord(request.Action, dest, Time.GetUtcNow().UtcDateTime, run.Undo.ToList())
            : null;
        return new TransferResult { Items = results, Undo = undo, Cancelled = cancelled };
    }

    private async Task<TransferItemResult> ProcessItemAsync(
        string source, string dest, TransferRequest request, RunState run, ConflictPrompt? prompt, CancellationToken ct)
    {
        var info = GetInfo(source);
        if (info is null)
            return Failed(source, $"\"{UserMessages.DisplayName(source)}\" no longer exists.");

        var name = Path.GetFileName(source);
        if (string.IsNullOrEmpty(name))
            return Failed(source, "A whole drive can't be dropped. Drop the folders inside it instead.");

        var isLink = info.LinkTarget is not null;
        var isDir = info is DirectoryInfo && !isLink;
        var verb = request.Action == DropAction.Move ? "move" : "copy";

        if (isDir && PathRules.WouldNestIntoItself(source, dest))
            return Failed(source, $"Can't {verb} the folder \"{name}\" into itself or one of its subfolders.");

        if (request.Action == DropAction.Move && Path.GetDirectoryName(source) is { } parent && PathRules.PathsEqual(parent, dest))
            return new TransferItemResult(source, source, ItemOutcome.Skipped, $"\"{name}\" is already in this folder.");

        var target = Path.Combine(dest, name);
        string? backup = null;
        if (run.IsTaken(target))
        {
            var existingIsFile = File.Exists(target) && !Directory.Exists(target);
            var canReplace = !isDir && existingIsFile;
            var choice = await DecideConflictAsync(request.ConflictPolicy, source, target, canReplace, run, prompt, ct).ConfigureAwait(false);
            switch (choice)
            {
                case ConflictChoice.Skip:
                    return new TransferItemResult(source, null, ItemOutcome.Skipped, $"\"{name}\" already exists in the destination.");
                case ConflictChoice.Cancel:
                    throw new OperationCanceledException();
                case ConflictChoice.Replace when canReplace:
                    backup = Path.Combine(dest, $".{name}.{Token()}.slotty-replaced");
                    File.Move(target, backup, overwrite: false);
                    break;
                default:
                    target = Path.Combine(dest, ConflictNaming.GetAvailableName(dest, name, isDir, run.IsTaken));
                    break;
            }
        }

        run.Reserved.Add(target);
        var undoMark = run.Undo.Count;
        if (backup is not null)
            run.Undo.Add(new ReplacedEntry(target, backup));
        try
        {
            var result = request.Action == DropAction.Move
                ? await MoveAsync(source, target, isDir, isLink, run, ct).ConfigureAwait(false)
                : await CopyAsync(source, target, isDir, isLink, run, ct).ConfigureAwait(false);
            if (backup is not null && result.Outcome == ItemOutcome.Failed && !Exists(target))
                RestoreBackup(run, undoMark, target, backup);
            return result;
        }
        catch (Exception ex)
        {
            if (backup is not null && !Exists(target))
                RestoreBackup(run, undoMark, target, backup);
            if (ex is OperationCanceledException)
                throw;
            return Failed(source, UserMessages.Describe(ex, source));
        }
    }

    private static void RestoreBackup(RunState run, int undoMark, string target, string backup)
    {
        try
        {
            File.Move(backup, target, overwrite: false);
            run.Undo.RemoveRange(undoMark, run.Undo.Count - undoMark);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // The ReplacedEntry stays in the undo record, so the backup can still be restored by Undo.
        }
    }

    private static async Task<ConflictChoice> DecideConflictAsync(
        ConflictPolicy policy, string source, string existing, bool canReplace, RunState run, ConflictPrompt? prompt, CancellationToken ct)
    {
        switch (policy)
        {
            case ConflictPolicy.Skip:
                return ConflictChoice.Skip;
            case ConflictPolicy.Ask when prompt is not null:
                if (run.RememberedAnswer is { } remembered)
                    return remembered == ConflictChoice.Replace && !canReplace ? ConflictChoice.KeepBoth : remembered;
                var answer = await prompt(new ConflictQuestion(source, existing, canReplace), ct).ConfigureAwait(false);
                var choice = answer.Choice == ConflictChoice.Replace && !canReplace ? ConflictChoice.KeepBoth : answer.Choice;
                if (answer.ApplyToAll && choice != ConflictChoice.Cancel)
                    run.RememberedAnswer = answer.Choice;
                return choice;
            default:
                return ConflictChoice.KeepBoth;
        }
    }

    private async Task<TransferItemResult> CopyAsync(string source, string target, bool isDir, bool isLink, RunState run, CancellationToken ct)
    {
        if (isLink)
        {
            CopyLink(source, target);
            run.Undo.Add(new CreatedFileEntry(target, null, null));
            return new TransferItemResult(source, target, ItemOutcome.Copied);
        }

        if (!isDir)
        {
            await CopyFileAsync(source, target, verify: false, run, ct).ConfigureAwait(false);
            run.Undo.Add(CreatedEntryFor(target));
            return new TransferItemResult(source, target, ItemOutcome.Copied);
        }

        Directory.CreateDirectory(target);
        var errors = new List<string>();
        try
        {
            await CopyTreeAsync(source, target, verify: false, run, errors, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            TryDeleteTree(target);
            throw;
        }

        run.Undo.Add(new CreatedDirectoryEntry(target));
        if (errors.Count == 0)
            return new TransferItemResult(source, target, ItemOutcome.Copied);
        return new TransferItemResult(source, target, ItemOutcome.Failed,
            $"\"{UserMessages.DisplayName(source)}\" was copied partly: {errors.Count} item(s) failed. {errors[0]}");
    }

    private async Task<TransferItemResult> MoveAsync(string source, string target, bool isDir, bool isLink, RunState run, CancellationToken ct)
    {
        var destDir = Path.GetDirectoryName(target)!;
        if (IsSameVolume(source, destDir))
        {
            try
            {
                if (isDir)
                    Directory.Move(source, target);
                else
                    File.Move(source, target, overwrite: false);
                run.BytesDone += MeasureBytes(target);
                run.Report();
                run.Undo.Add(new MovedEntry(source, target, isDir));
                return new TransferItemResult(source, target, ItemOutcome.Moved);
            }
            catch (IOException) when (Exists(source) && !Exists(target))
            {
                // Typically EXDEV (bind mounts, subvolumes) misdetected as one volume: fall back to copy.
            }
        }

        return await MoveAcrossVolumesAsync(source, target, isDir, isLink, run, ct).ConfigureAwait(false);
    }

    private async Task<TransferItemResult> MoveAcrossVolumesAsync(string source, string target, bool isDir, bool isLink, RunState run, CancellationToken ct)
    {
        var name = UserMessages.DisplayName(source);
        if (isLink || !isDir)
        {
            if (isLink)
                CopyLink(source, target);
            else
                await CopyFileAsync(source, target, verify: true, run, ct).ConfigureAwait(false);

            try
            {
                DeleteFile(source);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                run.Undo.Add(isLink ? new CreatedFileEntry(target, null, null) : CreatedEntryFor(target));
                return new TransferItemResult(source, target, ItemOutcome.Copied,
                    $"\"{name}\" was copied, but the original could not be removed: {UserMessages.Describe(ex, source)}");
            }
            run.Undo.Add(new MovedEntry(source, target, false));
            return new TransferItemResult(source, target, ItemOutcome.Moved);
        }

        Directory.CreateDirectory(target);
        var errors = new List<string>();
        try
        {
            await CopyTreeAsync(source, target, verify: true, run, errors, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            TryDeleteTree(target);
            throw;
        }

        if (errors.Count > 0)
        {
            // Keep the source intact and remove the incomplete copy.
            TryDeleteTree(target);
            return Failed(source, $"Nothing was moved: {errors.Count} item(s) in \"{name}\" could not be copied. {errors[0]}");
        }

        try
        {
            ForceDeleteDirectory(source);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            run.Undo.Add(new CreatedDirectoryEntry(target));
            return new TransferItemResult(source, target, ItemOutcome.Copied,
                $"\"{name}\" was copied, but some originals could not be removed: {UserMessages.Describe(ex, source)}");
        }
        run.Undo.Add(new MovedEntry(source, target, true));
        return new TransferItemResult(source, target, ItemOutcome.Moved);
    }

    private async Task CopyTreeAsync(string sourceDir, string targetDir, bool verify, RunState run, List<string> errors, CancellationToken ct)
    {
        IEnumerable<FileSystemInfo> entries;
        try
        {
            entries = new DirectoryInfo(sourceDir).EnumerateFileSystemInfos("*", AllEntries).ToList();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            errors.Add(UserMessages.Describe(ex, sourceDir));
            return;
        }

        foreach (var entry in entries)
        {
            ct.ThrowIfCancellationRequested();
            var dst = Path.Combine(targetDir, entry.Name);
            try
            {
                if (entry.LinkTarget is not null)
                {
                    CopyLink(entry.FullName, dst);
                }
                else if (entry is DirectoryInfo)
                {
                    Directory.CreateDirectory(dst);
                    await CopyTreeAsync(entry.FullName, dst, verify, run, errors, ct).ConfigureAwait(false);
                }
                else
                {
                    await CopyFileAsync(entry.FullName, dst, verify, run, ct).ConfigureAwait(false);
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                errors.Add(UserMessages.Describe(ex, entry.FullName));
            }
        }
    }

    /// <summary>
    /// Copies into a hidden partial file and renames it into place, so an interruption never leaves a
    /// truncated file under the final name. With <paramref name="verify"/>, the copy is flushed to disk and
    /// re-read to compare SHA-256 hashes before the caller deletes the source.
    /// </summary>
    private async Task CopyFileAsync(string source, string target, bool verify, RunState run, CancellationToken ct)
    {
        var dir = Path.GetDirectoryName(target)!;
        var partial = Path.Combine(dir, $".{Path.GetFileName(target)}.{Token()}{PartialSuffix}");
        var buffer = ArrayPool<byte>.Shared.Rent(BufferSize);
        var copied = 0L;
        try
        {
            byte[]? sourceHash = null;
            await using (var input = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read, 1, FileOptions.Asynchronous | FileOptions.SequentialScan))
            await using (var output = new FileStream(partial, FileMode.CreateNew, FileAccess.Write, FileShare.None, 1, FileOptions.Asynchronous))
            {
                using var hash = verify ? IncrementalHash.CreateHash(HashAlgorithmName.SHA256) : null;
                int read;
                while ((read = await input.ReadAsync(buffer.AsMemory(0, BufferSize), ct).ConfigureAwait(false)) > 0)
                {
                    await output.WriteAsync(buffer.AsMemory(0, read), ct).ConfigureAwait(false);
                    hash?.AppendData(buffer, 0, read);
                    copied += read;
                    run.BytesDone += read;
                    run.Report();
                }
                await output.FlushAsync(ct).ConfigureAwait(false);
                if (verify)
                    output.Flush(flushToDisk: true);
                sourceHash = hash?.GetHashAndReset();
            }

            if (sourceHash is not null)
            {
                var copyHash = await HashFileAsync(partial, buffer, ct).ConfigureAwait(false);
                if (!CryptographicOperations.FixedTimeEquals(sourceHash, copyHash))
                    throw new IOException($"Verification failed for \"{UserMessages.DisplayName(source)}\"; the original was kept.");
            }

            CopyMetadata(source, partial);
            File.Move(partial, target, overwrite: false);
            if (!OperatingSystem.IsWindows())
                return;
            // Read-only is applied last on Windows: it would otherwise block the rename above.
            var attrs = File.GetAttributes(source);
            if ((attrs & FileAttributes.ReadOnly) != 0)
                File.SetAttributes(target, File.GetAttributes(target) | FileAttributes.ReadOnly);
        }
        catch
        {
            run.BytesDone -= copied;
            TryDeleteFile(partial);
            throw;
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    private static async Task<byte[]> HashFileAsync(string path, byte[] buffer, CancellationToken ct)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1, FileOptions.Asynchronous | FileOptions.SequentialScan);
        int read;
        while ((read = await stream.ReadAsync(buffer.AsMemory(0, BufferSize), ct).ConfigureAwait(false)) > 0)
            hash.AppendData(buffer, 0, read);
        return hash.GetHashAndReset();
    }

    private static void CopyMetadata(string source, string target)
    {
        try
        {
            File.SetLastWriteTimeUtc(target, File.GetLastWriteTimeUtc(source));
            if (!OperatingSystem.IsWindows())
                File.SetUnixFileMode(target, File.GetUnixFileMode(source));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Metadata is nice to have; the content is what matters.
        }
    }

    private static void CopyLink(string source, string target)
    {
        FileSystemInfo info = Directory.Exists(source) ? new DirectoryInfo(source) : new FileInfo(source);
        var linkTarget = info.LinkTarget ?? throw new IOException("Not a symbolic link.");
        if (info is DirectoryInfo)
            Directory.CreateSymbolicLink(target, linkTarget);
        else
            File.CreateSymbolicLink(target, linkTarget);
    }

    // ---- Undo ----

    /// <summary>Reverts a finished operation. Items changed since the drop are kept and reported.</summary>
    public Task<UndoResult> UndoAsync(UndoRecord record, CancellationToken cancellationToken = default) =>
        Task.Run(() => UndoCoreAsync(record, cancellationToken), CancellationToken.None);

    private async Task<UndoResult> UndoCoreAsync(UndoRecord record, CancellationToken ct)
    {
        if (record.IsSpent)
            return new UndoResult(0, ["This operation was already undone."]);
        record.IsSpent = true;

        var problems = new List<string>();
        var reverted = 0;
        var run = new RunState(null, 0, Time);
        for (var i = record.Entries.Count - 1; i >= 0; i--)
        {
            ct.ThrowIfCancellationRequested();
            var entry = record.Entries[i];
            try
            {
                switch (entry)
                {
                    case CreatedFileEntry f:
                        if (!Exists(f.Path))
                            break;
                        if (f.Length is { } len && (new FileInfo(f.Path).Length != len || File.GetLastWriteTimeUtc(f.Path) != f.LastWriteUtc))
                        {
                            problems.Add($"\"{UserMessages.DisplayName(f.Path)}\" was changed after the drop, so it was kept.");
                            break;
                        }
                        DeleteFileOrLink(f.Path);
                        reverted++;
                        break;

                    case CreatedDirectoryEntry d:
                        if (!Directory.Exists(d.Path))
                            break;
                        if (HasFilesNewerThan(d.Path, record.CompletedUtc))
                        {
                            problems.Add($"\"{UserMessages.DisplayName(d.Path)}\" has changed since the drop, so it was kept.");
                            break;
                        }
                        ForceDeleteDirectory(d.Path);
                        reverted++;
                        break;

                    case MovedEntry m:
                        if (!Exists(m.CurrentPath))
                        {
                            problems.Add($"\"{UserMessages.DisplayName(m.CurrentPath)}\" is no longer where it was moved to.");
                            break;
                        }
                        if (Exists(m.OriginalPath))
                        {
                            problems.Add($"Something named \"{UserMessages.DisplayName(m.OriginalPath)}\" now exists at the original location.");
                            break;
                        }
                        var info = GetInfo(m.CurrentPath)!;
                        var isLink = info.LinkTarget is not null;
                        var result = await MoveAsync(m.CurrentPath, m.OriginalPath, m.IsDirectory && !isLink, isLink, run, ct).ConfigureAwait(false);
                        if (result.Outcome == ItemOutcome.Moved)
                            reverted++;
                        else
                            problems.Add(result.Message ?? $"\"{UserMessages.DisplayName(m.CurrentPath)}\" could not be moved back.");
                        break;

                    case ReplacedEntry r:
                        if (!File.Exists(r.BackupPath))
                            break;
                        if (Exists(r.Path))
                        {
                            problems.Add($"\"{UserMessages.DisplayName(r.Path)}\" could not be restored because a file with that name exists.");
                            break;
                        }
                        File.Move(r.BackupPath, r.Path, overwrite: false);
                        reverted++;
                        break;
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                problems.Add(UserMessages.Describe(ex, PathOf(entry)));
            }
        }
        return new UndoResult(reverted, problems);
    }

    /// <summary>Called when an operation can no longer be undone: deletes the backups of replaced files.</summary>
    public static void Discard(UndoRecord record)
    {
        record.IsSpent = true;
        foreach (var r in record.Entries.OfType<ReplacedEntry>())
            TryDeleteFile(r.BackupPath);
    }

    // ---- Helpers ----

    private static string PathOf(UndoEntry e) => e switch
    {
        CreatedFileEntry f => f.Path,
        CreatedDirectoryEntry d => d.Path,
        MovedEntry m => m.CurrentPath,
        ReplacedEntry r => r.Path,
        _ => "",
    };

    private static TransferItemResult Failed(string source, string message) => new(source, null, ItemOutcome.Failed, message);

    private static string Token() => Guid.NewGuid().ToString("N")[..8];

    private static FileSystemInfo? GetInfo(string path)
    {
        if (Directory.Exists(path))
            return new DirectoryInfo(path);
        var file = new FileInfo(path);
        return file.Exists || file.LinkTarget is not null ? file : null;
    }

    private static bool Exists(string path) => GetInfo(path) is not null;

    private static CreatedFileEntry CreatedEntryFor(string path)
    {
        var info = new FileInfo(path);
        return new CreatedFileEntry(path, info.Length, info.LastWriteTimeUtc);
    }

    private static long MeasureBytes(string path)
    {
        try
        {
            var info = GetInfo(path);
            if (info is null || info.LinkTarget is not null)
                return 0;
            if (info is FileInfo f)
                return f.Length;
            var options = new EnumerationOptions { AttributesToSkip = FileAttributes.ReparsePoint, IgnoreInaccessible = true, RecurseSubdirectories = true };
            return new DirectoryInfo(path).EnumerateFiles("*", options).Sum(fi => fi.Length);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return 0;
        }
    }

    private static bool HasFilesNewerThan(string dir, DateTime utc)
    {
        var options = new EnumerationOptions { AttributesToSkip = 0, IgnoreInaccessible = true, RecurseSubdirectories = true };
        return new DirectoryInfo(dir).EnumerateFiles("*", options).Any(f => f.LastWriteTimeUtc > utc);
    }

    private static void DeleteFile(string path)
    {
        if (OperatingSystem.IsWindows())
        {
            var attrs = File.GetAttributes(path);
            if ((attrs & FileAttributes.ReadOnly) != 0)
                File.SetAttributes(path, attrs & ~FileAttributes.ReadOnly);
        }
        File.Delete(path);
    }

    private static void DeleteFileOrLink(string path)
    {
        // On Windows a directory symlink is removed with Directory.Delete; on Unix unlink handles both.
        if (OperatingSystem.IsWindows() && Directory.Exists(path) && new DirectoryInfo(path).LinkTarget is not null)
            Directory.Delete(path);
        else
            DeleteFile(path);
    }

    private static void ForceDeleteDirectory(string path)
    {
        if (OperatingSystem.IsWindows())
        {
            var options = new EnumerationOptions { AttributesToSkip = FileAttributes.ReparsePoint, RecurseSubdirectories = true, IgnoreInaccessible = true };
            foreach (var f in new DirectoryInfo(path).EnumerateFiles("*", options))
            {
                if (f.IsReadOnly)
                    f.IsReadOnly = false;
            }
        }
        Directory.Delete(path, recursive: true);
    }

    private static void TryDeleteTree(string path)
    {
        try
        {
            if (Directory.Exists(path))
                ForceDeleteDirectory(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Debug.WriteLine($"Could not clean up {path}: {ex.Message}");
        }
    }

    private static void TryDeleteFile(string path)
    {
        try
        {
            if (File.Exists(path))
                DeleteFile(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Debug.WriteLine($"Could not clean up {path}: {ex.Message}");
        }
    }

    private sealed class RunState(IProgress<TransferProgress>? progress, int itemsTotal, TimeProvider time)
    {
        private long _lastReport = long.MinValue;

        public long BytesDone { get; set; }

        public long BytesTotal { get; set; }

        public int ItemsDone { get; set; }

        public string? CurrentItem { get; set; }

        public List<UndoEntry> Undo { get; } = [];

        /// <summary>Final names chosen in this run, so two dropped items never get the same name.</summary>
        public HashSet<string> Reserved { get; } = new(PathRules.PathComparison == StringComparison.Ordinal ? StringComparer.Ordinal : StringComparer.OrdinalIgnoreCase);

        public ConflictChoice? RememberedAnswer { get; set; }

        public bool IsTaken(string path) => Reserved.Contains(path) || Exists(path);

        public void Report(bool force = false)
        {
            if (progress is null)
                return;
            var now = time.GetTimestamp();
            if (!force && _lastReport != long.MinValue && time.GetElapsedTime(_lastReport, now) < ProgressInterval)
                return;
            _lastReport = now;
            progress.Report(new TransferProgress(Math.Max(0, BytesDone), BytesTotal, ItemsDone, itemsTotal, CurrentItem));
        }
    }
}
