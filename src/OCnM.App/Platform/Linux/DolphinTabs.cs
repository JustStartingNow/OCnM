using System.Diagnostics;
using System.Text.RegularExpressions;
using OCnM.App.Services;
using Tmds.DBus.Protocol;

namespace OCnM.App.Platform.Linux;

internal sealed record DolphinTarget(string Service, int Pid, ulong? NiriWindowId);

/// <summary>
/// Opens a folder as a new tab in a Dolphin window that is already open, through Dolphin's own D-Bus interface
/// (org.kde.dolphin.MainWindow.openDirectories). Dolphin switches to an existing tab instead of duplicating it.
/// Only used when Dolphin is the default file manager; every failure falls back to a normal new window.
/// </summary>
internal static partial class DolphinTabs
{
    private const string ServicePrefix = "org.kde.dolphin-";
    private const string AppId = "org.kde.dolphin";
    private const string MainWindowInterface = "org.kde.dolphin.MainWindow";
    private static readonly TimeSpan CallTimeout = TimeSpan.FromSeconds(2);

    /// <returns>True when Dolphin took the folder; false means "open a new window the normal way".</returns>
    public static async Task<bool> TryOpenInTabAsync(string folder)
    {
        var address = Environment.GetEnvironmentVariable("DBUS_SESSION_BUS_ADDRESS");
        if (string.IsNullOrEmpty(address) || !await DefaultFileManagerIsDolphinAsync().ConfigureAwait(false))
            return false;

        try
        {
            using var connection = new DBusConnection(address);
            var work = OpenAsync(connection, folder);
            if (await Task.WhenAny(work, Task.Delay(CallTimeout)).ConfigureAwait(false) != work)
            {
                Log.Warn("Dolphin did not answer in time; opening a new window instead.");
                return false;
            }
            return await work.ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            // D-Bus being unavailable or Dolphin refusing must never stop the folder from opening.
            Log.Warn($"Could not open a tab in Dolphin ({ex.GetType().Name}: {ex.Message}); opening a new window instead.");
            return false;
        }
    }

    private static async Task<bool> OpenAsync(DBusConnection connection, string folder)
    {
        await connection.ConnectAsync().ConfigureAwait(false);
        var services = await connection.ListServicesAsync().ConfigureAwait(false);
        IReadOnlyList<NiriWindow>? windows = NiriIpc.IsAvailable ? await NiriIpc.GetWindowsAsync().ConfigureAwait(false) : null;
        if (PickTarget(services, windows) is not { } target)
            return false;

        // A Dolphin process can be a window-less daemon (started for org.freedesktop.FileManager1); it needs a window.
        var windowPath = await FirstWindowPathAsync(connection, target.Service).ConfigureAwait(false);
        if (windowPath is null)
            return false;

        await connection.CallMethodAsync(OpenDirectoriesMessage(connection, target.Service, windowPath, new Uri(folder).AbsoluteUri))
            .ConfigureAwait(false);

        // Wayland doesn't let one app raise another's window. Niri can do it for us; elsewhere Dolphin is asked to
        // activate itself, which X11 desktops (and KDE in many cases) honour.
        if (target.NiriWindowId is { } id && await NiriIpc.FocusWindowAsync(id).ConfigureAwait(false))
            return true;
        await connection.CallMethodAsync(ActivateWindowMessage(connection, target.Service, windowPath)).ConfigureAwait(false);
        return true;
    }

    // MessageWriter is a ref struct and can't live across an await, so each message is built in its own method.
    private static MessageBuffer OpenDirectoriesMessage(DBusConnection connection, string service, string path, string uri)
    {
        using var writer = connection.GetMessageWriter();
        writer.WriteMethodCallHeader(service, path, MainWindowInterface, "openDirectories", "asb", MessageFlags.None);
        writer.WriteArray(new[] { uri });
        writer.WriteBool(false); // not split view
        return writer.CreateMessage();
    }

    private static MessageBuffer ActivateWindowMessage(DBusConnection connection, string service, string path)
    {
        using var writer = connection.GetMessageWriter();
        writer.WriteMethodCallHeader(service, path, MainWindowInterface, "activateWindow", "s", MessageFlags.None);
        writer.WriteString(""); // no xdg-activation token available
        return writer.CreateMessage();
    }

    private static MessageBuffer IntrospectMessage(DBusConnection connection, string service, string path)
    {
        using var writer = connection.GetMessageWriter();
        writer.WriteMethodCallHeader(service, path, "org.freedesktop.DBus.Introspectable", "Introspect", null, MessageFlags.None);
        return writer.CreateMessage();
    }

    /// <summary>
    /// Chooses the Dolphin process to use. With Niri's window list, the Dolphin window used most recently wins and a
    /// process without any window is ignored; without it, the first Dolphin on the bus is used.
    /// </summary>
    internal static DolphinTarget? PickTarget(IEnumerable<string> services, IReadOnlyList<NiriWindow>? windows)
    {
        var candidates = new Dictionary<int, string>();
        foreach (var service in services)
        {
            if (service.StartsWith(ServicePrefix, StringComparison.Ordinal)
                && int.TryParse(service.AsSpan(ServicePrefix.Length), out var pid) && pid > 0)
                candidates.TryAdd(pid, service);
        }
        if (candidates.Count == 0)
            return null;

        if (windows is null)
        {
            var first = candidates.OrderBy(c => c.Key).First();
            return new DolphinTarget(first.Value, first.Key, null);
        }

        var best = windows
            .Where(w => w.AppId == AppId && w.Pid is { } p && candidates.ContainsKey(p))
            .OrderByDescending(w => w.FocusTime)
            .FirstOrDefault();
        return best is null ? null : new DolphinTarget(candidates[best.Pid!.Value], best.Pid.Value, best.Id);
    }

    /// <summary>Dolphin exports its windows as /dolphin/Dolphin_1, _2, … The lowest one is used.</summary>
    private static async Task<string?> FirstWindowPathAsync(DBusConnection connection, string service)
    {
        var xml = await connection.CallMethodAsync(IntrospectMessage(connection, service, "/dolphin"),
            static (Message message, object? _) => message.GetBodyReader().ReadString(), null).ConfigureAwait(false);
        return WindowPathFromIntrospection(xml);
    }

    internal static string? WindowPathFromIntrospection(string xml)
    {
        var numbers = WindowNode().Matches(xml).Select(m => int.Parse(m.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture)).ToList();
        return numbers.Count == 0 ? null : $"/dolphin/Dolphin_{numbers.Min()}";
    }

    [GeneratedRegex("""<node\s+name="Dolphin_(\d+)"\s*/?>""")]
    private static partial Regex WindowNode();

    private static async Task<bool> DefaultFileManagerIsDolphinAsync()
    {
        try
        {
            var psi = new ProcessStartInfo("xdg-mime", "query default inode/directory")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
            };
            using var process = Process.Start(psi);
            if (process is null)
                return false;
            var output = await process.StandardOutput.ReadToEndAsync().ConfigureAwait(false);
            await process.WaitForExitAsync().ConfigureAwait(false);
            return output.Contains("dolphin", StringComparison.OrdinalIgnoreCase);
        }
        catch (System.ComponentModel.Win32Exception)
        {
            return false;
        }
    }
}
