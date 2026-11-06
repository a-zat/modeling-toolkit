using Microsoft.Extensions.DependencyInjection;
using ModelingToolkit.Core.IO;
using ModelingToolkit.Gui.ViewModels;
using ModelingToolkit.IO.Native;
using ModelingToolkit.Kernel.Occt;

namespace ModelingToolkit.Gui;

/// <summary>Composition of the desktop application, shared by <c>Program</c> and the tests.</summary>
internal static class AppHost
{
    public static ServiceProvider Build()
    {
        var services = new ServiceCollection();

        services.AddOpenCascade().AddNativeFileFormat().AddSingleton<DocumentFormats>();
        services.AddSingleton<IAppearance, Appearance>();
        services.AddTransient<MainWindowViewModel>();

        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true });
    }
}
