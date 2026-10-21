using Slotty.Core.Ipc;

namespace Slotty.App.Services;

/// <summary>
/// Receives IPC commands from the moment the server starts; commands that arrive before the UI is ready
/// are queued and replayed once a target is attached.
/// </summary>
internal sealed class CommandSink
{
    private readonly object _gate = new();
    private readonly List<IpcCommand> _pending = [];
    private Func<IpcCommand, Task>? _target;

    public Task HandleAsync(IpcCommand command)
    {
        Func<IpcCommand, Task>? target;
        lock (_gate)
        {
            target = _target;
            if (target is null)
            {
                _pending.Add(command);
                return Task.CompletedTask;
            }
        }
        return target(command);
    }

    public void Attach(Func<IpcCommand, Task> target)
    {
        List<IpcCommand> replay;
        lock (_gate)
        {
            _target = target;
            replay = [.. _pending];
            _pending.Clear();
        }
        foreach (var command in replay)
            _ = target(command);
    }
}
