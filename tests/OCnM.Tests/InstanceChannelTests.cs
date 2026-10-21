using System.Net.Sockets;
using OCnM.App.Platform.Ipc;
using OCnM.App.Platform.Linux;
using OCnM.Core.Ipc;
using OCnM.Core.Platform;

namespace OCnM.Tests;

public class InstanceChannelTests : IDisposable
{
    private readonly TempDir _tmp = new();

    public void Dispose() => _tmp.Dispose();

    private IInstanceChannel NewChannel(string name)
    {
        if (OperatingSystem.IsWindows())
            return new NamedPipeChannel("slotty-test-" + name);
        // Short path: Unix socket paths are limited to ~108 bytes.
        return new UnixSocketChannel(Path.Combine(Path.GetTempPath(), $"slt-{name}.sock"));
    }

    [Fact]
    public async Task Second_instance_forwards_commands_to_the_first()
    {
        var name = Guid.NewGuid().ToString("N")[..8];
        var received = new List<IpcCommand>();
        using var server = NewChannel(name);
        Assert.True(server.TryStartServer(c =>
        {
            lock (received)
                received.Add(c);
            return Task.CompletedTask;
        }));

        using var client = NewChannel(name);
        Assert.False(client.TryStartServer(_ => Task.CompletedTask));
        Assert.True(await client.TrySendAsync(IpcCommand.Toggle, TimeSpan.FromSeconds(5)));
        Assert.True(await client.TrySendAsync(IpcCommand.Settings, TimeSpan.FromSeconds(5)));

        lock (received)
            Assert.Equal([IpcCommand.Toggle, IpcCommand.Settings], received);
    }

    [Fact]
    public async Task Sending_without_a_server_fails_fast()
    {
        using var client = NewChannel(Guid.NewGuid().ToString("N")[..8]);
        Assert.False(await client.TrySendAsync(IpcCommand.Show, TimeSpan.FromSeconds(2)));
    }

    [Fact]
    public void Stale_socket_file_is_replaced_and_removed_on_dispose()
    {
        if (OperatingSystem.IsWindows())
            return;
        var path = Path.Combine(Path.GetTempPath(), $"slt-{Guid.NewGuid().ToString("N")[..8]}.sock");
        // A crashed instance leaves its socket file behind; nothing answers on it.
        File.WriteAllText(path, "");

        var channel = new UnixSocketChannel(path);
        Assert.True(channel.TryStartServer(_ => Task.CompletedTask));
        Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode(path));
        channel.Dispose();
        Assert.False(File.Exists(path));
    }

    [Fact]
    public async Task Garbage_requests_are_rejected_without_calling_the_handler()
    {
        if (OperatingSystem.IsWindows())
            return;
        var path = Path.Combine(Path.GetTempPath(), $"slt-{Guid.NewGuid().ToString("N")[..8]}.sock");
        var calls = 0;
        using var server = new UnixSocketChannel(path);
        Assert.True(server.TryStartServer(_ =>
        {
            Interlocked.Increment(ref calls);
            return Task.CompletedTask;
        }));

        using var socket = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
        await socket.ConnectAsync(new UnixDomainSocketEndPoint(path));
        await socket.SendAsync("rm -rf /\n"u8.ToArray(), SocketFlags.None);
        var buffer = new byte[64];
        var n = await socket.ReceiveAsync(buffer, SocketFlags.None);
        Assert.StartsWith("error", System.Text.Encoding.UTF8.GetString(buffer, 0, n));
        Assert.Equal(0, calls);
    }
}

public class XdgAutostartTests
{
    [Theory]
    [InlineData("/usr/bin/slotty", "/usr/bin/slotty")]
    [InlineData("--hide", "--hide")]
    [InlineData("/opt/My Apps/slotty", "\"/opt/My Apps/slotty\"")]
    [InlineData("/opt/a$b", "\"/opt/a\\\\$b\"")]
    [InlineData("/opt/a\\b", "\"/opt/a\\\\\\\\b\"")]
    public void Exec_arguments_are_quoted_per_the_desktop_entry_spec(string arg, string expected)
    {
        Assert.Equal(expected, XdgAutostart.Quote(arg));
    }
}
