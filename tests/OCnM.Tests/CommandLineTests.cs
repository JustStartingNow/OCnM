using OCnM.Core.Ipc;
using OCnM.Core.Models;

namespace OCnM.Tests;

public class CommandLineTests
{
    [Theory]
    [InlineData("--toggle", IpcCommand.Toggle)]
    [InlineData("--show", IpcCommand.Show)]
    [InlineData("--hide", IpcCommand.Hide)]
    [InlineData("--settings", IpcCommand.Settings)]
    [InlineData("--quit", IpcCommand.Quit)]
    [InlineData("--TOGGLE", IpcCommand.Toggle)]
    public void Parses_commands(string arg, IpcCommand expected)
    {
        var o = CommandLine.Parse([arg]);
        Assert.Null(o.Error);
        Assert.Equal(expected, o.Command);
    }

    [Fact]
    public void No_arguments_is_a_plain_launch()
    {
        var o = CommandLine.Parse([]);
        Assert.Null(o.Error);
        Assert.Null(o.Command);
        Assert.Null(o.Backend);
    }

    [Theory]
    [InlineData(new[] { "--backend", "x11" }, LinuxBackend.X11)]
    [InlineData(new[] { "--backend", "Wayland" }, LinuxBackend.Wayland)]
    [InlineData(new[] { "--backend=wayland" }, LinuxBackend.Wayland)]
    [InlineData(new[] { "--toggle", "--backend", "x11" }, LinuxBackend.X11)]
    public void Parses_backend(string[] args, LinuxBackend expected)
    {
        var o = CommandLine.Parse(args);
        Assert.Null(o.Error);
        Assert.Equal(expected, o.Backend);
    }

    [Theory]
    [InlineData("--backend")]
    [InlineData("--backend mir")]
    [InlineData("--backend=")]
    [InlineData("--frobnicate")]
    [InlineData("toggle")]
    [InlineData("--toggle --quit")]
    [InlineData("--1")]
    [InlineData("--")]
    public void Rejects_bad_input(string commandLine)
    {
        Assert.NotNull(CommandLine.Parse(commandLine.Split(' ')).Error);
    }

    [Fact]
    public void Repeating_the_same_command_is_fine()
    {
        Assert.Equal(IpcCommand.Show, CommandLine.Parse(["--show", "--show"]).Command);
    }

    [Fact]
    public void Help_and_version_flags()
    {
        Assert.True(CommandLine.Parse(["-h"]).ShowHelp);
        Assert.True(CommandLine.Parse(["--help"]).ShowHelp);
        Assert.True(CommandLine.Parse(["--version"]).ShowVersion);
    }

    [Theory]
    [InlineData(LinuxBackend.X11, "wayland", LinuxBackend.Wayland, LinuxBackend.X11)]
    [InlineData(null, "wayland", LinuxBackend.X11, LinuxBackend.Wayland)]
    [InlineData(null, "bogus", LinuxBackend.X11, LinuxBackend.X11)]
    [InlineData(null, null, LinuxBackend.Auto, LinuxBackend.Auto)]
    [InlineData(LinuxBackend.Auto, "x11", LinuxBackend.Wayland, LinuxBackend.X11)]
    public void ResolveBackend_precedence(LinuxBackend? arg, string? env, LinuxBackend config, LinuxBackend expected)
    {
        Assert.Equal(expected, CommandLine.ResolveBackend(arg, env, config));
    }
}

public class IpcProtocolTests
{
    [Theory]
    [InlineData(IpcCommand.Toggle)]
    [InlineData(IpcCommand.Show)]
    [InlineData(IpcCommand.Hide)]
    [InlineData(IpcCommand.Settings)]
    [InlineData(IpcCommand.Quit)]
    public void Round_trips(IpcCommand command)
    {
        var line = IpcProtocol.EncodeRequest(command);
        Assert.EndsWith("\n", line);
        Assert.True(IpcProtocol.TryDecodeRequest(line, out var decoded));
        Assert.Equal(command, decoded);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("toggle")]
    [InlineData("slotty/2 toggle")]
    [InlineData("slotty/1 explode")]
    [InlineData("slotty/1 toggle extra")]
    [InlineData("slotty/1 7")]
    [InlineData("GET / HTTP/1.1")]
    public void Rejects_garbage(string? line)
    {
        Assert.False(IpcProtocol.TryDecodeRequest(line, out _));
    }

    [Fact]
    public void Rejects_overlong_lines()
    {
        Assert.False(IpcProtocol.TryDecodeRequest("slotty/1 toggle" + new string(' ', 300), out _));
    }

    [Fact]
    public void Error_replies_are_single_line()
    {
        Assert.Equal("error a b\n", IpcProtocol.EncodeError("a\nb"));
    }
}
