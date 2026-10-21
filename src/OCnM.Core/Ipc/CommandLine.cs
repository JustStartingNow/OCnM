using OCnM.Core.Models;

namespace OCnM.Core.Ipc;

public sealed record CommandLineOptions
{
    /// <summary>The command to run; null for a plain launch.</summary>
    public IpcCommand? Command { get; init; }

    /// <summary>Linux backend requested with --backend; null when not given.</summary>
    public LinuxBackend? Backend { get; init; }

    public bool ShowHelp { get; init; }

    public bool ShowVersion { get; init; }

    public string? Error { get; init; }
}

public static class CommandLine
{
    public const string BackendEnvVar = "SLOTTY_BACKEND";

    public const string HelpText = """
        OCnM: Open, Copy 'n' Move
        Drop files onto a donut slice to copy or move them into a folder. 9 slices, 9 folders.

        Usage: ocnm [command] [--backend x11|wayland]

        Commands (sent to the running instance; the first launch starts it):
          --toggle      Show the donut, or hide it if it is visible
          --show        Show the donut
          --hide        Hide the donut
          --settings    Open the Settings window
          --quit        Quit the running instance

        Options:
          --backend x11|wayland   Linux display backend (also: SLOTTY_BACKEND)
          --version               Print the version
          -h, --help              Show this help
        """;

    public static CommandLineOptions Parse(IReadOnlyList<string> args)
    {
        IpcCommand? command = null;
        LinuxBackend? backend = null;
        bool help = false, version = false;

        for (var i = 0; i < args.Count; i++)
        {
            var arg = args[i];
            if (string.IsNullOrWhiteSpace(arg))
                continue;

            if (TryParseCommand(arg, out var parsed))
            {
                if (command is not null && command != parsed)
                    return Fail($"Only one command can be given (got {Format(command.Value)} and {Format(parsed)}).");
                command = parsed;
                continue;
            }

            switch (arg)
            {
                case "-h" or "--help" or "/?":
                    help = true;
                    continue;
                case "--version":
                    version = true;
                    continue;
                case "--backend":
                    if (i + 1 >= args.Count)
                        return Fail("--backend needs a value: x11 or wayland.");
                    if (!TryParseBackend(args[++i], out var b))
                        return Fail($"Unknown backend \"{args[i]}\". Use x11 or wayland.");
                    backend = b;
                    continue;
            }

            if (arg.StartsWith("--backend=", StringComparison.Ordinal))
            {
                var value = arg["--backend=".Length..];
                if (!TryParseBackend(value, out var b))
                    return Fail($"Unknown backend \"{value}\". Use x11 or wayland.");
                backend = b;
                continue;
            }

            return Fail($"Unknown argument \"{arg}\". Run ocnm --help for usage.");
        }

        return new CommandLineOptions { Command = command, Backend = backend, ShowHelp = help, ShowVersion = version };

        static CommandLineOptions Fail(string message) => new() { Error = message };
    }

    public static bool TryParseCommand(string arg, out IpcCommand command)
    {
        command = default;
        if (!arg.StartsWith("--", StringComparison.Ordinal))
            return false;
        return Enum.TryParse(arg[2..], ignoreCase: true, out command) && Enum.IsDefined(command) && !char.IsDigit(arg[2]);
    }

    public static string Format(IpcCommand command) => "--" + command.ToString().ToLowerInvariant();

    /// <summary>Accepts x11/wayland/auto, case-insensitively.</summary>
    public static bool TryParseBackend(string? value, out LinuxBackend backend)
    {
        backend = LinuxBackend.Auto;
        switch (value?.Trim().ToLowerInvariant())
        {
            case "x11" or "xwayland":
                backend = LinuxBackend.X11;
                return true;
            case "wayland":
                backend = LinuxBackend.Wayland;
                return true;
            case "auto":
                return true;
            default:
                return false;
        }
    }

    /// <summary>The backend to use: --backend beats SLOTTY_BACKEND beats the config setting.</summary>
    public static LinuxBackend ResolveBackend(LinuxBackend? fromArgs, string? fromEnv, LinuxBackend fromConfig)
    {
        if (fromArgs is { } a && a != LinuxBackend.Auto)
            return a;
        if (TryParseBackend(fromEnv, out var e) && e != LinuxBackend.Auto)
            return e;
        return fromConfig;
    }
}
