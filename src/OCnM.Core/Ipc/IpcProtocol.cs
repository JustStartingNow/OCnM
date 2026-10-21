namespace OCnM.Core.Ipc;

/// <summary>
/// One request line per connection: "ocnm/1 &lt;command&gt;\n". The server answers "ok\n" or "error &lt;message&gt;\n".
/// </summary>
public static class IpcProtocol
{
    public const string Magic = "ocnm/1";
    public const string Ok = "ok";
    public const int MaxLineLength = 256;

    public static string EncodeRequest(IpcCommand command) => $"{Magic} {command.ToString().ToLowerInvariant()}\n";

    public static bool TryDecodeRequest(string? line, out IpcCommand command)
    {
        command = default;
        if (string.IsNullOrEmpty(line) || line.Length > MaxLineLength)
            return false;
        var parts = line.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 2 || parts[0] != Magic)
            return false;
        return CommandLine.TryParseCommand("--" + parts[1], out command);
    }

    public static string EncodeError(string message) => $"error {message.ReplaceLineEndings(" ")}\n";
}
