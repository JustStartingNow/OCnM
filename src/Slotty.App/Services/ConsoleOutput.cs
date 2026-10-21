using Slotty.App.Platform.Windows;

namespace Slotty.App.Services;

internal static class ConsoleOutput
{
    /// <summary>On Windows the app is a GUI exe; attach to the parent console so CLI output is visible.</summary>
    public static void Write(string text, bool isError = false)
    {
        if (OperatingSystem.IsWindows())
            Win32Native.AttachConsole(-1);
        (isError ? Console.Error : Console.Out).WriteLine(text);
    }
}
