using System.Net.Sockets;
using System.Text;
using System.Text.Json;

namespace OCnM.App.Platform.Linux;

internal sealed record NiriWindow(ulong Id, string? AppId, int? Pid, double FocusTime);

/// <summary>
/// The two Niri IPC requests OCnM needs: list windows and focus one. Niri listens on $NIRI_SOCKET and speaks one JSON
/// value per line. Used to bring a file manager window forward, which Wayland doesn't let a normal client do.
/// </summary>
internal static class NiriIpc
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(1);

    public static bool IsAvailable => !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("NIRI_SOCKET"));

    public static async Task<IReadOnlyList<NiriWindow>?> GetWindowsAsync()
    {
        var reply = await RequestAsync("\"Windows\"").ConfigureAwait(false);
        return reply is null ? null : ParseWindows(reply);
    }

    public static async Task<bool> FocusWindowAsync(ulong id)
    {
        var reply = await RequestAsync($"{{\"Action\":{{\"FocusWindow\":{{\"id\":{id}}}}}}}").ConfigureAwait(false);
        return reply is not null && reply.Contains("\"Ok\"", StringComparison.Ordinal);
    }

    /// <summary>Parses the reply to "Windows": {"Ok":{"Windows":[{"id":..,"app_id":..,"pid":..,"focus_timestamp":{..}}]}}.</summary>
    internal static IReadOnlyList<NiriWindow> ParseWindows(string json)
    {
        var result = new List<NiriWindow>();
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("Ok", out var ok) || !ok.TryGetProperty("Windows", out var windows)
                || windows.ValueKind != JsonValueKind.Array)
                return result;
            foreach (var w in windows.EnumerateArray())
            {
                if (!w.TryGetProperty("id", out var idEl) || !idEl.TryGetUInt64(out var id))
                    continue;
                var appId = w.TryGetProperty("app_id", out var a) && a.ValueKind == JsonValueKind.String ? a.GetString() : null;
                int? pid = w.TryGetProperty("pid", out var p) && p.TryGetInt32(out var pv) ? pv : null;
                double focus = 0;
                if (w.TryGetProperty("focus_timestamp", out var ts) && ts.ValueKind == JsonValueKind.Object)
                {
                    var secs = ts.TryGetProperty("secs", out var s) && s.TryGetDouble(out var sv) ? sv : 0;
                    var nanos = ts.TryGetProperty("nanos", out var n) && n.TryGetDouble(out var nv) ? nv : 0;
                    focus = secs + nanos / 1e9;
                }
                result.Add(new NiriWindow(id, appId, pid, focus));
            }
        }
        catch (JsonException)
        {
            // An unexpected reply just means "no windows known".
        }
        return result;
    }

    private static async Task<string?> RequestAsync(string request)
    {
        var path = Environment.GetEnvironmentVariable("NIRI_SOCKET");
        if (string.IsNullOrEmpty(path))
            return null;
        using var cts = new CancellationTokenSource(Timeout);
        try
        {
            using var socket = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
            await socket.ConnectAsync(new UnixDomainSocketEndPoint(path), cts.Token).ConfigureAwait(false);
            await socket.SendAsync(Encoding.UTF8.GetBytes(request + "\n"), SocketFlags.None, cts.Token).ConfigureAwait(false);
            var buffer = new byte[64 * 1024];
            using var reply = new MemoryStream();
            while (true)
            {
                var read = await socket.ReceiveAsync(buffer, SocketFlags.None, cts.Token).ConfigureAwait(false);
                if (read == 0)
                    break;
                reply.Write(buffer, 0, read);
                if (buffer[read - 1] == (byte)'\n')
                    break;
            }
            return Encoding.UTF8.GetString(reply.GetBuffer(), 0, (int)reply.Length);
        }
        catch (Exception ex) when (ex is SocketException or OperationCanceledException or IOException)
        {
            Services.Log.Warn($"Niri IPC request failed: {ex.Message}");
            return null;
        }
    }
}
