namespace OCnM.App.Platform;

/// <summary>How to start this app again (for autostart entries).</summary>
public static class LaunchCommand
{
    /// <summary>The executable plus leading arguments. Under "dotnet slotty.dll" that is dotnet + the dll.</summary>
    public static (string Executable, IReadOnlyList<string> Arguments) Current()
    {
        var process = Environment.ProcessPath ?? "slotty";
        var name = Path.GetFileNameWithoutExtension(process);
        if (string.Equals(name, "dotnet", StringComparison.OrdinalIgnoreCase))
        {
            var dll = Path.Combine(AppContext.BaseDirectory, "slotty.dll");
            return (process, [dll]);
        }
        return (process, []);
    }
}
