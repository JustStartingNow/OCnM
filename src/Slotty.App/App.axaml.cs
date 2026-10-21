using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;

namespace Slotty.App;

public partial class App : Application
{
    private AppController? _controller;

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop && Program.Startup is { } startup)
        {
            _controller = new AppController(this, desktop, startup);
            desktop.Exit += (_, _) => _controller.Dispose();
            _controller.Start();
        }
        base.OnFrameworkInitializationCompleted();
    }
}
