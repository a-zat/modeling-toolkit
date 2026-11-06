using Avalonia;
using Microsoft.Extensions.DependencyInjection;

namespace ModelingToolkit.Gui;

internal static class Program
{
    // Nothing that uses Avalonia may run before StartWithClassicDesktopLifetime: it is not initialized yet.
    [STAThread]
    public static int Main(string[] args)
    {
        using ServiceProvider services = AppHost.Build();
        return BuildAvaloniaApp(services).StartWithClassicDesktopLifetime(args);
    }

    // Used by the XAML previewer of the IDE.
    public static AppBuilder BuildAvaloniaApp() => BuildAvaloniaApp(AppHost.Build());

    private static AppBuilder BuildAvaloniaApp(IServiceProvider services) =>
        AppBuilder.Configure(() => new App(services))
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}
