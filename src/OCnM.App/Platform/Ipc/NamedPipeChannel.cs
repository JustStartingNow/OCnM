using System.IO.Pipes;
using System.Runtime.Versioning;
using System.Text;
using OCnM.Core.Ipc;
using OCnM.Core.Platform;

namespace OCnM.App.Platform.Ipc;

/// <summary>Single-instance channel over a per-user named pipe (Windows).</summary>
[SupportedOSPlatform("windows")]
public sealed class NamedPipeChannel(string pipeName) : IInstanceChannel
{
    private readonly CancellationTokenSource _cts = new();

    public static string DefaultName()
    {
        var user = $"{Environment.UserDomainName}-{Environment.UserName}";
        var safe = new string(user.Select(c => char.IsLetterOrDigit(c) || c is '-' or '_' ? c : '_').ToArray());
        return $"Slotty-{safe}";
    }

    public async Task<bool> TrySendAsync(IpcCommand command, TimeSpan timeout, CancellationToken cancellationToken = default)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(timeout);
        try
        {
            await using var pipe = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
            await pipe.ConnectAsync(cts.Token);
            await pipe.WriteAsync(Encoding.UTF8.GetBytes(IpcProtocol.EncodeRequest(command)), cts.Token);
            await pipe.FlushAsync(cts.Token);
            using var reader = new StreamReader(pipe, Encoding.UTF8, false, 256, leaveOpen: true);
            var reply = await reader.ReadLineAsync(cts.Token);
            return reply is not null && reply.StartsWith(IpcProtocol.Ok, StringComparison.Ordinal);
        }
        catch (Exception ex) when (ex is IOException or OperationCanceledException or TimeoutException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    public bool TryStartServer(Func<IpcCommand, Task> handler)
    {
        NamedPipeServerStream first;
        try
        {
            // FirstPipeInstance fails if another process already owns the name.
            first = Create(PipeOptions.FirstPipeInstance);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
        _ = Task.Run(() => ServeLoopAsync(first, handler, _cts.Token));
        return true;
    }

    private NamedPipeServerStream Create(PipeOptions extra = PipeOptions.None) =>
        new(pipeName, PipeDirection.InOut, NamedPipeServerStream.MaxAllowedServerInstances, PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly | extra);

    private async Task ServeLoopAsync(NamedPipeServerStream server, Func<IpcCommand, Task> handler, CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                await server.WaitForConnectionAsync(ct);
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
                timeout.CancelAfter(TimeSpan.FromSeconds(3));
                using var reader = new StreamReader(server, Encoding.UTF8, false, 256, leaveOpen: true);
                var line = await reader.ReadLineAsync(timeout.Token);
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
                await server.WriteAsync(Encoding.UTF8.GetBytes(reply), timeout.Token);
                await server.FlushAsync(timeout.Token);
            }
            catch (Exception ex) when (ex is IOException or OperationCanceledException)
            {
                if (ct.IsCancellationRequested)
                    break;
            }
            finally
            {
                await server.DisposeAsync();
            }

            if (ct.IsCancellationRequested)
                return;
            try
            {
                server = Create();
            }
            catch (IOException)
            {
                return;
            }
        }
    }

    public void Dispose() => _cts.Cancel();
}
