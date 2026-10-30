using Avalonia;
using Microsoft.Extensions.Hosting;

namespace ModelingToolkit.Gui;

internal static class Program
{
    // Nothing that uses Avalonia may run before StartWithClassicDesktopLifetime: it is not initialized yet.
    [STAThread]
    public static int Main(string[] args)
    {
        using IHost host = AppHost.Build();
        return BuildAvaloniaApp(host.Services).StartWithClassicDesktopLifetime(args);
    }

    // Used by the XAML previewer of the IDE.
    public static AppBuilder BuildAvaloniaApp() => BuildAvaloniaApp(AppHost.Build().Services);

    private static AppBuilder BuildAvaloniaApp(IServiceProvider services) =>
        AppBuilder.Configure(() => new App(services))
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}
