using Slotty.Core.FileOps;
using Slotty.Core.Models;

namespace Slotty.Tests;

public class FileTransferEngineTests : IDisposable
{
    private readonly TempDir _tmp = new();
    private readonly string _src;
    private readonly string _dst;

    public FileTransferEngineTests()
    {
        _src = _tmp.Dir("src");
        _dst = _tmp.Dir("dst");
    }

    public void Dispose() => _tmp.Dispose();

    private static FileTransferEngine Engine(bool crossVolume = false) =>
        crossVolume
            ? new FileTransferEngine { IsSameVolume = (_, _) => false, Time = new SteppingClock() }
            : new FileTransferEngine { Time = new SteppingClock() };

    private Task<TransferResult> Run(DropAction action, ConflictPolicy policy, params string[] sources) =>
        Engine().RunAsync(new TransferRequest(sources, _dst, action, policy));

    private static bool IsRoot => !OperatingSystem.IsWindows() && Environment.UserName == "root";

    // ---- Copy ----

    [Fact]
    public async Task Copies_files_and_folders_recursively()
    {
        var file = _tmp.File("src/a.txt", "hello");
        var dir = _tmp.Dir("src", "folder");
        _tmp.File("src/folder/sub/deep.txt", "deep");
        _tmp.File("src/folder/.hidden", "h");
        _tmp.Dir("src", "folder", "empty");

        var result = await Run(DropAction.Copy, ConflictPolicy.AutoRename, file, dir);

        Assert.True(result.IsCompleteSuccess, result.FirstProblem);
        Assert.Equal(2, result.SucceededCount);
        Assert.Equal("hello", File.ReadAllText(Path.Combine(_dst, "a.txt")));
        Assert.Equal("deep", File.ReadAllText(Path.Combine(_dst, "folder", "sub", "deep.txt")));
        Assert.True(File.Exists(Path.Combine(_dst, "folder", ".hidden")));
        Assert.True(Directory.Exists(Path.Combine(_dst, "folder", "empty")));
        Assert.True(File.Exists(file));
        Assert.True(Directory.Exists(dir));
    }

    [Fact]
    public async Task Copy_preserves_modification_time_and_leaves_no_partial_files()
    {
        var file = _tmp.File("src/a.txt");
        var stamp = new DateTime(2020, 1, 2, 3, 4, 5, DateTimeKind.Utc);
        File.SetLastWriteTimeUtc(file, stamp);

        await Run(DropAction.Copy, ConflictPolicy.AutoRename, file);

        Assert.Equal(stamp, File.GetLastWriteTimeUtc(Path.Combine(_dst, "a.txt")));
        Assert.Single(Directory.GetFileSystemEntries(_dst));
    }

    [Fact]
    public async Task Never_overwrites_by_default_and_auto_renames()
    {
        var file = _tmp.File("src/report.pdf", "new");
        _tmp.File("dst/report.pdf", "old");
        _tmp.File("dst/report (1).pdf", "older");

        var result = await Run(DropAction.Copy, ConflictPolicy.AutoRename, file);

        Assert.True(result.IsCompleteSuccess);
        Assert.Equal("old", File.ReadAllText(Path.Combine(_dst, "report.pdf")));
        Assert.Equal("older", File.ReadAllText(Path.Combine(_dst, "report (1).pdf")));
        Assert.Equal("new", File.ReadAllText(Path.Combine(_dst, "report (2).pdf")));
        Assert.Equal(Path.Combine(_dst, "report (2).pdf"), result.Items[0].DestinationPath);
    }

    [Fact]
    public async Task Folder_conflict_is_renamed_not_merged()
    {
        var dir = _tmp.Dir("src", "photos");
        _tmp.File("src/photos/1.jpg", "new");
        _tmp.File("dst/photos/1.jpg", "old");

        await Run(DropAction.Copy, ConflictPolicy.AutoRename, dir);

        Assert.Equal("old", File.ReadAllText(Path.Combine(_dst, "photos", "1.jpg")));
        Assert.Equal("new", File.ReadAllText(Path.Combine(_dst, "photos (1)", "1.jpg")));
    }

    [Fact]
    public async Task Skip_policy_leaves_existing_files_alone()
    {
        var file = _tmp.File("src/a.txt", "new");
        _tmp.File("dst/a.txt", "old");

        var result = await Run(DropAction.Copy, ConflictPolicy.Skip, file);

        Assert.Equal(ItemOutcome.Skipped, result.Items[0].Outcome);
        Assert.Equal("old", File.ReadAllText(Path.Combine(_dst, "a.txt")));
        Assert.Single(Directory.GetFileSystemEntries(_dst));
        Assert.Null(result.Undo);
    }

    [Fact]
    public async Task Ask_policy_can_replace_and_undo_restores_the_old_file()
    {
        var file = _tmp.File("src/a.txt", "new");
        _tmp.File("dst/a.txt", "old");
        var engine = Engine();
        ConflictQuestion? asked = null;

        var result = await engine.RunAsync(new TransferRequest([file], _dst, DropAction.Copy, ConflictPolicy.Ask),
            prompt: (q, _) => { asked = q; return Task.FromResult(new ConflictAnswer(ConflictChoice.Replace)); });

        Assert.NotNull(asked);
        Assert.True(asked!.CanReplace);
        Assert.Equal("new", File.ReadAllText(Path.Combine(_dst, "a.txt")));

        var undo = await engine.UndoAsync(result.Undo!);
        Assert.True(undo.IsCompleteSuccess, string.Join("; ", undo.Problems));
        Assert.Equal("old", File.ReadAllText(Path.Combine(_dst, "a.txt")));
        Assert.Single(Directory.GetFileSystemEntries(_dst));
    }

    [Fact]
    public async Task Discarding_a_replace_deletes_the_hidden_backup()
    {
        var file = _tmp.File("src/a.txt", "new");
        _tmp.File("dst/a.txt", "old");
        var result = await Engine().RunAsync(new TransferRequest([file], _dst, DropAction.Copy, ConflictPolicy.Ask),
            prompt: (_, _) => Task.FromResult(new ConflictAnswer(ConflictChoice.Replace)));
        Assert.Equal(2, Directory.GetFileSystemEntries(_dst).Length);

        FileTransferEngine.Discard(result.Undo!);

        Assert.Single(Directory.GetFileSystemEntries(_dst));
        Assert.Equal("new", File.ReadAllText(Path.Combine(_dst, "a.txt")));
    }

    [Fact]
    public async Task Ask_never_offers_replace_for_folders()
    {
        var dir = _tmp.Dir("src", "x");
        _tmp.Dir("dst", "x");
        var result = await Engine().RunAsync(new TransferRequest([dir], _dst, DropAction.Copy, ConflictPolicy.Ask),
            prompt: (q, _) =>
            {
                Assert.False(q.CanReplace);
                return Task.FromResult(new ConflictAnswer(ConflictChoice.Replace));
            });
        Assert.True(result.IsCompleteSuccess);
        Assert.True(Directory.Exists(Path.Combine(_dst, "x (1)")));
    }

    [Fact]
    public async Task Ask_apply_to_all_is_remembered_and_cancel_stops_the_rest()
    {
        var a = _tmp.File("src/a.txt");
        var b = _tmp.File("src/b.txt");
        var c = _tmp.File("src/c.txt");
        _tmp.File("dst/a.txt", "old");
        _tmp.File("dst/b.txt", "old");
        var asks = 0;

        var result = await Engine().RunAsync(new TransferRequest([a, b, c], _dst, DropAction.Copy, ConflictPolicy.Ask),
            prompt: (_, _) => { asks++; return Task.FromResult(new ConflictAnswer(ConflictChoice.Skip, ApplyToAll: true)); });
        Assert.Equal(1, asks);
        Assert.Equal([ItemOutcome.Skipped, ItemOutcome.Skipped, ItemOutcome.Copied], result.Items.Select(i => i.Outcome));

        var cancelled = await Engine().RunAsync(new TransferRequest([a, c], _dst, DropAction.Copy, ConflictPolicy.Ask),
            prompt: (_, _) => Task.FromResult(new ConflictAnswer(ConflictChoice.Cancel)));
        Assert.True(cancelled.Cancelled);
        Assert.All(cancelled.Items, i => Assert.Equal(ItemOutcome.Skipped, i.Outcome));
    }

    [Fact]
    public async Task Two_dropped_items_with_the_same_name_both_survive()
    {
        var a = _tmp.File("src/one/same.txt", "1");
        var b = _tmp.File("src/two/same.txt", "2");

        var result = await Run(DropAction.Copy, ConflictPolicy.AutoRename, a, b);

        Assert.True(result.IsCompleteSuccess);
        Assert.Equal("1", File.ReadAllText(Path.Combine(_dst, "same.txt")));
        Assert.Equal("2", File.ReadAllText(Path.Combine(_dst, "same (1).txt")));
    }

    [Fact]
    public async Task Unicode_and_long_names_work()
    {
        var longName = new string('x', 200) + ".txt";
        var files = new[] { _tmp.File("src/Ünïcødé 文件 🎉.txt", "u"), _tmp.File("src/" + longName, "l") };

        var result = await Run(DropAction.Copy, ConflictPolicy.AutoRename, files);

        Assert.True(result.IsCompleteSuccess, result.FirstProblem);
        Assert.Equal("u", File.ReadAllText(Path.Combine(_dst, "Ünïcødé 文件 🎉.txt")));
        Assert.Equal("l", File.ReadAllText(Path.Combine(_dst, longName)));
    }

    [Fact]
    public async Task Deeply_nested_long_paths_are_copied()
    {
        var parts = Enumerable.Range(0, 12).Select(i => $"level-{i}-{new string('d', 20)}").ToArray();
        var top = _tmp.Dir("src", "top");
        var deep = Path.Combine([top, .. parts]);
        Directory.CreateDirectory(deep);
        File.WriteAllText(Path.Combine(deep, "f.txt"), "deep");
        Assert.True(Path.Combine(deep, "f.txt").Length > 260);

        var result = await Run(DropAction.Copy, ConflictPolicy.AutoRename, top);

        Assert.True(result.IsCompleteSuccess, result.FirstProblem);
        Assert.Equal("deep", File.ReadAllText(Path.Combine([_dst, "top", .. parts, "f.txt"])));
    }

    [Fact]
    public async Task Read_only_source_files_are_copied_and_can_be_undone()
    {
        var file = _tmp.File("src/ro.txt", "r");
        File.SetAttributes(file, FileAttributes.ReadOnly);
        if (!OperatingSystem.IsWindows())
            File.SetUnixFileMode(file, UnixFileMode.UserRead);
        var engine = Engine();

        var result = await engine.RunAsync(new TransferRequest([file], _dst, DropAction.Copy, ConflictPolicy.AutoRename));
        Assert.True(result.IsCompleteSuccess, result.FirstProblem);

        var undo = await engine.UndoAsync(result.Undo!);
        Assert.True(undo.IsCompleteSuccess, string.Join("; ", undo.Problems));
        Assert.Empty(Directory.GetFileSystemEntries(_dst));
    }

    [Fact]
    public async Task Unreadable_file_gives_a_permission_message()
    {
        if (OperatingSystem.IsWindows() || IsRoot)
            return;
        var file = _tmp.File("src/secret.txt");
        File.SetUnixFileMode(file, UnixFileMode.None);

        var result = await Run(DropAction.Copy, ConflictPolicy.AutoRename, file);

        Assert.Equal(ItemOutcome.Failed, result.Items[0].Outcome);
        Assert.Contains("Permission denied", result.Items[0].Message);
        Assert.Empty(Directory.GetFileSystemEntries(_dst));
    }

    [Fact]
    public async Task Missing_destination_is_a_clear_fatal_error()
    {
        var file = _tmp.File("src/a.txt");
        var result = await Engine().RunAsync(new TransferRequest([file], _tmp.Combine("gone"), DropAction.Copy, ConflictPolicy.AutoRename));
        Assert.NotNull(result.FatalError);
        Assert.Contains("no longer exists", result.FatalError);
        Assert.False(result.IsCompleteSuccess);
    }

    [Fact]
    public async Task Missing_source_fails_only_that_item()
    {
        var file = _tmp.File("src/a.txt");
        var result = await Run(DropAction.Copy, ConflictPolicy.AutoRename, _tmp.Combine("src", "ghost.txt"), file);
        Assert.Equal([ItemOutcome.Failed, ItemOutcome.Copied], result.Items.Select(i => i.Outcome));
    }

    // ---- Self nesting ----

    [Fact]
    public async Task Refuses_to_copy_a_folder_into_itself()
    {
        var result = await Engine().RunAsync(new TransferRequest([_dst], _dst, DropAction.Copy, ConflictPolicy.AutoRename));
        Assert.Equal(ItemOutcome.Failed, result.Items[0].Outcome);
        Assert.Contains("into itself", result.Items[0].Message);
    }

    [Theory]
    [InlineData(DropAction.Copy)]
    [InlineData(DropAction.Move)]
    public async Task Refuses_to_put_a_folder_into_its_own_subfolder(DropAction action)
    {
        var parent = _tmp.Dir("p");
        var child = _tmp.Dir("p", "c", "d");
        var result = await Engine().RunAsync(new TransferRequest([parent], child, action, ConflictPolicy.AutoRename));
        Assert.Equal(ItemOutcome.Failed, result.Items[0].Outcome);
        Assert.Contains("subfolders", result.Items[0].Message);
        Assert.True(Directory.Exists(child));
        Assert.Empty(Directory.GetFileSystemEntries(child));
    }

    // ---- Move ----

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Moves_files_and_folders(bool crossVolume)
    {
        var file = _tmp.File("src/a.txt", "a");
        var dir = _tmp.Dir("src", "d");
        _tmp.File("src/d/inner/b.txt", "b");

        var result = await Engine(crossVolume).RunAsync(new TransferRequest([file, dir], _dst, DropAction.Move, ConflictPolicy.AutoRename));

        Assert.True(result.IsCompleteSuccess, result.FirstProblem);
        Assert.All(result.Items, i => Assert.Equal(ItemOutcome.Moved, i.Outcome));
        Assert.False(File.Exists(file));
        Assert.False(Directory.Exists(dir));
        Assert.Equal("a", File.ReadAllText(Path.Combine(_dst, "a.txt")));
        Assert.Equal("b", File.ReadAllText(Path.Combine(_dst, "d", "inner", "b.txt")));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Undo_of_a_move_puts_everything_back(bool crossVolume)
    {
        var file = _tmp.File("src/a.txt", "a");
        var dir = _tmp.Dir("src", "d");
        _tmp.File("src/d/b.txt", "b");
        var engine = Engine(crossVolume);

        var result = await engine.RunAsync(new TransferRequest([file, dir], _dst, DropAction.Move, ConflictPolicy.AutoRename));
        var undo = await engine.UndoAsync(result.Undo!);

        Assert.True(undo.IsCompleteSuccess, string.Join("; ", undo.Problems));
        Assert.Equal(2, undo.Reverted);
        Assert.Equal("a", File.ReadAllText(file));
        Assert.Equal("b", File.ReadAllText(Path.Combine(dir, "b.txt")));
        Assert.Empty(Directory.GetFileSystemEntries(_dst));
    }

    [Fact]
    public async Task Cross_volume_move_keeps_the_source_when_part_of_a_folder_cannot_be_read()
    {
        if (OperatingSystem.IsWindows() || IsRoot)
            return;
        var dir = _tmp.Dir("src", "d");
        _tmp.File("src/d/ok.txt");
        var locked = _tmp.File("src/d/locked.txt");
        File.SetUnixFileMode(locked, UnixFileMode.None);

        var result = await Engine(crossVolume: true).RunAsync(new TransferRequest([dir], _dst, DropAction.Move, ConflictPolicy.AutoRename));

        Assert.Equal(ItemOutcome.Failed, result.Items[0].Outcome);
        Assert.Contains("Nothing was moved", result.Items[0].Message);
        Assert.True(File.Exists(Path.Combine(dir, "ok.txt")));
        Assert.True(File.Exists(locked));
        Assert.Empty(Directory.GetFileSystemEntries(_dst));
    }

    [Fact]
    public async Task Moving_into_the_folder_it_is_already_in_is_skipped()
    {
        var file = _tmp.File("dst/a.txt");
        var result = await Run(DropAction.Move, ConflictPolicy.AutoRename, file);
        Assert.Equal(ItemOutcome.Skipped, result.Items[0].Outcome);
        Assert.True(File.Exists(file));
        Assert.Single(Directory.GetFileSystemEntries(_dst));
    }

    [Fact]
    public async Task Move_with_conflict_is_renamed()
    {
        var file = _tmp.File("src/a.txt", "new");
        _tmp.File("dst/a.txt", "old");
        await Run(DropAction.Move, ConflictPolicy.AutoRename, file);
        Assert.Equal("old", File.ReadAllText(Path.Combine(_dst, "a.txt")));
        Assert.Equal("new", File.ReadAllText(Path.Combine(_dst, "a (1).txt")));
        Assert.False(File.Exists(file));
    }

    // ---- Undo of copies ----

    [Fact]
    public async Task Undo_of_a_copy_deletes_only_what_was_created()
    {
        var file = _tmp.File("src/a.txt");
        var dir = _tmp.Dir("src", "d");
        _tmp.File("src/d/b.txt");
        _tmp.File("dst/a.txt", "pre-existing");
        var engine = Engine();

        var result = await engine.RunAsync(new TransferRequest([file, dir], _dst, DropAction.Copy, ConflictPolicy.AutoRename));
        var undo = await engine.UndoAsync(result.Undo!);

        Assert.True(undo.IsCompleteSuccess, string.Join("; ", undo.Problems));
        Assert.Equal(["a.txt"], Directory.GetFileSystemEntries(_dst).Select(Path.GetFileName));
        Assert.Equal("pre-existing", File.ReadAllText(Path.Combine(_dst, "a.txt")));
        Assert.True(File.Exists(file));
        Assert.True(File.Exists(Path.Combine(dir, "b.txt")));
    }

    [Fact]
    public async Task Undo_keeps_a_copy_that_was_edited_afterwards()
    {
        var file = _tmp.File("src/a.txt", "orig");
        var engine = Engine();
        var result = await engine.RunAsync(new TransferRequest([file], _dst, DropAction.Copy, ConflictPolicy.AutoRename));
        File.WriteAllText(Path.Combine(_dst, "a.txt"), "edited by user");

        var undo = await engine.UndoAsync(result.Undo!);

        Assert.False(undo.IsCompleteSuccess);
        Assert.Contains("changed", undo.Problems[0]);
        Assert.True(File.Exists(Path.Combine(_dst, "a.txt")));
    }

    [Fact]
    public async Task Undo_runs_only_once()
    {
        var file = _tmp.File("src/a.txt");
        var engine = Engine();
        var result = await engine.RunAsync(new TransferRequest([file], _dst, DropAction.Copy, ConflictPolicy.AutoRename));

        Assert.True((await engine.UndoAsync(result.Undo!)).IsCompleteSuccess);
        Assert.False((await engine.UndoAsync(result.Undo!)).IsCompleteSuccess);
    }

    [Fact]
    public async Task Undo_of_a_move_refuses_to_overwrite_a_new_file_at_the_origin()
    {
        var file = _tmp.File("src/a.txt", "moved");
        var engine = Engine();
        var result = await engine.RunAsync(new TransferRequest([file], _dst, DropAction.Move, ConflictPolicy.AutoRename));
        File.WriteAllText(file, "new file at origin");

        var undo = await engine.UndoAsync(result.Undo!);

        Assert.False(undo.IsCompleteSuccess);
        Assert.Equal("new file at origin", File.ReadAllText(file));
        Assert.Equal("moved", File.ReadAllText(Path.Combine(_dst, "a.txt")));
    }

    // ---- Progress and cancellation ----

    [Fact]
    public async Task Reports_progress_up_to_the_total()
    {
        var big = _tmp.Combine("src", "big.bin");
        await File.WriteAllBytesAsync(big, new byte[5 * 1024 * 1024 + 17]);
        var reports = new List<TransferProgress>();
        var progress = new SyncProgress<TransferProgress>(reports.Add);

        var result = await Engine().RunAsync(new TransferRequest([big], _dst, DropAction.Copy, ConflictPolicy.AutoRename), progress);

        Assert.True(result.IsCompleteSuccess);
        Assert.NotEmpty(reports);
        Assert.Equal(5 * 1024 * 1024 + 17, reports[^1].BytesTotal);
        Assert.Equal(1.0, reports[^1].Fraction);
        Assert.Equal(1, reports[^1].ItemsDone);
    }

    [Fact]
    public async Task Cancellation_leaves_no_partial_files()
    {
        var big = _tmp.Combine("src", "big.bin");
        await File.WriteAllBytesAsync(big, new byte[8 * 1024 * 1024]);
        var other = _tmp.File("src/z.txt");
        using var cts = new CancellationTokenSource();
        var progress = new SyncProgress<TransferProgress>(p =>
        {
            if (p.BytesDone > 0)
                cts.Cancel();
        });

        var result = await Engine().RunAsync(new TransferRequest([big, other], _dst, DropAction.Copy, ConflictPolicy.AutoRename), progress, null, cts.Token);

        Assert.True(result.Cancelled);
        Assert.All(result.Items, i => Assert.Equal(ItemOutcome.Skipped, i.Outcome));
        Assert.Empty(Directory.GetFileSystemEntries(_dst));
    }

    [Fact]
    public async Task Cancelled_cross_volume_move_keeps_the_source()
    {
        var big = _tmp.Combine("src", "big.bin");
        await File.WriteAllBytesAsync(big, new byte[8 * 1024 * 1024]);
        using var cts = new CancellationTokenSource();
        var progress = new SyncProgress<TransferProgress>(p =>
        {
            if (p.BytesDone > 0)
                cts.Cancel();
        });

        var result = await Engine(crossVolume: true).RunAsync(new TransferRequest([big], _dst, DropAction.Move, ConflictPolicy.AutoRename), progress, null, cts.Token);

        Assert.True(result.Cancelled);
        Assert.True(File.Exists(big));
        Assert.Equal(8 * 1024 * 1024, new FileInfo(big).Length);
        Assert.Empty(Directory.GetFileSystemEntries(_dst));
    }

    [Fact]
    public async Task Symlinks_inside_folders_are_copied_as_links()
    {
        if (OperatingSystem.IsWindows())
            return;
        var dir = _tmp.Dir("src", "d");
        _tmp.File("src/d/real.txt", "r");
        File.CreateSymbolicLink(Path.Combine(dir, "link.txt"), "real.txt");
        Directory.CreateSymbolicLink(Path.Combine(dir, "loop"), dir);

        var result = await Run(DropAction.Copy, ConflictPolicy.AutoRename, dir);

        Assert.True(result.IsCompleteSuccess, result.FirstProblem);
        Assert.Equal("real.txt", new FileInfo(Path.Combine(_dst, "d", "link.txt")).LinkTarget);
        Assert.NotNull(new DirectoryInfo(Path.Combine(_dst, "d", "loop")).LinkTarget);
    }

    /// <summary>A clock that jumps one second per reading, so progress throttling never hides a report.</summary>
    private sealed class SteppingClock : TimeProvider
    {
        private long _ticks;

        public override long TimestampFrequency => 1;

        public override long GetTimestamp() => Interlocked.Increment(ref _ticks);
    }

    /// <summary>IProgress that reports synchronously (Progress&lt;T&gt; posts to the thread pool).</summary>
    private sealed class SyncProgress<T>(Action<T> action) : IProgress<T>
    {
        private readonly object _lock = new();

        public void Report(T value)
        {
            lock (_lock)
                action(value);
        }
    }
}
