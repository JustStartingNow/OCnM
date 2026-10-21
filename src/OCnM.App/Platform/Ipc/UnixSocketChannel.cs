using System.Net.Sockets;
using System.Runtime.Versioning;
using System.Text;
using OCnM.App.Services;
using OCnM.Core.Ipc;
using OCnM.Core.Platform;

namespace OCnM.App.Platform.Ipc;

/// <summary>Single-instance channel over a Unix domain socket in $XDG_RUNTIME_DIR (mode 0600).</summary>
[UnsupportedOSPlatform("windows")]
public sealed class UnixSocketChannel(string socketPath) : IInstanceChannel
{
    private readonly CancellationTokenSource _cts = new();
    private Socket? _listener;

    public string SocketPath { get; } = socketPath;

    public static string DefaultPath()
    {
        var runtimeDir = Environment.GetEnvironmentVariable("XDG_RUNTIME_DIR");
        if (!string.IsNullOrEmpty(runtimeDir) && Directory.Exists(runtimeDir))
            return Path.Combine(runtimeDir, "ocnm.sock");
        // Without a private runtime dir, include the user name so users don't collide in /tmp.
        return Path.Combine(Path.GetTempPath(), $"ocnm-{Environment.UserName}.sock");
    }

    public async Task<bool> TrySendAsync(IpcCommand command, TimeSpan timeout, CancellationToken cancellationToken = default)
    {
        if (!File.Exists(SocketPath))
            return false;
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(timeout);
        try
        {
            using var socket = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
            await socket.ConnectAsync(new UnixDomainSocketEndPoint(SocketPath), cts.Token);
            await socket.SendAsync(Encoding.UTF8.GetBytes(IpcProtocol.EncodeRequest(command)), SocketFlags.None, cts.Token);
            var reply = await ReadLineAsync(socket, cts.Token);
            return reply is not null && reply.StartsWith(IpcProtocol.Ok, StringComparison.Ordinal);
        }
        catch (Exception ex) when (ex is SocketException or OperationCanceledException or IOException)
        {
            return false;
        }
    }

    public bool TryStartServer(Func<IpcCommand, Task> handler)
    {
        if (File.Exists(SocketPath))
        {
            // A live instance answers; a stale socket file (crash) is removed.
            if (IsAlive())
                return false;
            try
            {
                File.Delete(SocketPath);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                Log.Warn($"Could not remove stale socket {SocketPath}: {ex.Message}");
                return false;
            }
        }

        var listener = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
        try
        {
            listener.Bind(new UnixDomainSocketEndPoint(SocketPath));
            File.SetUnixFileMode(SocketPath, UnixFileMode.UserRead | UnixFileMode.UserWrite);
            listener.Listen(8);
        }
        catch (SocketException)
        {
            // Another instance won the race.
            listener.Dispose();
            return false;
        }

        _listener = listener;
        _ = Task.Run(() => AcceptLoopAsync(listener, handler, _cts.Token));
        return true;
    }

    private bool IsAlive()
    {
        try
        {
            using var probe = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
            probe.Connect(new UnixDomainSocketEndPoint(SocketPath));
            return true;
        }
        catch (SocketException)
        {
            return false;
        }
    }

    private static async Task AcceptLoopAsync(Socket listener, Func<IpcCommand, Task> handler, CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            Socket client;
            try
            {
                client = await listener.AcceptAsync(ct);
            }
            catch (Exception ex) when (ex is OperationCanceledException or ObjectDisposedException or SocketException)
            {
                return;
            }
            _ = Task.Run(() => ServeAsync(client, handler, ct), CancellationToken.None);
        }
    }

    private static async Task ServeAsync(Socket client, Func<IpcCommand, Task> handler, CancellationToken ct)
    {
        using (client)
        {
            try
            {
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
                timeout.CancelAfter(TimeSpan.FromSeconds(3));
                var line = await ReadLineAsync(client, timeout.Token);
                string reply;
                if (IpcProtocol.TryDecodeRequest(line, out var command))
                {
                    await handler(command);
                    reply = IpcProtocol.Ok + "\n";
                }
                else
                {
                    reply = IpcProtocol.EncodeError("bad request");
                }
                await client.SendAsync(Encoding.UTF8.GetBytes(reply), SocketFlags.None, timeout.Token);
            }
            catch (Exception ex) when (ex is SocketException or OperationCanceledException or IOException)
            {
                // Client went away; nothing to do.
            }
        }
    }

    private static async Task<string?> ReadLineAsync(Socket socket, CancellationToken ct)
    {
        var buffer = new byte[IpcProtocol.MaxLineLength + 1];
        var total = 0;
        while (total < buffer.Length)
        {
            var read = await socket.ReceiveAsync(buffer.AsMemory(total), SocketFlags.None, ct);
            if (read == 0)
                break;
            total += read;
            var newline = Array.IndexOf(buffer, (byte)'\n', 0, total);
            if (newline >= 0)
                return Encoding.UTF8.GetString(buffer, 0, newline);
        }
        return total == 0 || total > IpcProtocol.MaxLineLength ? null : Encoding.UTF8.GetString(buffer, 0, total);
    }

    public void Dispose()
    {
        _cts.Cancel();
        if (_listener is null)
            return;
        _listener.Dispose();
        _listener = null;
        try
        {
            File.Delete(SocketPath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Warn($"Could not remove socket {SocketPath}: {ex.Message}");
        }
    }
}
