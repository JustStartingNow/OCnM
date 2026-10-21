using Avalonia;
using Slotty.App.Platform;
using Slotty.App.Services;
using Slotty.Core.Config;
using Slotty.Core.Ipc;
using Slotty.Core.Models;
using Slotty.Core.Platform;

namespace Slotty.App;

/// <summary>Everything decided before Avalonia starts.</summary>
internal sealed record StartupContext(
    CommandLineOptions Options,
    IInstanceChannel Channel,
    CommandSink Sink,
    ConfigStore Store,
    ConfigLoadResult Load,
    LinuxBackend RequestedBackend);

internal static class Program
{
    internal static StartupContext? Startup { get; private set; }

    public static string Version =>
        typeof(Program).Assembly.GetName().Version?.ToString(3) ?? "1.0.0";

    [STAThread]
    public static int Main(string[] args)
    {
        var options = CommandLine.Parse(args);
        if (options.Error is not null)
        {
            ConsoleOutput.Write(options.Error + Environment.NewLine + Environment.NewLine + CommandLine.HelpText, isError: true);
            return 2;
        }
        if (options.ShowHelp)
        {
            ConsoleOutput.Write(CommandLine.HelpText);
            return 0;
        }
        if (options.ShowVersion)
        {
            ConsoleOutput.Write($"slotty {Version}");
            return 0;
        }

        // Single instance: hand the command to a running instance if there is one.
        var channel = PlatformServices.CreateInstanceChannel();
        var forwarded = options.Command ?? IpcCommand.Show;
        if (channel.TrySendAsync(forwarded, TimeSpan.FromSeconds(2)).GetAwaiter().GetResult())
            return 0;
        if (options.Command == IpcCommand.Quit)
            return 0;

        var sink = new CommandSink();
        if (!channel.TryStartServer(sink.HandleAsync))
        {
            // Lost a start-up race; the winner should answer shortly.
            Thread.Sleep(500);
            if (channel.TrySendAsync(forwarded, TimeSpan.FromSeconds(2)).GetAwaiter().GetResult())
                return 0;
            ConsoleOutput.Write("Another Slotty instance is running but not answering.", isError: true);
            return 1;
        }

        try
        {
            var store = ConfigStore.CreateDefault();
            var load = store.Load();
            var backend = CommandLine.ResolveBackend(options.Backend,
                Environment.GetEnvironmentVariable(CommandLine.BackendEnvVar), load.Config.Settings.LinuxBackend);
            Startup = new StartupContext(options, channel, sink, store, load, backend);
            Log.Info($"Starting Slotty {Version} (session {SessionDetector.Detect()}, backend request {backend}, config {load.Status})");

            BuildAvaloniaApp(backend).StartWithClassicDesktopLifetime(args, Avalonia.Controls.ShutdownMode.OnExplicitShutdown);
            return 0;
        }
        catch (Exception ex)
        {
            Log.Error("Fatal error", ex);
            ConsoleOutput.Write($"Slotty crashed: {ex.Message}", isError: true);
            return 1;
        }
        finally
        {
            channel.Dispose();
        }
    }

    /// <summary>Used by the XAML previewer.</summary>
    public static AppBuilder BuildAvaloniaApp() => BuildAvaloniaApp(LinuxBackend.Auto);

    private static AppBuilder BuildAvaloniaApp(LinuxBackend backend)
    {
        var builder = AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .With(new Avalonia.Media.FontManagerOptions { DefaultFamilyName = "fonts:Inter#Inter" })
            .With(new X11PlatformOptions { WmClass = "slotty" })
            .LogToTrace();

        if (OperatingSystem.IsLinux() && ShouldUseWayland(backend))
        {
            // Experimental native backend; falls back to X11/XWayland if no usable compositor answers.
            builder = builder
                .UseWaylandWithFallback()
                .With(new WaylandPlatformOptions { AppId = "slotty" });
        }
        return builder;
    }

    /// <summary>Auto picks the native Wayland backend inside a Wayland session.</summary>
    internal static bool ShouldUseWayland(LinuxBackend backend) => backend switch
    {
        LinuxBackend.Wayland => true,
        LinuxBackend.X11 => false,
        _ => SessionDetector.Detect() == SessionKind.LinuxWayland,
    };
}
